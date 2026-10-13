// Yadif's line kernel with 512-bit vectors: YadifVector128 at four times the
// width, which VLC's assembly does not have.
//
// GPL-2.0-or-later, like the FFmpeg C code it is ported from.

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace VLCLR.Filters.Video;

/// <summary>
/// <see cref="YadifVector128"/> on 32 pixels at a time: AVX-512 (BW for
/// 16-bit lanes).
/// </summary>
internal static unsafe class YadifVector512
{
    public const int MaxBitDepth = 12;

    /// <summary>Whether the build and the CPU have AVX-512 BW.</summary>
    public static bool IsSupported => Avx512BW.IsSupported;

    /// <inheritdoc cref="YadifReference.Line{T}"/>
    public static void Line<T>(T* dst, T* prev, T* cur, T* next, int w, int prefs, int mrefs, int parity, int mode)
        where T : unmanaged
    {
        const int Lanes = 32;
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
            Vector512<short> c = Load(cx + m);
            Vector512<short> e = Load(cx + p);
            Vector512<short> p2 = Load(prev2 + x);
            Vector512<short> n2 = Load(next2 + x);
            Vector512<short> d = (p2 + n2) >> 1;
            Vector512<short> temporalDiff0 = Vector512.Abs(p2 - n2);
            Vector512<short> temporalDiff1 = (Vector512.Abs(Load(prev + x + m) - c) + Vector512.Abs(Load(prev + x + p) - e)) >> 1;
            Vector512<short> temporalDiff2 = (Vector512.Abs(Load(next + x + m) - c) + Vector512.Abs(Load(next + x + p) - e)) >> 1;
            Vector512<short> diff = Vector512.Max(Vector512.Max(temporalDiff0 >> 1, temporalDiff1), temporalDiff2);

            Vector512<short> spatialPred = (c + e) >> 1;
            Vector512<short> spatialScore = Vector512.Abs(Load(cx + m - 1) - Load(cx + p - 1)) + Vector512.Abs(c - e)
                + Vector512.Abs(Load(cx + m + 1) - Load(cx + p + 1)) - Vector512<short>.One;

            // CHECK(-1) CHECK(-2) }}: the second check only counts where the first won.
            Vector512<short> won = Check(cx, m - 1, p + 1, Vector512<short>.AllBitsSet, ref spatialScore, ref spatialPred);
            Check(cx, m - 2, p + 2, won, ref spatialScore, ref spatialPred);
            // CHECK(1) CHECK(2) }}
            won = Check(cx, m + 1, p - 1, Vector512<short>.AllBitsSet, ref spatialScore, ref spatialPred);
            Check(cx, m + 2, p - 2, won, ref spatialScore, ref spatialPred);

            if (!edge)
            {
                Vector512<short> b = (Load(prev2 + x + 2 * m) + Load(next2 + x + 2 * m)) >> 1;
                Vector512<short> f = (Load(prev2 + x + 2 * p) + Load(next2 + x + 2 * p)) >> 1;
                Vector512<short> de = d - e, dc = d - c, bc = b - c, fe = f - e;
                Vector512<short> max = Vector512.Max(Vector512.Max(de, dc), Vector512.Min(bc, fe));
                Vector512<short> min = Vector512.Min(Vector512.Min(de, dc), Vector512.Max(bc, fe));
                diff = Vector512.Max(Vector512.Max(diff, min), -max);
            }

            // diff >= 0, so the clamp is a max then a min.
            Store(dst + x, Vector512.Min(Vector512.Max(spatialPred, d - diff), d + diff));
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
    private static Vector512<short> Check<T>(T* cx, nint o1, nint o2, Vector512<short> enabled,
        ref Vector512<short> spatialScore, ref Vector512<short> spatialPred)
        where T : unmanaged
    {
        Vector512<short> a = Load(cx + o1);
        Vector512<short> b = Load(cx + o2);
        Vector512<short> score = Vector512.Abs(Load(cx + o1 - 1) - Load(cx + o2 - 1)) + Vector512.Abs(a - b)
            + Vector512.Abs(Load(cx + o1 + 1) - Load(cx + o2 + 1));
        Vector512<short> won = Vector512.LessThan(score, spatialScore) & enabled;
        spatialScore = Vector512.ConditionalSelect(won, score, spatialScore);
        spatialPred = Vector512.ConditionalSelect(won, (a + b) >> 1, spatialPred);
        return won;
    }

    // 32 samples widened to 16-bit lanes (vpmovzxbw from memory).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector512<short> Load<T>(T* p) where T : unmanaged
        => typeof(T) == typeof(byte)
            ? Avx512BW.ConvertToVector512Int16(Vector256.Load((byte*)p))
            : Vector512.Load((short*)p);

    // Results are within the input range, so narrowing does not clip
    // (vpmovwb).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store<T>(T* p, Vector512<short> value) where T : unmanaged
    {
        if (typeof(T) == typeof(byte))
        {
            Avx512BW.ConvertToVector256Byte(value).Store((byte*)p);
        }
        else
        {
            value.Store((short*)p);
        }
    }
}
