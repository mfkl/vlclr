using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LiveAudioTranslator.VisualTest;

internal sealed record ImageDifferenceResult(
    bool Passed,
    int Width,
    int Height,
    long SubtitleBandDifferences,
    long OutsideDifferences,
    int LargestConnectedComponent,
    double OutsideDifferenceRatio);

internal static class ImageAssertions
{
    public static ImageDifferenceResult Compare(
        string baselinePath,
        string translatedPath,
        string differencePath)
    {
        using Image<Rgba32> baseline = Image.Load<Rgba32>(baselinePath);
        using Image<Rgba32> translated = Image.Load<Rgba32>(translatedPath);
        if (baseline.Width != translated.Width || baseline.Height != translated.Height)
            throw new InvalidDataException("Baseline and translated image dimensions differ.");

        int width = baseline.Width;
        int height = baseline.Height;
        int bandTop = (int)(height * 0.55);
        var changedInBand = new bool[width * (height - bandTop)];
        using var difference = new Image<Rgba32>(width, height);
        long band = 0;
        long outside = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Rgba32 left = baseline[x, y];
                Rgba32 right = translated[x, y];
                int magnitude =
                    Math.Abs(left.R - right.R) +
                    Math.Abs(left.G - right.G) +
                    Math.Abs(left.B - right.B) +
                    Math.Abs(left.A - right.A);
                bool changed = magnitude >= 36;
                difference[x, y] = changed
                    ? new Rgba32(255, 0, 255, 255)
                    : new Rgba32(0, 0, 0, 255);
                if (!changed)
                    continue;
                if (y >= bandTop)
                {
                    band++;
                    changedInBand[(y - bandTop) * width + x] = true;
                }
                else
                {
                    outside++;
                }
            }
        }
        difference.SaveAsPng(differencePath);

        int largest = LargestComponent(changedInBand, width, height - bandTop);
        double outsideRatio = outside / (double)(width * Math.Max(1, bandTop));
        bool passed =
            band >= 100 &&
            band < (long)width * (height - bandTop) * 0.75 &&
            largest >= 25 &&
            outsideRatio <= 0.02;
        return new ImageDifferenceResult(
            passed,
            width,
            height,
            band,
            outside,
            largest,
            outsideRatio);
    }

    private static int LargestComponent(bool[] pixels, int width, int height)
    {
        var queue = new Queue<int>();
        int maximum = 0;
        for (int index = 0; index < pixels.Length; index++)
        {
            if (!pixels[index])
                continue;
            pixels[index] = false;
            queue.Enqueue(index);
            int size = 0;
            while (queue.TryDequeue(out int current))
            {
                size++;
                int x = current % width;
                int y = current / width;
                Visit(x - 1, y);
                Visit(x + 1, y);
                Visit(x, y - 1);
                Visit(x, y + 1);
            }
            maximum = Math.Max(maximum, size);
        }
        return maximum;

        void Visit(int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
                return;
            int neighbor = y * width + x;
            if (!pixels[neighbor])
                return;
            pixels[neighbor] = false;
            queue.Enqueue(neighbor);
        }
    }
}
