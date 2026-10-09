// Fastest scalar C# version: no SIMD, no intrinsics, no threads, baseline
// x86-64. Same arithmetic result as VLC's C (bit-exact), restructured:
//
// 1. Interior vs border: pixels where the C uses the full kernel take a fast
//    path; border pixels run the C's exact bounds (including its horizontal
//    read of up to two bytes past the visible width).
// 2. Symmetric kernel: distribution[-k] == distribution[k] (checked at setup,
//    with a fallback), so each pair of taps costs one multiply: w*(a+b).
// 3. Row-wise vertical pass: accumulate one output line from whole buffer
//    rows (sequential memory) instead of walking each column through the
//    buffer. Integer sums of non-negative terms do not depend on order and
//    cannot overflow here (< 2^25), so the result is unchanged.
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
        // Columns where the C bounds are exactly [-dim, dim].
        int interiorStart = Math.Min(visiblePitch, (dim + step - 1) / step);
        int interiorEnd = Math.Min(visiblePitch,
            Math.Max(interiorStart, visiblePitch - Math.Max(0, (dim - 1 + step - 1) / step) + 1));

        // Tap offsets of -k and +k for this plane's subsampling.
        int* negative = stackalloc int[dim + 1];
        int* positive = stackalloc int[dim + 1];
        for (int k = 0; k <= dim; k++)
        {
            negative[k] = (-k) >> xFactor;
            positive[k] = k >> xFactor;
        }
        int w0 = center[0];

        for (int line = 0; line < visibleLines; line++)
        {
            byte* row = input + line * inPitch;
            int* outRow = buffer + line * inPitch;

            for (int col = 0; col < interiorStart; col++)
                outRow[col] = BorderPixel(row, col, visiblePitch, xFactor, center, dim);

            if (symmetric && xFactor == 0)
            {
                for (int col = interiorStart; col < interiorEnd; col++)
                {
                    byte* p = row + col;
                    int value = w0 * p[0];
                    for (int k = 1; k <= dim; k++)
                        value += center[k] * (p[-k] + p[k]);
                    outRow[col] = value;
                }
            }
            else if (symmetric)
            {
                for (int col = interiorStart; col < interiorEnd; col++)
                {
                    byte* p = row + col;
                    int value = w0 * p[0];
                    for (int k = 1; k <= dim; k++)
                        value += center[k] * (p[negative[k]] + p[positive[k]]);
                    outRow[col] = value;
                }
            }
            else
            {
                for (int col = interiorStart; col < interiorEnd; col++)
                {
                    byte* p = row + col;
                    int value = 0;
                    for (int x = -dim; x <= dim; x++)
                        value += center[x] * p[x >> xFactor];
                    outRow[col] = value;
                }
            }

            for (int col = interiorEnd; col < visiblePitch; col++)
                outRow[col] = BorderPixel(row, col, visiblePitch, xFactor, center, dim);
        }
    }

    // The C's exact bounds, for border columns.
    private static int BorderPixel(byte* row, int col, int visiblePitch, int xFactor, int* center, int dim)
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
        byte* output, int outPitch, int xFactor, int yFactor,
        int* buffer, int* scale, int* distribution, int dim,
        Setup setup, int* accumulator)
    {
        int* center = distribution + dim;
        int step = yFactor + 1;
        int scalePitch = inPitch << xFactor;
        int interiorScale = setup.InteriorScale;
        ulong magic = setup.Magic;
        int shift = setup.Shift;

        for (int line = 0; line < visibleLines; line++)
        {
            // The C bounds for this line (they do not depend on the column).
            int lo = Math.Max(-dim, -line * step);
            int hi = Math.Min(dim, (visibleLines - line) * step - 1);
            int* baseRow = buffer + line * inPitch;

            // y = 0 is always inside [lo, hi].
            int w0 = center[0];
            for (int col = 0; col < visiblePitch; col++)
                accumulator[col] = w0 * baseRow[col];

            int paired = setup.Symmetric ? Math.Min(-lo, hi) : 0;
            for (int k = 1; k <= paired; k++)
            {
                int* above = baseRow + ((-k) >> yFactor) * inPitch;
                int* below = baseRow + (k >> yFactor) * inPitch;
                int w = center[k];
                for (int col = 0; col < visiblePitch; col++)
                    accumulator[col] += w * (above[col] + below[col]);
            }
            for (int y = lo; y < -paired; y++)
                AddRow(accumulator, baseRow + (y >> yFactor) * inPitch, center[y], visiblePitch);
            for (int y = Math.Max(paired + 1, 1); y <= hi; y++)
                AddRow(accumulator, baseRow + (y >> yFactor) * inPitch, center[y], visiblePitch);

            int* scaleRow = scale + (line << yFactor) * scalePitch;
            byte* outRow = output + line * outPitch;
            for (int col = 0; col < visiblePitch; col++)
            {
                int value = accumulator[col];
                int s = scaleRow[col << xFactor];
                int quotient = s == interiorScale
                    ? (int)(((ulong)(uint)value * magic) >> shift)
                    : value / s;
                outRow[col] = (byte)quotient;
            }
        }
    }

    private static void AddRow(int* accumulator, int* row, int weight, int count)
    {
        for (int col = 0; col < count; col++)
            accumulator[col] += weight * row[col];
    }
}
