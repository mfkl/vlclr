// Yadif's line kernel (YadifReference) written the way RyuJIT compiles well.
//
// GPL-2.0-or-later, like the FFmpeg C code it is ported from.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace VLCLR.Filters.Video;

/// <summary>
/// <see cref="YadifReference"/> with the same arithmetic, written for RyuJIT.
/// Measured against VLC's C compiled by clang (benchmarks/YadifCompare), each
/// change below closed part of a 3x gap:
/// <list type="bullet">
/// <item>No loop around the per-pixel code. RyuJIT does not turn ifs, min
/// and max into conditional moves inside loops (ifconversion.cpp: hot
/// blocks are skipped), and branches on pixel values mispredict. The line
/// loop calls a method that computes two pixels and has no loop: there it
/// does.</item>
/// <item>Field parity and edge mode as compile-time constants: one method per
/// combination, so prev2/next2 are not two more pointers in registers and
/// the edge test disappears.</item>
/// <item>Samples held as <see cref="nint"/>: byte and ushort loads zero-extend
/// to 64 bits for free, no value lives in two widths, and no sign extension
/// before indexing.</item>
/// <item>For 8-bit samples, |a - b| from a 511-entry table: one load instead of
/// five instructions and a temporary register, which RyuJIT's allocator is
/// short of. (The table speeds up the C too, by less.)</item>
/// <item>Absolute values without Math.Abs's overflow check; the clamp as max
/// then min (diff is never negative).</item>
/// </list>
/// Scalar, no SIMD, exact for every bit depth.
/// </summary>
internal static unsafe class YadifScalar
{
    /// <inheritdoc cref="YadifReference.Line{T}"/>
    public static void Line<T>(T* dst, T* prev, T* cur, T* next, int w, int prefs, int mrefs, int parity, int mode)
        where T : unmanaged
    {
        nint p = prefs, m = mrefs;
        nint x = 0;
        if (parity != 0)
        {
            if (mode < 2)
            {
                for (; x <= w - 2; x += 2)
                {
                    PairParity1Mode0(dst + x, cur + x, prev + x, next + x, p, m);
                }
            }
            else
            {
                for (; x <= w - 2; x += 2)
                {
                    PairParity1Mode2(dst + x, cur + x, prev + x, next + x, p, m);
                }
            }
        }
        else
        {
            if (mode < 2)
            {
                for (; x <= w - 2; x += 2)
                {
                    PairParity0Mode0(dst + x, cur + x, prev + x, next + x, p, m);
                }
            }
            else
            {
                for (; x <= w - 2; x += 2)
                {
                    PairParity0Mode2(dst + x, cur + x, prev + x, next + x, p, m);
                }
            }
        }
        if (x < w)
        {
            Single(dst + x, cur + x, prev + x, next + x, p, m, parity, mode);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PairParity1Mode0<T>(T* dst, T* cur, T* prev, T* next, nint p, nint m) where T : unmanaged
    {
        byte* t = Table;
        YadifReference.Store(dst, (int)Pixel(true, 0, t, cur, prev, next, p, m));
        YadifReference.Store(dst + 1, (int)Pixel(true, 0, t, cur + 1, prev + 1, next + 1, p, m));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PairParity1Mode2<T>(T* dst, T* cur, T* prev, T* next, nint p, nint m) where T : unmanaged
    {
        byte* t = Table;
        YadifReference.Store(dst, (int)Pixel(true, 2, t, cur, prev, next, p, m));
        YadifReference.Store(dst + 1, (int)Pixel(true, 2, t, cur + 1, prev + 1, next + 1, p, m));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PairParity0Mode0<T>(T* dst, T* cur, T* prev, T* next, nint p, nint m) where T : unmanaged
    {
        byte* t = Table;
        YadifReference.Store(dst, (int)Pixel(false, 0, t, cur, prev, next, p, m));
        YadifReference.Store(dst + 1, (int)Pixel(false, 0, t, cur + 1, prev + 1, next + 1, p, m));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PairParity0Mode2<T>(T* dst, T* cur, T* prev, T* next, nint p, nint m) where T : unmanaged
    {
        byte* t = Table;
        YadifReference.Store(dst, (int)Pixel(false, 2, t, cur, prev, next, p, m));
        YadifReference.Store(dst + 1, (int)Pixel(false, 2, t, cur + 1, prev + 1, next + 1, p, m));
    }

    // The last pixel of an odd-width line.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Single<T>(T* dst, T* cur, T* prev, T* next, nint p, nint m, int parity, int mode) where T : unmanaged
    {
        byte* t = Table;
        nint value = parity != 0
            ? (mode < 2 ? Pixel(true, 0, t, cur, prev, next, p, m) : Pixel(true, 2, t, cur, prev, next, p, m))
            : (mode < 2 ? Pixel(false, 0, t, cur, prev, next, p, m) : Pixel(false, 2, t, cur, prev, next, p, m));
        YadifReference.Store(dst, (int)value);
    }

    // One output sample; every pointer is at its x. parity and mode are
    // constants at every call site.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nint Pixel<T>(bool parity, int mode, byte* t, T* cur, T* prev, T* next, nint p, nint m)
        where T : unmanaged
    {
        T* prev2 = parity ? prev : cur;
        T* next2 = parity ? cur : next;
        nint c = Load(cur + m);
        nint e = Load(cur + p);
        nint p2 = Load(prev2);
        nint n2 = Load(next2);
        nint d = (p2 + n2) >> 1;
        nint temporal_diff0 = AbsDiff<T>(t, p2, n2);
        nint temporal_diff1 = (AbsDiff<T>(t, Load(prev + m), c) + AbsDiff<T>(t, Load(prev + p), e)) >> 1;
        nint temporal_diff2 = (AbsDiff<T>(t, Load(next + m), c) + AbsDiff<T>(t, Load(next + p), e)) >> 1;
        nint diff = Max(Max(temporal_diff0 >> 1, temporal_diff1), temporal_diff2);
        nint spatial_pred = (c + e) >> 1;
        nint spatial_score = AbsDiff<T>(t, Load(cur + m - 1), Load(cur + p - 1)) + AbsDiff<T>(t, c, e)
                           + AbsDiff<T>(t, Load(cur + m + 1), Load(cur + p + 1)) - 1;

        // CHECK(-1) CHECK(-2) }}. These stay branches, in clang too: on real
        // pictures they are predictable, and computing every score costs more.
        nint score = AbsDiff<T>(t, Load(cur + m - 2), Load(cur + p)) + AbsDiff<T>(t, Load(cur + m - 1), Load(cur + p + 1))
                   + AbsDiff<T>(t, Load(cur + m), Load(cur + p + 2));
        if (score < spatial_score)
        {
            spatial_score = score;
            spatial_pred = (Load(cur + m - 1) + Load(cur + p + 1)) >> 1;
            score = AbsDiff<T>(t, Load(cur + m - 3), Load(cur + p + 1)) + AbsDiff<T>(t, Load(cur + m - 2), Load(cur + p + 2))
                  + AbsDiff<T>(t, Load(cur + m - 1), Load(cur + p + 3));
            if (score < spatial_score)
            {
                spatial_score = score;
                spatial_pred = (Load(cur + m - 2) + Load(cur + p + 2)) >> 1;
            }
        }
        // CHECK(1) CHECK(2) }}
        score = AbsDiff<T>(t, Load(cur + m), Load(cur + p - 2)) + AbsDiff<T>(t, Load(cur + m + 1), Load(cur + p - 1))
              + AbsDiff<T>(t, Load(cur + m + 2), Load(cur + p));
        if (score < spatial_score)
        {
            spatial_score = score;
            spatial_pred = (Load(cur + m + 1) + Load(cur + p - 1)) >> 1;
            score = AbsDiff<T>(t, Load(cur + m + 1), Load(cur + p - 3)) + AbsDiff<T>(t, Load(cur + m + 2), Load(cur + p - 2))
                  + AbsDiff<T>(t, Load(cur + m + 3), Load(cur + p - 1));
            if (score < spatial_score)
            {
                spatial_pred = (Load(cur + m + 2) + Load(cur + p - 2)) >> 1;
            }
        }

        if (mode < 2)
        {
            nint b = (Load(prev2 + 2 * m) + Load(next2 + 2 * m)) >> 1;
            nint f = (Load(prev2 + 2 * p) + Load(next2 + 2 * p)) >> 1;
            nint max = Max(Max(d - e, d - c), Min(b - c, f - e));
            nint min = Min(Min(d - e, d - c), Max(b - c, f - e));
            diff = Max(Max(diff, min), -max);
        }

        // diff >= 0, so the clamp is a max then a min.
        return Min(Max(spatial_pred, d - diff), d + diff);
    }

    // A sample, zero-extended.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nint Load<T>(T* p) where T : unmanaged
        => typeof(T) == typeof(byte) ? *(byte*)p : *(ushort*)p;

    // |a - b|: from AbsTable for bytes, computed for 16-bit samples (a table
    // would need 2^17 entries).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nint AbsDiff<T>(byte* t, nint a, nint b) where T : unmanaged
    {
        if (typeof(T) == typeof(byte))
        {
            return t[a - b];
        }
        nint v = a - b;
        return v < 0 ? -v : v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nint Max(nint a, nint b) => a > b ? a : b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nint Min(nint a, nint b) => a < b ? a : b;

    // The middle of AbsTable, read once per call: as a local, the address
    // stays in a register instead of being rematerialized for every lookup.
    private static byte* Table
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(AbsTable)) + 255;
    }

    // |i - 255| for i in 0..510: the absolute difference of two bytes a and b
    // is AbsTable[a - b + 255]. Stored in the image's read-only data.
    private static ReadOnlySpan<byte> AbsTable =>
    [
        255, 254, 253, 252, 251, 250, 249, 248, 247, 246, 245, 244, 243, 242, 241, 240,
        239, 238, 237, 236, 235, 234, 233, 232, 231, 230, 229, 228, 227, 226, 225, 224,
        223, 222, 221, 220, 219, 218, 217, 216, 215, 214, 213, 212, 211, 210, 209, 208,
        207, 206, 205, 204, 203, 202, 201, 200, 199, 198, 197, 196, 195, 194, 193, 192,
        191, 190, 189, 188, 187, 186, 185, 184, 183, 182, 181, 180, 179, 178, 177, 176,
        175, 174, 173, 172, 171, 170, 169, 168, 167, 166, 165, 164, 163, 162, 161, 160,
        159, 158, 157, 156, 155, 154, 153, 152, 151, 150, 149, 148, 147, 146, 145, 144,
        143, 142, 141, 140, 139, 138, 137, 136, 135, 134, 133, 132, 131, 130, 129, 128,
        127, 126, 125, 124, 123, 122, 121, 120, 119, 118, 117, 116, 115, 114, 113, 112,
        111, 110, 109, 108, 107, 106, 105, 104, 103, 102, 101, 100, 99, 98, 97, 96,
        95, 94, 93, 92, 91, 90, 89, 88, 87, 86, 85, 84, 83, 82, 81, 80,
        79, 78, 77, 76, 75, 74, 73, 72, 71, 70, 69, 68, 67, 66, 65, 64,
        63, 62, 61, 60, 59, 58, 57, 56, 55, 54, 53, 52, 51, 50, 49, 48,
        47, 46, 45, 44, 43, 42, 41, 40, 39, 38, 37, 36, 35, 34, 33, 32,
        31, 30, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20, 19, 18, 17, 16,
        15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0,
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
        17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32,
        33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48,
        49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64,
        65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80,
        81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96,
        97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112,
        113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 127, 128,
        129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144,
        145, 146, 147, 148, 149, 150, 151, 152, 153, 154, 155, 156, 157, 158, 159, 160,
        161, 162, 163, 164, 165, 166, 167, 168, 169, 170, 171, 172, 173, 174, 175, 176,
        177, 178, 179, 180, 181, 182, 183, 184, 185, 186, 187, 188, 189, 190, 191, 192,
        193, 194, 195, 196, 197, 198, 199, 200, 201, 202, 203, 204, 205, 206, 207, 208,
        209, 210, 211, 212, 213, 214, 215, 216, 217, 218, 219, 220, 221, 222, 223, 224,
        225, 226, 227, 228, 229, 230, 231, 232, 233, 234, 235, 236, 237, 238, 239, 240,
        241, 242, 243, 244, 245, 246, 247, 248, 249, 250, 251, 252, 253, 254, 255,
    ];
}
