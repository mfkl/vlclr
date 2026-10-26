// GaussianBlurTuned without unsafe code: spans and managed arrays instead of
// pointers, every access bounds-checked or proven in range by the JIT. Same
// restructuring (interior/border split, symmetric taps, row-wise vertical
// pass, division by a multiplication in the interior), same bit-exact result.
//
// The loops are shaped so the JIT can drop the bounds checks: both passes
// work one tap at a time on whole runs of pixels, with every span sliced to
// the length of the sums. The normalization table is computed as the product
// of two 1-D sums (equal to the C's 2-D sum, without its cost).
//
// LGPL-2.1-or-later, like the VLC source it is ported from.

namespace VLCLR.Filters.Video;

internal static class GaussianBlurSafe
{
    /// <summary>
    /// The C's normalization table (GaussianBlurReference.Scale) for the Y
    /// plane geometry. Its 2-D sum of products is the product of the row and
    /// column sums of the weights in range, the same integer.
    /// </summary>
    public static void Scale(Span<int> scale, ReadOnlySpan<int> distribution, int dim, int visibleLines, int visiblePitch, int pitch)
    {
        int[] columnSums = new int[visiblePitch];
        for (int col = 0; col < visiblePitch; col++)
            columnSums[col] = Sum(distribution, dim, Math.Max(-dim, -col), Math.Min(dim, visiblePitch - col - 1));
        for (int line = 0; line < visibleLines; line++)
        {
            int lineSum = Sum(distribution, dim, Math.Max(-dim, -line), Math.Min(dim, visibleLines - line - 1));
            Span<int> scaleRow = scale.Slice(line * pitch, visiblePitch);
            for (int col = 0; col < scaleRow.Length; col++)
                scaleRow[col] = lineSum * columnSums[col];
        }
    }

    private static int Sum(ReadOnlySpan<int> distribution, int dim, int lo, int hi)
    {
        int sum = 0;
        foreach (int weight in distribution.Slice(dim + lo, hi - lo + 1))
            sum += weight;
        return sum;
    }

    /// <summary>
    /// The input and output spans start at the first visible pixel and cover
    /// at least the visible lines; <paramref name="buffer"/> and
    /// <paramref name="scale"/> have the luma layout, as in the C.
    /// </summary>
    public static void Plane(
        ReadOnlySpan<byte> input, int inPitch, int visibleLines, int visiblePitch,
        Span<byte> output, int outPitch,
        int xFactor, int yFactor,
        Span<int> buffer, ReadOnlySpan<int> scale, ReadOnlySpan<int> distribution, int dim,
        GaussianBlurTuned.Setup setup, Span<int> accumulator)
    {
        Horizontal(input, inPitch, visibleLines, visiblePitch, xFactor, buffer, distribution, dim, setup.Symmetric);
        Vertical(inPitch, visibleLines, visiblePitch, output, outPitch, xFactor, yFactor,
            buffer, scale, distribution, dim, setup, accumulator);
    }

    private static void Horizontal(
        ReadOnlySpan<byte> input, int inPitch, int visibleLines, int visiblePitch, int xFactor,
        Span<int> buffer, ReadOnlySpan<int> distribution, int dim, bool symmetric)
    {
        int step = xFactor + 1;
        // Columns where the C bounds are exactly [-dim, dim]: col * step >= dim
        // and (visiblePitch - col) * step - 1 >= dim.
        int interiorStart = Math.Min(visiblePitch, (dim + step - 1) / step);
        int interiorEnd = Math.Min(visiblePitch,
            Math.Max(interiorStart, visiblePitch - (dim + step) / step + 1));

        for (int line = 0; line < visibleLines; line++)
        {
            ReadOnlySpan<byte> row = input.Slice(line * inPitch, visiblePitch);
            Span<int> outRow = buffer.Slice(line * inPitch, visiblePitch);

            for (int col = 0; col < interiorStart; col++)
                outRow[col] = BorderPixel(row, col, xFactor, distribution, dim);
            if (interiorEnd > interiorStart)
                Interior(row, outRow.Slice(interiorStart, interiorEnd - interiorStart), interiorStart, xFactor, distribution, dim, symmetric);
            for (int col = interiorEnd; col < visiblePitch; col++)
                outRow[col] = BorderPixel(row, col, xFactor, distribution, dim);
        }
    }

    // Interior columns, one tap at a time over the whole run (as the vertical
    // pass does with rows): every tap reads a slice of the row as long as
    // sums, so the inner loops need no bounds checks. Same sums as per pixel.
    private static void Interior(ReadOnlySpan<byte> row, Span<int> sums, int start, int xFactor,
        ReadOnlySpan<int> distribution, int dim, bool symmetric)
    {
        int w0 = distribution[dim];
        ReadOnlySpan<byte> center = row.Slice(start, sums.Length);
        for (int col = 0; col < sums.Length; col++)
            sums[col] = w0 * center[col];

        if (symmetric)
        {
            for (int k = 1; k <= dim; k++)
            {
                ReadOnlySpan<byte> left = row.Slice(start + ((-k) >> xFactor), sums.Length);
                ReadOnlySpan<byte> right = row.Slice(start + (k >> xFactor), sums.Length);
                int w = distribution[dim + k];
                for (int col = 0; col < sums.Length; col++)
                    sums[col] += w * (left[col] + right[col]);
            }
        }
        else
        {
            for (int x = -dim; x <= dim; x++)
            {
                if (x == 0)
                    continue;
                ReadOnlySpan<byte> taps = row.Slice(start + (x >> xFactor), sums.Length);
                int w = distribution[dim + x];
                for (int col = 0; col < sums.Length; col++)
                    sums[col] += w * taps[col];
            }
        }
    }

    // The C's exact bounds, for border columns. row holds the visible pixels.
    private static int BorderPixel(ReadOnlySpan<byte> row, int col, int xFactor, ReadOnlySpan<int> distribution, int dim)
    {
        int lo = Math.Max(-dim, -col * (xFactor + 1));
        int hi = Math.Min(dim, (row.Length - col) * (xFactor + 1) - 1);
        int value = 0;
        for (int x = lo; x <= hi; x++)
            value += distribution[x + dim] * row[col + (x >> xFactor)];
        return value;
    }

    private static void Vertical(
        int inPitch, int visibleLines, int visiblePitch,
        Span<byte> output, int outPitch, int xFactor, int yFactor,
        ReadOnlySpan<int> buffer, ReadOnlySpan<int> scale, ReadOnlySpan<int> distribution, int dim,
        GaussianBlurTuned.Setup setup, Span<int> accumulator)
    {
        int step = yFactor + 1;
        int scalePitch = inPitch << xFactor;
        Span<int> sums = accumulator.Slice(0, visiblePitch);

        for (int line = 0; line < visibleLines; line++)
        {
            // The C bounds for this line (they do not depend on the column).
            int lo = Math.Max(-dim, -line * step);
            int hi = Math.Min(dim, (visibleLines - line) * step - 1);
            int baseRow = line * inPitch;

            // y = 0 is always inside [lo, hi].
            int w0 = distribution[dim];
            ReadOnlySpan<int> center = buffer.Slice(baseRow, sums.Length);
            for (int col = 0; col < sums.Length; col++)
                sums[col] = w0 * center[col];

            int paired = setup.Symmetric ? Math.Min(-lo, hi) : 0;
            for (int k = 1; k <= paired; k++)
            {
                ReadOnlySpan<int> above = buffer.Slice(baseRow + ((-k) >> yFactor) * inPitch, sums.Length);
                ReadOnlySpan<int> below = buffer.Slice(baseRow + (k >> yFactor) * inPitch, sums.Length);
                int w = distribution[dim + k];
                for (int col = 0; col < sums.Length; col++)
                    sums[col] += w * (above[col] + below[col]);
            }
            for (int y = lo; y < -paired; y++)
                AddRow(sums, buffer.Slice(baseRow + (y >> yFactor) * inPitch, sums.Length), distribution[dim + y]);
            for (int y = Math.Max(paired + 1, 1); y <= hi; y++)
                AddRow(sums, buffer.Slice(baseRow + (y >> yFactor) * inPitch, sums.Length), distribution[dim + y]);

            Normalize(sums, scale.Slice((line << yFactor) * scalePitch), output.Slice(line * outPitch, sums.Length),
                xFactor, setup);
        }
    }

    // In a method of its own, so the loop gets the registers for its constants.
    private static void Normalize(ReadOnlySpan<int> sums, ReadOnlySpan<int> scaleRow, Span<byte> outRow,
        int xFactor, GaussianBlurTuned.Setup setup)
    {
        int interiorScale = setup.InteriorScale;
        ulong magic = setup.Magic;
        int shift = setup.Shift;
        outRow = outRow.Slice(0, sums.Length);
        if (xFactor == 0)
        {
            // Luma and unsubsampled chroma: the scale row lines up with the sums.
            scaleRow = scaleRow.Slice(0, sums.Length);
            for (int col = 0; col < sums.Length; col++)
                outRow[col] = Divide(sums[col], scaleRow[col], interiorScale, magic, shift);
        }
        else
        {
            for (int col = 0; col < sums.Length; col++)
                outRow[col] = Divide(sums[col], scaleRow[col << xFactor], interiorScale, magic, shift);
        }
    }

    private static byte Divide(int value, int s, int interiorScale, ulong magic, int shift)
        => (byte)(s == interiorScale ? (int)(((ulong)(uint)value * magic) >> shift) : value / s);

    private static void AddRow(Span<int> sums, ReadOnlySpan<int> row, int weight)
    {
        for (int col = 0; col < sums.Length; col++)
            sums[col] += weight * row[col];
    }
}
