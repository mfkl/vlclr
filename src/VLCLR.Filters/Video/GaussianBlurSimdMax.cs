// GaussianBlurSimd, restructured around what .NET's code generator does
// poorly in the hot loops (seen in the disassembly): per-tap bookkeeping
// such as offset multiplies, invariant values reloaded from the stack, and
// no loop unrolling. Same arithmetic and bit-exact output; only the loop
// shapes differ:
//
// - Horizontal pass: 32 pixels per iteration (two 16-lane sums), so each
//   tap's offset and weight are loaded once for twice the pixels.
// - Vertical pass: 64 columns per iteration (eight 8-lane sums), and the row
//   offsets of the taps are multiplied out once per line, not once per tap.
//   It uses vpmaddwd instead of a widen + vpmulld per tap: vpmaddwd
//   multiplies two rows by two weights and adds them in one instruction
//   (see Bias).
//
// LGPL-2.1-or-later, like the VLC source it is ported from.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace VLCLR.Filters.Video;

internal static unsafe class GaussianBlurSimdMax
{
    // The horizontal pass stores v ^ 0x8000, which read as a signed 16-bit
    // value is v - 32768: what vpmaddwd (signed) needs. VerticalMadd adds
    // 32768 * (sum of weights) back.
    private const ushort Bias = 0x8000;

    public static void Plane(
        byte* input, int inPitch, int visibleLines, int visiblePitch,
        byte* output, int outPitch,
        int xFactor, int yFactor,
        ushort* buffer, int* distribution, int dim,
        int* divisors, float* reciprocals)
    {
        Horizontal(input, inPitch, visibleLines, visiblePitch, xFactor, buffer, distribution, dim, Bias);
        VerticalMadd(inPitch, visibleLines, visiblePitch, output, outPitch, yFactor,
            buffer, distribution, dim, divisors, reciprocals);
    }

    private static void Horizontal(
        byte* input, int inPitch, int visibleLines, int visiblePitch, int xFactor,
        ushort* buffer, int* distribution, int dim, ushort bias)
    {
        int* center = distribution + dim;
        int step = xFactor + 1;
        Vector256<ushort> vbias = Vector256.Create(bias);
        int interiorStart = Math.Min(visiblePitch, (dim + step - 1) / step);
        int interiorEnd = Math.Min(visiblePitch,
            Math.Max(interiorStart, visiblePitch - Math.Max(0, (dim - 1 + step - 1) / step) + 1));

        int taps = dim + 2;
        int* merged = stackalloc int[2 * taps];
        int* pairOffsets = stackalloc int[taps];
        int* pairWeights = stackalloc int[taps];
        int* singleOffsets = stackalloc int[2 * taps];
        int* singleWeights = stackalloc int[2 * taps];
        int w0 = GaussianBlurSimd.MergeTaps(center, -dim, dim, xFactor, merged,
            pairOffsets, pairWeights, out int pairCount, singleOffsets, singleWeights, out int singleCount);
        Vector256<ushort> vw0 = Vector256.Create((ushort)w0);
        Tap* pairs = stackalloc Tap[Math.Max(1, pairCount)];
        Tap* singles = stackalloc Tap[Math.Max(1, singleCount)];
        for (int i = 0; i < pairCount; i++) pairs[i] = new Tap(pairOffsets[i], pairWeights[i]);
        for (int i = 0; i < singleCount; i++) singles[i] = new Tap(singleOffsets[i], singleWeights[i]);

        // Border columns: VLC's clipped window does not depend on the line,
        // so each column gets its weights over a contiguous run of pixels once
        // (taps that read the same pixel, as in chroma, merged up front).
        int rightCount = visiblePitch - interiorEnd;
        int borderCount = interiorStart + rightCount;
        int span = (2 * dim + 4 + 7) & ~7;
        int* borderWeights = (int*)NativeMemory.AllocZeroed((nuint)Math.Max(1, borderCount * span), sizeof(int));
        int* borderFirst = (int*)NativeMemory.Alloc((nuint)Math.Max(1, borderCount), sizeof(int));
        int* borderLength = (int*)NativeMemory.Alloc((nuint)Math.Max(1, borderCount), sizeof(int));
        for (int j = 0; j < borderCount; j++)
        {
            int col = j < interiorStart ? j : interiorEnd + (j - interiorStart);
            int lo = Math.Max(-dim, -col * (xFactor + 1));
            int hi = Math.Min(dim, (visiblePitch - col) * (xFactor + 1) + 1);
            int first = lo >> xFactor;
            borderFirst[j] = first;
            borderLength[j] = (hi >> xFactor) - first + 1;
            for (int x = lo; x <= hi; x++)
                borderWeights[j * span + (x >> xFactor) - first] += center[x];
        }

        for (int line = 0; line < visibleLines; line++)
        {
            byte* row = input + line * inPitch;
            ushort* outRow = buffer + line * inPitch;

            for (int col = 0; col < interiorStart; col++)
                outRow[col] = (ushort)(Dot(row + col + borderFirst[col], borderWeights + col * span, borderLength[col]) ^ bias);

            int c = Interior32(row, outRow, interiorStart, interiorEnd, pairs, pairCount, singles, singleCount, vw0, vbias);
            for (; c + 16 <= interiorEnd; c += 16)
            {
                byte* p = row + c;
                Vector256<ushort> sum = Avx2.MultiplyLow(Avx2.ConvertToVector256Int16(p).AsUInt16(), vw0);
                for (int i = 0; i < pairCount; i++)
                {
                    int o = pairOffsets[i];
                    sum = Avx2.Add(sum, Avx2.MultiplyLow(Avx2.Add(
                        Avx2.ConvertToVector256Int16(p - o).AsUInt16(), Avx2.ConvertToVector256Int16(p + o).AsUInt16()),
                        Vector256.Create((ushort)pairWeights[i])));
                }
                for (int i = 0; i < singleCount; i++)
                    sum = Avx2.Add(sum, Avx2.MultiplyLow(Avx2.ConvertToVector256Int16(p + singleOffsets[i]).AsUInt16(),
                        Vector256.Create((ushort)singleWeights[i])));
                Avx.Store(outRow + c, Avx2.Xor(sum, vbias));
            }
            for (; c < interiorEnd; c++)
            {
                byte* p = row + c;
                int value = w0 * p[0];
                for (int i = 0; i < pairCount; i++)
                    value += pairWeights[i] * (p[-pairOffsets[i]] + p[pairOffsets[i]]);
                for (int i = 0; i < singleCount; i++)
                    value += singleWeights[i] * p[singleOffsets[i]];
                outRow[c] = (ushort)(value ^ bias);
            }

            for (int col = interiorEnd; col < visiblePitch; col++)
            {
                int j = interiorStart + col - interiorEnd;
                outRow[col] = (ushort)(Dot(row + col + borderFirst[j], borderWeights + j * span, borderLength[j]) ^ bias);
            }
        }
        NativeMemory.Free(borderWeights);
        NativeMemory.Free(borderFirst);
        NativeMemory.Free(borderLength);
    }

    // Sum of weights[k] * pixels[k] for k < count, 8 at a time.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Dot(byte* pixels, int* weights, int count)
    {
        Vector256<int> sum = Vector256<int>.Zero;
        int k = 0;
        for (; k + 8 <= count; k += 8)
            sum = Avx2.Add(sum, Avx2.MultiplyLow(Avx2.ConvertToVector256Int32(pixels + k), Avx.LoadVector256(weights + k)));
        int value = Vector256.Sum(sum);
        for (; k < count; k++)
            value += weights[k] * pixels[k];
        return value;
    }

    // Weight is read through its address so the broadcast loads it straight
    // from memory (vpbroadcastw ymm, m16, like clang): broadcasting a value
    // already in a general register costs a second shuffle (vmovd), and the
    // horizontal pass is limited by the shuffle port.
    private struct Tap(nint offset, int weight)
    {
        public nint Offset = offset;
        public int Weight = weight;
    }

    // The 32-pixel interior loop of one line, in its own method so the code
    // generator allocates registers for this loop alone (inlined into
    // Horizontal, its pointers and bounds were reloaded from the stack on
    // every tap). Taps are unrolled by two. When the interior is at least 32
    // pixels wide, the last block ends exactly at `end`, overlapping the
    // previous one (those pixels get the same values again), so no tail is
    // left. Returns the first column left.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Interior32(byte* row, ushort* outRow, int start, int end,
        Tap* pairs, int pairCount, Tap* singles, int singleCount, Vector256<ushort> vw0, Vector256<ushort> vbias)
    {
        if (end - start < 32)
            return start;
        for (int c = start; ; c += 32)
        {
            if (c + 32 > end)
                c = end - 32;
            byte* p = row + c;
            Vector256<ushort> sum0 = Avx2.MultiplyLow(Avx2.ConvertToVector256Int16(p).AsUInt16(), vw0);
            Vector256<ushort> sum1 = Avx2.MultiplyLow(Avx2.ConvertToVector256Int16(p + 16).AsUInt16(), vw0);
            Tap* t0 = pairs;
            Tap* pairsEnd = pairs + pairCount;
            for (; t0 + 2 <= pairsEnd; t0 += 2)
            {
                Tap* t1 = t0 + 1;
                byte* lo0 = p - t0->Offset, hi0 = p + t0->Offset;
                byte* lo1 = p - t1->Offset, hi1 = p + t1->Offset;
                Vector256<ushort> w0 = Avx2.BroadcastScalarToVector256((ushort*)&t0->Weight);
                Vector256<ushort> w1 = Avx2.BroadcastScalarToVector256((ushort*)&t1->Weight);
                sum0 = Avx2.Add(sum0, Avx2.MultiplyLow(Avx2.Add(
                    Avx2.ConvertToVector256Int16(lo0).AsUInt16(), Avx2.ConvertToVector256Int16(hi0).AsUInt16()), w0));
                sum1 = Avx2.Add(sum1, Avx2.MultiplyLow(Avx2.Add(
                    Avx2.ConvertToVector256Int16(lo0 + 16).AsUInt16(), Avx2.ConvertToVector256Int16(hi0 + 16).AsUInt16()), w0));
                sum0 = Avx2.Add(sum0, Avx2.MultiplyLow(Avx2.Add(
                    Avx2.ConvertToVector256Int16(lo1).AsUInt16(), Avx2.ConvertToVector256Int16(hi1).AsUInt16()), w1));
                sum1 = Avx2.Add(sum1, Avx2.MultiplyLow(Avx2.Add(
                    Avx2.ConvertToVector256Int16(lo1 + 16).AsUInt16(), Avx2.ConvertToVector256Int16(hi1 + 16).AsUInt16()), w1));
            }
            for (Tap* t = t0; t < pairsEnd; t++)
            {
                byte* lo = p - t->Offset, hi = p + t->Offset;
                Vector256<ushort> w = Avx2.BroadcastScalarToVector256((ushort*)&t->Weight);
                sum0 = Avx2.Add(sum0, Avx2.MultiplyLow(Avx2.Add(
                    Avx2.ConvertToVector256Int16(lo).AsUInt16(), Avx2.ConvertToVector256Int16(hi).AsUInt16()), w));
                sum1 = Avx2.Add(sum1, Avx2.MultiplyLow(Avx2.Add(
                    Avx2.ConvertToVector256Int16(lo + 16).AsUInt16(), Avx2.ConvertToVector256Int16(hi + 16).AsUInt16()), w));
            }
            for (Tap* t = singles, singlesEnd = singles + singleCount; t < singlesEnd; t++)
            {
                byte* s = p + t->Offset;
                Vector256<ushort> w = Avx2.BroadcastScalarToVector256((ushort*)&t->Weight);
                sum0 = Avx2.Add(sum0, Avx2.MultiplyLow(Avx2.ConvertToVector256Int16(s).AsUInt16(), w));
                sum1 = Avx2.Add(sum1, Avx2.MultiplyLow(Avx2.ConvertToVector256Int16(s + 16).AsUInt16(), w));
            }
            Avx.Store(outRow + c, Avx2.Xor(sum0, vbias));
            Avx.Store(outRow + c + 16, Avx2.Xor(sum1, vbias));
            if (c + 32 == end)
                return end;
        }
    }

    // Vertical pass with vpmaddwd: two taps per multiply. Taps (center,
    // pairs, singles) become a flat list of (row stride, weight) entries,
    // taken two at a time: the two rows are interleaved with unpack and
    // multiplied by (weightA, weightB) pairs, giving weightA*a + weightB*b
    // per 32-bit lane. Unpack works within 128-bit halves, so one 16-column
    // group comes out as columns [0-3, 8-11] and [4-7, 12-15]; the divisors
    // are permuted the same way, and the first pack restores the order.
    private static void VerticalMadd(
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
        int maxEntries = 4 * taps + 2;
        nint* entryStrides = stackalloc nint[maxEntries];
        int* entryWeights = stackalloc int[maxEntries];
        nint* strideA = stackalloc nint[maxEntries / 2];
        nint* strideB = stackalloc nint[maxEntries / 2];
        int* weightAB = stackalloc int[maxEntries / 2];

        for (int line = 0; line < visibleLines; line++)
        {
            int lo = Math.Max(-dim, -line * step);
            int hi = Math.Min(dim, (visibleLines - line) * step - 1);
            int w0 = GaussianBlurSimd.MergeTaps(center, lo, hi, yFactor, merged,
                pairOffsets, pairWeights, out int pairCount, singleOffsets, singleWeights, out int singleCount);

            int n = 0, sumWeights = w0;
            entryStrides[n] = 0; entryWeights[n++] = w0;
            for (int i = 0; i < pairCount; i++)
            {
                nint stride = (nint)pairOffsets[i] * inPitch;
                entryStrides[n] = -stride; entryWeights[n++] = pairWeights[i];
                entryStrides[n] = stride; entryWeights[n++] = pairWeights[i];
                sumWeights += 2 * pairWeights[i];
            }
            for (int i = 0; i < singleCount; i++)
            {
                entryStrides[n] = (nint)singleOffsets[i] * inPitch; entryWeights[n++] = singleWeights[i];
                sumWeights += singleWeights[i];
            }
            if ((n & 1) != 0)
            {
                entryStrides[n] = 0; entryWeights[n++] = 0;
            }
            int tapPairs = n / 2;
            for (int k = 0; k < tapPairs; k++)
            {
                strideA[k] = entryStrides[2 * k];
                strideB[k] = entryStrides[2 * k + 1];
                weightAB[k] = (entryWeights[2 * k] & 0xFFFF) | (entryWeights[2 * k + 1] << 16);
            }
            Vector256<int> bias = Vector256.Create(32768 * sumWeights);

            short* baseRow = (short*)(buffer + line * inPitch);
            int* lineDivisors = divisors + line * visiblePitch;
            float* lineReciprocals = reciprocals + line * visiblePitch;
            byte* outRow = output + line * outPitch;

            int col = 0;
            for (; col + 64 <= visiblePitch; col += 64)
            {
                short* p = baseRow + col;
                Vector256<int> a0 = bias, a1 = bias, a2 = bias, a3 = bias, a4 = bias, a5 = bias, a6 = bias, a7 = bias;
                for (int k = 0; k < tapPairs; k++)
                {
                    short* ra = p + strideA[k];
                    short* rb = p + strideB[k];
                    Vector256<short> w = Vector256.Create(weightAB[k]).AsInt16();
                    Vector256<short> x = Avx.LoadVector256(ra), y = Avx.LoadVector256(rb);
                    a0 = Avx2.Add(a0, Avx2.MultiplyAddAdjacent(Avx2.UnpackLow(x, y), w));
                    a1 = Avx2.Add(a1, Avx2.MultiplyAddAdjacent(Avx2.UnpackHigh(x, y), w));
                    x = Avx.LoadVector256(ra + 16); y = Avx.LoadVector256(rb + 16);
                    a2 = Avx2.Add(a2, Avx2.MultiplyAddAdjacent(Avx2.UnpackLow(x, y), w));
                    a3 = Avx2.Add(a3, Avx2.MultiplyAddAdjacent(Avx2.UnpackHigh(x, y), w));
                    x = Avx.LoadVector256(ra + 32); y = Avx.LoadVector256(rb + 32);
                    a4 = Avx2.Add(a4, Avx2.MultiplyAddAdjacent(Avx2.UnpackLow(x, y), w));
                    a5 = Avx2.Add(a5, Avx2.MultiplyAddAdjacent(Avx2.UnpackHigh(x, y), w));
                    x = Avx.LoadVector256(ra + 48); y = Avx.LoadVector256(rb + 48);
                    a6 = Avx2.Add(a6, Avx2.MultiplyAddAdjacent(Avx2.UnpackLow(x, y), w));
                    a7 = Avx2.Add(a7, Avx2.MultiplyAddAdjacent(Avx2.UnpackHigh(x, y), w));
                }
                StoreMadd32(outRow + col, a0, a1, a2, a3, lineDivisors + col, lineReciprocals + col);
                StoreMadd32(outRow + col + 32, a4, a5, a6, a7, lineDivisors + col + 32, lineReciprocals + col + 32);
            }
            for (; col + 32 <= visiblePitch; col += 32)
            {
                short* p = baseRow + col;
                Vector256<int> a0 = bias, a1 = bias, a2 = bias, a3 = bias;
                for (int k = 0; k < tapPairs; k++)
                {
                    short* ra = p + strideA[k];
                    short* rb = p + strideB[k];
                    Vector256<short> w = Vector256.Create(weightAB[k]).AsInt16();
                    Vector256<short> x = Avx.LoadVector256(ra), y = Avx.LoadVector256(rb);
                    a0 = Avx2.Add(a0, Avx2.MultiplyAddAdjacent(Avx2.UnpackLow(x, y), w));
                    a1 = Avx2.Add(a1, Avx2.MultiplyAddAdjacent(Avx2.UnpackHigh(x, y), w));
                    x = Avx.LoadVector256(ra + 16); y = Avx.LoadVector256(rb + 16);
                    a2 = Avx2.Add(a2, Avx2.MultiplyAddAdjacent(Avx2.UnpackLow(x, y), w));
                    a3 = Avx2.Add(a3, Avx2.MultiplyAddAdjacent(Avx2.UnpackHigh(x, y), w));
                }
                StoreMadd32(outRow + col, a0, a1, a2, a3, lineDivisors + col, lineReciprocals + col);
            }
            for (; col < visiblePitch; col++)
            {
                ushort* q = buffer + line * inPitch + col;
                int value = 0;
                for (int e = 0; e < n; e++)
                    value += entryWeights[e] * (q[entryStrides[e]] ^ Bias);
                outRow[col] = (byte)(value / lineDivisors[col]);
            }
        }
    }

    // a0/a1: columns [0-3, 8-11] and [4-7, 12-15]; a2/a3: the same for 16-31.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreMadd32(byte* destination, Vector256<int> a0, Vector256<int> a1, Vector256<int> a2, Vector256<int> a3,
        int* divisors, float* reciprocals)
    {
        Vector256<int> low = Vector256.Create(0xFF);
        Vector256<int> q0 = DivideMadd(a0, divisors, reciprocals, 0x20);
        Vector256<int> q1 = DivideMadd(a1, divisors, reciprocals, 0x31);
        Vector256<int> q2 = DivideMadd(a2, divisors + 16, reciprocals + 16, 0x20);
        Vector256<int> q3 = DivideMadd(a3, divisors + 16, reciprocals + 16, 0x31);
        Vector256<ushort> words0 = Avx2.PackUnsignedSaturate(Avx2.And(q0, low), Avx2.And(q1, low));
        Vector256<ushort> words1 = Avx2.PackUnsignedSaturate(Avx2.And(q2, low), Avx2.And(q3, low));
        Vector256<byte> bytes = Avx2.PackUnsignedSaturate(words0.AsInt16(), words1.AsInt16());
        Avx.Store(destination, Avx2.Permute4x64(bytes.AsInt64(), 0b11_01_10_00).AsByte());
    }

    // Exact value / divisor for the columns selected by `halves` (0x20: 0-3
    // and 8-11, 0x31: 4-7 and 12-15) of a 16-column group.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<int> DivideMadd(Vector256<int> value, int* divisors, float* reciprocals, [ConstantExpected] byte halves)
    {
        Vector256<int> divisor = Avx2.Permute2x128(Avx.LoadVector256(divisors), Avx.LoadVector256(divisors + 8), halves);
        Vector256<float> reciprocal = Avx.Permute2x128(Avx.LoadVector256(reciprocals), Avx.LoadVector256(reciprocals + 8), halves);
        Vector256<int> q = Avx.ConvertToVector256Int32WithTruncation(Avx.Multiply(Avx.ConvertToVector256Single(value), reciprocal));
        Vector256<int> remainder = Avx2.Subtract(value, Avx2.MultiplyLow(q, divisor));
        q = Avx2.Add(q, Avx2.CompareGreaterThan(Vector256<int>.Zero, remainder));
        q = Avx2.Subtract(q, Avx2.CompareGreaterThan(remainder, Avx2.Subtract(divisor, Vector256<int>.One)));
        return q;
    }
}
