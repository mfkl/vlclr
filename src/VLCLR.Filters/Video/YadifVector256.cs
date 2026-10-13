// Yadif's line kernel with 256-bit vectors: YadifVector128 at twice the
// width, which VLC's assembly does not have.
//
// GPL-2.0-or-later, like the FFmpeg C code it is ported from.

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace VLCLR.Filters.Video;

/// <summary>
/// <see cref="YadifVector128"/> on 16 pixels at a time: AVX2.
/// </summary>
internal static unsafe class YadifVector256
{
    public const int MaxBitDepth = 12;

    /// <summary>Whether the build and the CPU have AVX2.</summary>
    public static bool IsSupported => Avx2.IsSupported;

    /// <inheritdoc cref="YadifReference.Line{T}"/>
    public static void Line<T>(T* dst, T* prev, T* cur, T* next, int w, int prefs, int mrefs, int parity, int mode)
        where T : unmanaged
    {
        const int Lanes = 16;
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
            Vector256<short> c = Load(cx + m);
            Vector256<short> e = Load(cx + p);
            Vector256<short> p2 = Load(prev2 + x);
            Vector256<short> n2 = Load(next2 + x);
            Vector256<short> d = (p2 + n2) >> 1;
            Vector256<short> temporalDiff0 = Vector256.Abs(p2 - n2);
            Vector256<short> temporalDiff1 = (Vector256.Abs(Load(prev + x + m) - c) + Vector256.Abs(Load(prev + x + p) - e)) >> 1;
            Vector256<short> temporalDiff2 = (Vector256.Abs(Load(next + x + m) - c) + Vector256.Abs(Load(next + x + p) - e)) >> 1;
            Vector256<short> diff = Vector256.Max(Vector256.Max(temporalDiff0 >> 1, temporalDiff1), temporalDiff2);

            Vector256<short> spatialPred = (c + e) >> 1;
            Vector256<short> spatialScore = Vector256.Abs(Load(cx + m - 1) - Load(cx + p - 1)) + Vector256.Abs(c - e)
                + Vector256.Abs(Load(cx + m + 1) - Load(cx + p + 1)) - Vector256<short>.One;

            // CHECK(-1) CHECK(-2) }}: the second check only counts where the first won.
            Vector256<short> won = Check(cx, m - 1, p + 1, Vector256<short>.AllBitsSet, ref spatialScore, ref spatialPred);
            Check(cx, m - 2, p + 2, won, ref spatialScore, ref spatialPred);
            // CHECK(1) CHECK(2) }}
            won = Check(cx, m + 1, p - 1, Vector256<short>.AllBitsSet, ref spatialScore, ref spatialPred);
            Check(cx, m + 2, p - 2, won, ref spatialScore, ref spatialPred);

            if (!edge)
            {
                Vector256<short> b = (Load(prev2 + x + 2 * m) + Load(next2 + x + 2 * m)) >> 1;
                Vector256<short> f = (Load(prev2 + x + 2 * p) + Load(next2 + x + 2 * p)) >> 1;
                Vector256<short> de = d - e, dc = d - c, bc = b - c, fe = f - e;
                Vector256<short> max = Vector256.Max(Vector256.Max(de, dc), Vector256.Min(bc, fe));
                Vector256<short> min = Vector256.Min(Vector256.Min(de, dc), Vector256.Max(bc, fe));
                diff = Vector256.Max(Vector256.Max(diff, min), -max);
            }

            // diff >= 0, so the clamp is a max then a min.
            Store(dst + x, Vector256.Min(Vector256.Max(spatialPred, d - diff), d + diff));
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
    private static Vector256<short> Check<T>(T* cx, nint o1, nint o2, Vector256<short> enabled,
        ref Vector256<short> spatialScore, ref Vector256<short> spatialPred)
        where T : unmanaged
    {
        Vector256<short> a = Load(cx + o1);
        Vector256<short> b = Load(cx + o2);
        Vector256<short> score = Vector256.Abs(Load(cx + o1 - 1) - Load(cx + o2 - 1)) + Vector256.Abs(a - b)
            + Vector256.Abs(Load(cx + o1 + 1) - Load(cx + o2 + 1));
        Vector256<short> won = Vector256.LessThan(score, spatialScore) & enabled;
        spatialScore = Vector256.ConditionalSelect(won, score, spatialScore);
        spatialPred = Vector256.ConditionalSelect(won, (a + b) >> 1, spatialPred);
        return won;
    }

    // 16 samples widened to 16-bit lanes (vpmovzxbw from memory).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<short> Load<T>(T* p) where T : unmanaged
        => typeof(T) == typeof(byte)
            ? Avx2.ConvertToVector256Int16((byte*)p)
            : Vector256.Load((short*)p);

    // Results are within the input range, so narrowing does not clip.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store<T>(T* p, Vector256<short> value) where T : unmanaged
    {
        if (typeof(T) == typeof(byte))
        {
            Vector256<byte> bytes = Vector256.Narrow(value.AsUInt16(), value.AsUInt16());
            bytes.GetLower().Store((byte*)p);
        }
        else
        {
            value.Store((short*)p);
        }
    }
}
