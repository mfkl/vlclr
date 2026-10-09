// AVX2 version, bit-exact with VLC's C code. Single thread.
//
// Builds on the tuned scalar version (interior/border split, symmetric taps,
// row-wise vertical pass) and adds:
//
// 1. 16-bit horizontal pass. VLC's 8.8 weights add up to at most 256
//    (checked at setup), so a horizontal sum is at most 255 * 256 = 65280:
//    it fits an unsigned 16-bit lane. One instruction then works on 16
//    pixels instead of 8, and the intermediate buffer is half the size.
//    Lane arithmetic wraps modulo 2^16, which is harmless because the final
//    sum fits.
// 2. Merged taps for subsampled planes. Chroma reads p[x >> 1]: two taps hit
//    the same pixel, so their weights are added once at setup and chroma
//    does half the multiplications. Same for rows (y >> 1).
// 3. Vertical pass on blocks of 32 columns kept in four registers, the
//    buffer read as 16-bit and widened to 32-bit.
// 4. Division without a divide instruction. Every vertical sum is below
//    255 * 256^2 < 2^24, so it is exact in single-precision float: the
//    quotient is trunc(value * (1/S)) in float, then corrected by one step
//    in integers (the float result is off by at most one), which makes it
//    exactly value / S. 1/S comes from a per-pixel table built at setup.
//
// Kernels whose weights add up to more than 256 (sigma below about 0.6) do
// not fit 16 bits and use the tuned scalar version instead.
//
// LGPL-2.1-or-later, like the VLC source it is ported from.

using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace VLCLR.Filters.Video;

internal static unsafe class GaussianBlurSimd
{
    public static bool IsSupported => Avx2.IsSupported;

    /// <summary>True when every sum fits the 16-bit horizontal lanes.</summary>
    public static bool Fits(int[] distribution)
    {
        long sum = 0;
        foreach (int weight in distribution)
        {
            if (weight < 0) return false;
            sum += weight;
        }
        return sum > 0 && sum <= 256;
    }

    /// <summary>
    /// Per-plane divisor tables, built once: the C's scale value for each
    /// output pixel (scale[(line &lt;&lt; yFactor) * (pitch &lt;&lt; xFactor) + (col &lt;&lt; xFactor)])
    /// and its float reciprocal.
    /// </summary>
    public static void ScaleTables(int* scale, int inPitch, int visibleLines, int visiblePitch,
        int xFactor, int yFactor, int* divisors, float* reciprocals)
    {
        int scalePitch = inPitch << xFactor;
        for (int line = 0; line < visibleLines; line++)
        {
            int* scaleRow = scale + (line << yFactor) * scalePitch;
            for (int col = 0; col < visiblePitch; col++)
            {
                int s = scaleRow[col << xFactor];
                divisors[line * visiblePitch + col] = s;
                reciprocals[line * visiblePitch + col] = 1.0f / s;
            }
        }
    }

    public static void Plane(
        byte* input, int inPitch, int visibleLines, int visiblePitch,
        byte* output, int outPitch,
        int xFactor, int yFactor,
        ushort* buffer, int* distribution, int dim,
        int* divisors, float* reciprocals)
    {
        Horizontal(input, inPitch, visibleLines, visiblePitch, xFactor, buffer, distribution, dim);
        Vertical(inPitch, visibleLines, visiblePitch, output, outPitch, yFactor,
            buffer, distribution, dim, divisors, reciprocals);
    }

    /// <summary>
    /// Merges the taps x in [lo, hi] by pixel offset x &gt;&gt; factor, then pairs
    /// offsets -o and +o that carry the same weight. Returns the weight of
    /// offset 0; pairs and singles go to the given arrays.
    /// </summary>
    internal static int MergeTaps(int* center, int lo, int hi, int factor, int* merged,
        int* pairOffsets, int* pairWeights, out int pairCount,
        int* singleOffsets, int* singleWeights, out int singleCount)
    {
        int first = lo >> factor, last = hi >> factor;
        int* m = merged - first;
        for (int o = first; o <= last; o++) m[o] = 0;
        for (int x = lo; x <= hi; x++) m[x >> factor] += center[x];

        pairCount = 0;
        singleCount = 0;
        for (int o = 1; o <= Math.Max(-first, last); o++)
        {
            bool hasNegative = -o >= first, hasPositive = o <= last;
            if (hasNegative && hasPositive && m[-o] == m[o])
            {
                pairOffsets[pairCount] = o;
                pairWeights[pairCount++] = m[o];
                continue;
            }
            if (hasNegative)
            {
                singleOffsets[singleCount] = -o;
                singleWeights[singleCount++] = m[-o];
            }
            if (hasPositive)
            {
                singleOffsets[singleCount] = o;
                singleWeights[singleCount++] = m[o];
            }
        }
        return m[0];
    }

    private static void Horizontal(
        byte* input, int inPitch, int visibleLines, int visiblePitch, int xFactor,
        ushort* buffer, int* distribution, int dim)
    {
        int* center = distribution + dim;
        int step = xFactor + 1;
        // Columns where the C bounds are exactly [-dim, dim].
        int interiorStart = Math.Min(visiblePitch, (dim + step - 1) / step);
        int interiorEnd = Math.Min(visiblePitch,
            Math.Max(interiorStart, visiblePitch - Math.Max(0, (dim - 1 + step - 1) / step) + 1));

        int taps = dim + 2;
        int* merged = stackalloc int[2 * taps];
        int* pairOffsets = stackalloc int[taps];
        int* pairWeights = stackalloc int[taps];
        int* singleOffsets = stackalloc int[2 * taps];
        int* singleWeights = stackalloc int[2 * taps];
        int w0 = MergeTaps(center, -dim, dim, xFactor, merged,
            pairOffsets, pairWeights, out int pairCount, singleOffsets, singleWeights, out int singleCount);

        Vector256<ushort> vw0 = Vector256.Create((ushort)w0);

        for (int line = 0; line < visibleLines; line++)
        {
            byte* row = input + line * inPitch;
            ushort* outRow = buffer + line * inPitch;

            for (int col = 0; col < interiorStart; col++)
                outRow[col] = (ushort)BorderPixel(row, col, visiblePitch, xFactor, center, dim);

            int c = interiorStart;
            for (; c + 16 <= interiorEnd; c += 16)
            {
                byte* p = row + c;
                Vector256<ushort> sum = Avx2.MultiplyLow(Avx2.ConvertToVector256Int16(p).AsUInt16(), vw0);
                for (int i = 0; i < pairCount; i++)
                {
                    int o = pairOffsets[i];
                    Vector256<ushort> both = Avx2.Add(
                        Avx2.ConvertToVector256Int16(p - o).AsUInt16(),
                        Avx2.ConvertToVector256Int16(p + o).AsUInt16());
                    sum = Avx2.Add(sum, Avx2.MultiplyLow(both, Vector256.Create((ushort)pairWeights[i])));
                }
                for (int i = 0; i < singleCount; i++)
                {
                    Vector256<ushort> pixels = Avx2.ConvertToVector256Int16(p + singleOffsets[i]).AsUInt16();
                    sum = Avx2.Add(sum, Avx2.MultiplyLow(pixels, Vector256.Create((ushort)singleWeights[i])));
                }
                Avx.Store(outRow + c, sum);
            }
            for (; c < interiorEnd; c++)
            {
                byte* p = row + c;
                int value = w0 * p[0];
                for (int i = 0; i < pairCount; i++)
                    value += pairWeights[i] * (p[-pairOffsets[i]] + p[pairOffsets[i]]);
                for (int i = 0; i < singleCount; i++)
                    value += singleWeights[i] * p[singleOffsets[i]];
                outRow[c] = (ushort)value;
            }

            for (int col = interiorEnd; col < visiblePitch; col++)
                outRow[col] = (ushort)BorderPixel(row, col, visiblePitch, xFactor, center, dim);
        }
    }

    // The C's exact bounds, for border columns.
    internal static int BorderPixel(byte* row, int col, int visiblePitch, int xFactor, int* center, int dim)
    {
        int lo = Math.Max(-dim, -col * (xFactor + 1));
        int hi = Math.Min(dim, (visiblePitch - col) * (xFactor + 1) + 1);
        byte* p = row + col;
        int value = 0;
        for (int x = lo; x <= hi; x++)
            value += center[x] * p[x >> xFactor];
        return value;
    }

    private static void Vertical(
        int inPitch, int visibleLines, int visiblePitch,
        byte* output, int outPitch, int yFactor,
        ushort* buffer, int* distribution, int dim,
        int* divisors, float* reciprocals)
    {
        int* center = distribution + dim;
        int step = yFactor + 1;
        int taps = dim + 2;
        int* merged = stackalloc int[2 * taps];
        int* pairOffsets = stackalloc int[taps];
        int* pairWeights = stackalloc int[taps];
        int* singleOffsets = stackalloc int[2 * taps];
        int* singleWeights = stackalloc int[2 * taps];
        // Lane order after the two packs (see Store32).
        Vector256<int> packOrder = Vector256.Create(0, 4, 1, 5, 2, 6, 3, 7);

        for (int line = 0; line < visibleLines; line++)
        {
            // The C bounds for this line (they do not depend on the column).
            int lo = Math.Max(-dim, -line * step);
            int hi = Math.Min(dim, (visibleLines - line) * step - 1);
            int w0 = MergeTaps(center, lo, hi, yFactor, merged,
                pairOffsets, pairWeights, out int pairCount, singleOffsets, singleWeights, out int singleCount);
            ushort* baseRow = buffer + line * inPitch;
            int* lineDivisors = divisors + line * visiblePitch;
            float* lineReciprocals = reciprocals + line * visiblePitch;
            byte* outRow = output + line * outPitch;
            Vector256<int> vw0 = Vector256.Create(w0);

            int col = 0;
            for (; col + 32 <= visiblePitch; col += 32)
            {
                ushort* p = baseRow + col;
                Vector256<int> a0 = Avx2.MultiplyLow(Avx2.ConvertToVector256Int32(p), vw0);
                Vector256<int> a1 = Avx2.MultiplyLow(Avx2.ConvertToVector256Int32(p + 8), vw0);
                Vector256<int> a2 = Avx2.MultiplyLow(Avx2.ConvertToVector256Int32(p + 16), vw0);
                Vector256<int> a3 = Avx2.MultiplyLow(Avx2.ConvertToVector256Int32(p + 24), vw0);
                for (int i = 0; i < pairCount; i++)
                {
                    ushort* above = p - pairOffsets[i] * inPitch;
                    ushort* below = p + pairOffsets[i] * inPitch;
                    Vector256<int> w = Vector256.Create(pairWeights[i]);
                    a0 = Avx2.Add(a0, Avx2.MultiplyLow(Avx2.Add(Avx2.ConvertToVector256Int32(above), Avx2.ConvertToVector256Int32(below)), w));
                    a1 = Avx2.Add(a1, Avx2.MultiplyLow(Avx2.Add(Avx2.ConvertToVector256Int32(above + 8), Avx2.ConvertToVector256Int32(below + 8)), w));
                    a2 = Avx2.Add(a2, Avx2.MultiplyLow(Avx2.Add(Avx2.ConvertToVector256Int32(above + 16), Avx2.ConvertToVector256Int32(below + 16)), w));
                    a3 = Avx2.Add(a3, Avx2.MultiplyLow(Avx2.Add(Avx2.ConvertToVector256Int32(above + 24), Avx2.ConvertToVector256Int32(below + 24)), w));
                }
                for (int i = 0; i < singleCount; i++)
                {
                    ushort* r = p + singleOffsets[i] * inPitch;
                    Vector256<int> w = Vector256.Create(singleWeights[i]);
                    a0 = Avx2.Add(a0, Avx2.MultiplyLow(Avx2.ConvertToVector256Int32(r), w));
                    a1 = Avx2.Add(a1, Avx2.MultiplyLow(Avx2.ConvertToVector256Int32(r + 8), w));
                    a2 = Avx2.Add(a2, Avx2.MultiplyLow(Avx2.ConvertToVector256Int32(r + 16), w));
                    a3 = Avx2.Add(a3, Avx2.MultiplyLow(Avx2.ConvertToVector256Int32(r + 24), w));
                }
                Vector256<int> q0 = Divide(a0, lineDivisors + col, lineReciprocals + col);
                Vector256<int> q1 = Divide(a1, lineDivisors + col + 8, lineReciprocals + col + 8);
                Vector256<int> q2 = Divide(a2, lineDivisors + col + 16, lineReciprocals + col + 16);
                Vector256<int> q3 = Divide(a3, lineDivisors + col + 24, lineReciprocals + col + 24);
                // (uint8_t) cast: keep the low byte, then pack 32 lanes to bytes.
                Vector256<int> low = Vector256.Create(0xFF);
                Vector256<ushort> words01 = Avx2.PackUnsignedSaturate(Avx2.And(q0, low), Avx2.And(q1, low));
                Vector256<ushort> words23 = Avx2.PackUnsignedSaturate(Avx2.And(q2, low), Avx2.And(q3, low));
                Vector256<byte> bytes = Avx2.PackUnsignedSaturate(words01.AsInt16(), words23.AsInt16());
                Avx.Store(outRow + col, Avx2.PermuteVar8x32(bytes.AsInt32(), packOrder).AsByte());
            }
            for (; col < visiblePitch; col++)
            {
                ushort* p = baseRow + col;
                int value = w0 * p[0];
                for (int i = 0; i < pairCount; i++)
                    value += pairWeights[i] * (p[-pairOffsets[i] * inPitch] + p[pairOffsets[i] * inPitch]);
                for (int i = 0; i < singleCount; i++)
                    value += singleWeights[i] * p[singleOffsets[i] * inPitch];
                outRow[col] = (byte)(value / lineDivisors[col]);
            }
        }
    }

    // Exact value / divisor for 8 lanes (value < 2^24, divisor > 0).
    internal static Vector256<int> Divide(Vector256<int> value, int* divisors, float* reciprocals)
    {
        Vector256<int> divisor = Avx.LoadVector256(divisors);
        Vector256<int> q = Avx.ConvertToVector256Int32WithTruncation(
            Avx.Multiply(Avx.ConvertToVector256Single(value), Avx.LoadVector256(reciprocals)));
        Vector256<int> remainder = Avx2.Subtract(value, Avx2.MultiplyLow(q, divisor));
        // remainder < 0: q is one too big; remainder >= divisor: one too small.
        // A true compare gives -1 in the lane.
        q = Avx2.Add(q, Avx2.CompareGreaterThan(Vector256<int>.Zero, remainder));
        q = Avx2.Subtract(q, Avx2.CompareGreaterThan(remainder, Avx2.Subtract(divisor, Vector256<int>.One)));
        return q;
    }
}
