// Straight C# port of the pixel code in VLC's modules/video_filter/gaussianblur.c
// (integer build, DONT_USE_FLOATS), kept line-for-line comparable with the C:
// same loop bounds (including the "+ 1" in the scale table and the
// horizontal pass reading up to two bytes past the visible width), same
// integer math, truncating division and byte cast. Raw pointers mirror the
// C memory accesses exactly.
//
// LGPL-2.1-or-later, like the VLC source it is ported from.

namespace VLCLR.Filters.Video;

internal static unsafe class GaussianBlurReference
{
    /// <summary>
    /// gaussianblur_InitDistribution: same float/double steps as the C, then
    /// truncated to 8.8 fixed point.
    /// </summary>
    public static int[] Distribution(double sigma, out int dim)
    {
        dim = (int)(3.0 * sigma);
        var distribution = new int[2 * dim + 1];
        for (int x = -dim; x <= dim; x++)
        {
            float f = (float)Math.Sqrt(Math.Exp(-(x * x) / (sigma * sigma)) / (2.0 * Math.PI * sigma * sigma));
            const float factor = 1 << 8;
            distribution[dim + x] = (int)(f * factor);
        }
        return distribution;
    }

    /// <summary>The normalization table, computed once from the Y plane geometry.</summary>
    public static void Scale(int* scale, int* distribution, int dim, int visibleLines, int visiblePitch, int pitch)
    {
        for (int line = 0; line < visibleLines; line++)
        {
            for (int col = 0; col < visiblePitch; col++)
            {
                int value = 0;
                for (int y = Math.Max(-dim, -line); y <= Math.Min(dim, visibleLines - line - 1); y++)
                {
                    for (int x = Math.Max(-dim, -col); x <= Math.Min(dim, visiblePitch - col + 1); x++)
                    {
                        value += distribution[y + dim] * distribution[x + dim];
                    }
                }
                scale[line * pitch + col] = value;
            }
        }
    }

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
                int c = line * inPitch + col;
                for (int x = Math.Max(-dim, -col * (xFactor + 1));
                     x <= Math.Min(dim, (visiblePitch - col) * (xFactor + 1) + 1);
                     x++)
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
                int c = line * inPitch + col;
                for (int y = Math.Max(-dim, -line * (yFactor + 1));
                     y <= Math.Min(dim, (visibleLines - line) * (yFactor + 1) - 1);
                     y++)
                {
                    value += distribution[y + dim] * buffer[c + (y >> yFactor) * inPitch];
                }
                int s = scale[(line << yFactor) * (inPitch << xFactor) + (col << xFactor)];
                output[line * outPitch + col] = (byte)(value / s);
            }
        }
    }
}
