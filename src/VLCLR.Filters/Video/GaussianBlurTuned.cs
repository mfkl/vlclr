// Fastest scalar C# version: no SIMD, no intrinsics, no threads, baseline
// x86-64. Same arithmetic result as VLC's C (bit-exact), restructured:
//
// 1. Interior vs border: pixels where the C uses the full kernel take a fast
//    path; border pixels run the C's exact bounds.
// 2. Symmetric kernel: distribution[-k] == distribution[k] (checked at setup,
//    with a fallback), so each pair of taps costs one multiply: w*(a+b).
// 3. Row-wise passes: accumulate one output line from whole buffer rows
//    (sequential memory) instead of walking each column through the
//    buffer, and the horizontal interior one tap at a time over the whole
//    run instead of a short tap loop per pixel. Integer sums of
//    non-negative terms do not depend on order and cannot overflow here
//    (< 2^25), so the result is unchanged.
// 4. Division by an invariant: in the interior the normalization divisor is
//    the constant S = (sum of weights)^2, so value / S is computed as
//    (value * M) >> K with M = ceil(2^K / S), K = N + L, where value < 2^N and
//    S <= 2^L. Exact: value*M/2^K = value/S + value*e/(S*2^K) with e < S, and
//    value*e < 2^(N+L) = 2^K keeps the error below 1/S, too small to reach
//    the next integer. Other divisors (borders) use a real division.
//
// LGPL-2.1-or-later, like the VLC source it is ported from.

namespace VLCLR.Filters.Video;

internal static unsafe class GaussianBlurTuned
{
    /// <summary>Per-sigma constants, computed once at open.</summary>
    public sealed class Setup
    {
        public required bool Symmetric { get; init; }
        public required int InteriorScale { get; init; }
        public required ulong Magic { get; init; }
        public required int Shift { get; init; }
    }

    public static Setup Prepare(int[] distribution, int dim)
    {
        bool symmetric = true;
        long sum = 0;
        for (int k = 0; k < distribution.Length; k++)
        {
            sum += distribution[k];
            if (distribution[k] != distribution[distribution.Length - 1 - k])
                symmetric = false;
        }
        // Interior pixels: both kernel ranges are full, scale = sum * sum.
        long scale = sum * sum;
        int l = 0;
        while ((1L << l) < scale) l++;          // scale <= 2^l
        int n = 8 + l;                          // value <= 255 * scale < 2^(8+l)
        int shift = n + l;
        ulong magic = (ulong)((((Int128)1 << shift) + scale - 1) / scale);
        return new Setup
        {
            Symmetric = symmetric,
            InteriorScale = (int)Math.Min(scale, int.MaxValue),
            Magic = magic,
            Shift = shift,
        };
    }

    public static void Plane(
        byte* input, int inPitch, int visibleLines, int visiblePitch,
        byte* output, int outPitch,
        int xFactor, int yFactor,
        int* buffer, int* scale, int* distribution, int dim,
        Setup setup, int* accumulator)
    {
        Horizontal(input, inPitch, visibleLines, visiblePitch, xFactor, buffer, distribution, dim, setup.Symmetric);
        Vertical(inPitch, visibleLines, visiblePitch, output, outPitch, xFactor, yFactor,
            buffer, scale, distribution, dim, setup, accumulator);
    }

    private static void Horizontal(
        byte* input, int inPitch, int visibleLines, int visiblePitch, int xFactor,
        int* buffer, int* distribution, int dim, bool symmetric)
    {
        int* center = distribution + dim;
        int step = xFactor + 1;
        // Columns where the C bounds are exactly [-dim, dim]: col * step >= dim
        // and (visiblePitch - col) * step - 1 >= dim.
        int interiorStart = Math.Min(visiblePitch, (dim + step - 1) / step);
        int interiorEnd = Math.Min(visiblePitch,
            Math.Max(interiorStart, visiblePitch - (dim + step) / step + 1));

        for (int line = 0; line < visibleLines; line++)
        {
            byte* row = input + line * inPitch;
            int* outRow = buffer + line * inPitch;

            for (int col = 0; col < interiorStart; col++)
                outRow[col] = BorderPixel(row, col, visiblePitch, xFactor, center, dim);
            Interior(row + interiorStart, outRow + interiorStart, interiorEnd - interiorStart,
                xFactor, center, dim, symmetric);
            for (int col = interiorEnd; col < visiblePitch; col++)
                outRow[col] = BorderPixel(row, col, visiblePitch, xFactor, center, dim);
        }
    }

    // Interior columns, one tap at a time over the whole run, as the vertical
    // pass does with rows: long loops of independent pixels instead of a
    // short tap loop per pixel. Integer sums, so the order does not matter.
    // nint indices: no sign extension in the loops.
    private static void Interior(byte* row, int* sums, nint count, int xFactor, int* center, int dim, bool symmetric)
    {
        int w0 = center[0];
        for (nint col = 0; col < count; col++)
            sums[col] = w0 * row[col];

        if (symmetric)
        {
            for (int k = 1; k <= dim; k++)
            {
                byte* left = row + ((-k) >> xFactor);
                byte* right = row + (k >> xFactor);
                int w = center[k];
                for (nint col = 0; col < count; col++)
                    sums[col] += w * (left[col] + right[col]);
            }
        }
        else
        {
            for (int x = -dim; x <= dim; x++)
            {
                if (x == 0)
                    continue;
                byte* taps = row + (x >> xFactor);
                int w = center[x];
                for (nint col = 0; col < count; col++)
                    sums[col] += w * taps[col];
            }
        }
    }

    // The C's exact bounds, for border columns.
    private static int BorderPixel(byte* row, int col, int visiblePitch, int xFactor, int* center, int dim)
    {
        int lo = Math.Max(-dim, -col * (xFactor + 1));
        int hi = Math.Min(dim, (visiblePitch - col) * (xFactor + 1) - 1);
        byte* p = row + col;
        int value = 0;
        for (int x = lo; x <= hi; x++)
            value += center[x] * p[x >> xFactor];
        return value;
    }

    private static void Vertical(
        int inPitch, int visibleLines, int visiblePitch,
        byte* output, int outPitch, int xFactor, int yFactor,
        int* buffer, int* scale, int* distribution, int dim,
        Setup setup, int* accumulator)
    {
        int* center = distribution + dim;
        int step = yFactor + 1;
        int scalePitch = inPitch << xFactor;
        nint count = visiblePitch;

        for (int line = 0; line < visibleLines; line++)
        {
            // The C bounds for this line (they do not depend on the column).
            int lo = Math.Max(-dim, -line * step);
            int hi = Math.Min(dim, (visibleLines - line) * step - 1);
            int* baseRow = buffer + line * inPitch;

            // y = 0 is always inside [lo, hi].
            int w0 = center[0];
            for (nint col = 0; col < count; col++)
                accumulator[col] = w0 * baseRow[col];

            int paired = setup.Symmetric ? Math.Min(-lo, hi) : 0;
            for (int k = 1; k <= paired; k++)
            {
                int* above = baseRow + ((-k) >> yFactor) * inPitch;
                int* below = baseRow + (k >> yFactor) * inPitch;
                int w = center[k];
                for (nint col = 0; col < count; col++)
                    accumulator[col] += w * (above[col] + below[col]);
            }
            for (int y = lo; y < -paired; y++)
                AddRow(accumulator, baseRow + (y >> yFactor) * inPitch, center[y], count);
            for (int y = Math.Max(paired + 1, 1); y <= hi; y++)
                AddRow(accumulator, baseRow + (y >> yFactor) * inPitch, center[y], count);

            Normalize(accumulator, scale + (line << yFactor) * scalePitch, output + line * outPitch, count, xFactor, setup);
        }
    }

    // In a method of its own, so the loop gets the registers for its constants.
    private static void Normalize(int* sums, int* scaleRow, byte* outRow, nint count, int xFactor, Setup setup)
    {
        int interiorScale = setup.InteriorScale;
        ulong magic = setup.Magic;
        int shift = setup.Shift;
        for (nint col = 0; col < count; col++)
        {
            int value = sums[col];
            int s = scaleRow[col << xFactor];
            int quotient = s == interiorScale
                ? (int)(((ulong)(uint)value * magic) >> shift)
                : value / s;
            outRow[col] = (byte)quotient;
        }
    }

    private static void AddRow(int* accumulator, int* row, int weight, nint count)
    {
        for (nint col = 0; col < count; col++)
            accumulator[col] += weight * row[col];
    }
}
