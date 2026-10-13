// Yadif's line kernel with 128-bit vectors: the structure of VLC's SSSE3
// assembly (yadif_x86.asm), written with the cross-platform Vector128 API.
//
// GPL-2.0-or-later, like the FFmpeg C code it is ported from.

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace VLCLR.Filters.Video;

/// <summary>
/// Yadif's line kernel on 8 pixels at a time in 16-bit lanes, like VLC's
/// SSSE3 assembly: samples widened to 16 bits, every diagonal check computed
/// and merged with masks instead of the C's nested ifs. Exact for up to 12
/// bits per sample (the largest score, 3 x 4095, fits a 16-bit lane).
/// Cross-platform: SSE on x86, NEON on ARM64.
/// </summary>
internal static unsafe class YadifVector128
{
    public const int MaxBitDepth = 12;

    /// <summary>Whether the build and the CPU accelerate 128-bit vectors.</summary>
    public static bool IsSupported => Vector128.IsHardwareAccelerated;

    /// <inheritdoc cref="YadifReference.Line{T}"/>
    public static void Line<T>(T* dst, T* prev, T* cur, T* next, int w, int prefs, int mrefs, int parity, int mode)
        where T : unmanaged
    {
        const int Lanes = 8;
        if (w < Lanes)
        {
            YadifScalar.Line(dst, prev, cur, next, w, prefs, mrefs, parity, mode);
            return;
        }
        T* prev2 = parity != 0 ? prev : cur;
        T* next2 = parity != 0 ? cur : next;
        nint p = prefs, m = mrefs;
        bool edge = mode >= 2;
        nint last = w - Lanes;
        for (nint x = 0; ; x += Lanes)
        {
            // The last block overlaps the one before instead of running past
            // the line: the output only depends on the inputs.
            if (x > last)
            {
                x = last;
            }
            T* cx = cur + x;
            Vector128<short> c = Load(cx + m);
            Vector128<short> e = Load(cx + p);
            Vector128<short> p2 = Load(prev2 + x);
            Vector128<short> n2 = Load(next2 + x);
            Vector128<short> d = (p2 + n2) >> 1;
            Vector128<short> temporalDiff0 = Vector128.Abs(p2 - n2);
            Vector128<short> temporalDiff1 = (Vector128.Abs(Load(prev + x + m) - c) + Vector128.Abs(Load(prev + x + p) - e)) >> 1;
            Vector128<short> temporalDiff2 = (Vector128.Abs(Load(next + x + m) - c) + Vector128.Abs(Load(next + x + p) - e)) >> 1;
            Vector128<short> diff = Vector128.Max(Vector128.Max(temporalDiff0 >> 1, temporalDiff1), temporalDiff2);

            Vector128<short> spatialPred = (c + e) >> 1;
            Vector128<short> spatialScore = Vector128.Abs(Load(cx + m - 1) - Load(cx + p - 1)) + Vector128.Abs(c - e)
                + Vector128.Abs(Load(cx + m + 1) - Load(cx + p + 1)) - Vector128<short>.One;

            // CHECK(-1) CHECK(-2) }}: the second check only counts where the first won.
            Vector128<short> won = Check(cx, m - 1, p + 1, Vector128<short>.AllBitsSet, ref spatialScore, ref spatialPred);
            Check(cx, m - 2, p + 2, won, ref spatialScore, ref spatialPred);
            // CHECK(1) CHECK(2) }}
            won = Check(cx, m + 1, p - 1, Vector128<short>.AllBitsSet, ref spatialScore, ref spatialPred);
            Check(cx, m + 2, p - 2, won, ref spatialScore, ref spatialPred);

            if (!edge)
            {
                Vector128<short> b = (Load(prev2 + x + 2 * m) + Load(next2 + x + 2 * m)) >> 1;
                Vector128<short> f = (Load(prev2 + x + 2 * p) + Load(next2 + x + 2 * p)) >> 1;
                Vector128<short> de = d - e, dc = d - c, bc = b - c, fe = f - e;
                Vector128<short> max = Vector128.Max(Vector128.Max(de, dc), Vector128.Min(bc, fe));
                Vector128<short> min = Vector128.Min(Vector128.Min(de, dc), Vector128.Max(bc, fe));
                diff = Vector128.Max(Vector128.Max(diff, min), -max);
            }

            // diff >= 0, so the clamp is a max then a min.
            Store(dst + x, Vector128.Min(Vector128.Max(spatialPred, d - diff), d + diff));
            if (x == last)
            {
                break;
            }
        }
    }

    // CHECK(j) for every lane, with o1 = mrefs + j and o2 = prefs - j:
    // the score of the diagonal through cur[o1] and cur[o2], and where it
    // beats the best so far (and the lane is enabled), that diagonal's
    // average as the prediction. Returns the lanes where it won.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> Check<T>(T* cx, nint o1, nint o2, Vector128<short> enabled,
        ref Vector128<short> spatialScore, ref Vector128<short> spatialPred)
        where T : unmanaged
    {
        Vector128<short> a = Load(cx + o1);
        Vector128<short> b = Load(cx + o2);
        Vector128<short> score = Vector128.Abs(Load(cx + o1 - 1) - Load(cx + o2 - 1)) + Vector128.Abs(a - b)
            + Vector128.Abs(Load(cx + o1 + 1) - Load(cx + o2 + 1));
        Vector128<short> won = Vector128.LessThan(score, spatialScore) & enabled;
        spatialScore = Vector128.ConditionalSelect(won, score, spatialScore);
        spatialPred = Vector128.ConditionalSelect(won, (a + b) >> 1, spatialPred);
        return won;
    }

    // 8 samples widened to 16-bit lanes.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> Load<T>(T* p) where T : unmanaged
        => typeof(T) == typeof(byte)
            ? Vector128.WidenLower(Vector128.CreateScalarUnsafe(Unsafe.ReadUnaligned<ulong>(p)).AsByte()).AsInt16()
            : Vector128.Load((short*)p);

    // Results are within the input range, so narrowing does not clip.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store<T>(T* p, Vector128<short> value) where T : unmanaged
    {
        if (typeof(T) == typeof(byte))
        {
            Vector128<byte> bytes = Vector128.Narrow(value.AsUInt16(), value.AsUInt16());
            Unsafe.WriteUnaligned(p, bytes.AsUInt64().ToScalar());
        }
        else
        {
            value.Store((short*)p);
        }
    }
}
