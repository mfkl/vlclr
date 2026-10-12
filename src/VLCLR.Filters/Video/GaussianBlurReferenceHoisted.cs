// GaussianBlurReference with two changes that only affect how RyuJIT compiles
// it, not what it computes: the inner loop bounds are computed once per pixel
// instead of in the loop condition, and indices are native-sized (nint), so
// the hot loops need no sign extension. clang does both on its own for the C.
// Same statements, same order, same integer math, no SIMD.
//
// LGPL-2.1-or-later, like the VLC source it is ported from.

namespace VLCLR.Filters.Video;

internal static unsafe class GaussianBlurReferenceHoisted
{
    /// <summary>One plane: horizontal pass into <paramref name="buffer"/>, then vertical pass and normalization.</summary>
    public static void Plane(
        byte* input, int inPitch, int visibleLines, int visiblePitch,
        byte* output, int outPitch,
        int xFactor, int yFactor,
        int* buffer, int* scale, int* distribution, int dim)
    {
        for (int line = 0; line < visibleLines; line++)
        {
            for (int col = 0; col < visiblePitch; col++)
            {
                int value = 0;
                nint c = (nint)line * inPitch + col;
                nint xStart = Math.Max(-dim, -col * (xFactor + 1));
                nint xEnd = Math.Min(dim, (visiblePitch - col) * (xFactor + 1) + 1);
                for (nint x = xStart; x <= xEnd; x++)
                {
                    value += distribution[x + dim] * input[c + (x >> xFactor)];
                }
                buffer[c] = value;
            }
        }
        for (int line = 0; line < visibleLines; line++)
        {
            for (int col = 0; col < visiblePitch; col++)
            {
                int value = 0;
                nint c = (nint)line * inPitch + col;
                nint yStart = Math.Max(-dim, -line * (yFactor + 1));
                nint yEnd = Math.Min(dim, (visibleLines - line) * (yFactor + 1) - 1);
                for (nint y = yStart; y <= yEnd; y++)
                {
                    value += distribution[y + dim] * buffer[c + (y >> yFactor) * inPitch];
                }
                int s = scale[(line << yFactor) * (inPitch << xFactor) + (col << xFactor)];
                output[(nint)line * outPitch + col] = (byte)(value / s);
            }
        }
    }
}
