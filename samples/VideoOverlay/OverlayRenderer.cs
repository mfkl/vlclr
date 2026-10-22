using System.Reflection;
using System.Runtime.InteropServices;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace VideoOverlay;

/// <summary>
/// Renders the debug overlay without allocating per frame.
/// </summary>
/// <remarks>
/// ImageSharp draws everything up front: the background, the border and the
/// line that never changes into <see cref="_background"/>, and each printable
/// ASCII character of the (monospaced) font into a coverage mask. A frame
/// copies the background and blends the masks of its two changing lines, whose
/// text is formatted on the stack. Drawing the text with ImageSharp on each
/// frame allocated about 1.7 MB per frame.
/// </remarks>
public class OverlayRenderer : IDisposable
{
    private const float TextX = 10;
    private const float TextY = 10;
    private const float LineHeight = 22;
    private const char FirstGlyph = ' ';
    private const char LastGlyph = '~';

    // Glyph cells are wider than the advance so that antialiased edges fit;
    // a glyph is drawn GlyphPad pixels into its cell.
    private const int GlyphPad = 2;

    private readonly byte[] _background;
    private readonly byte[] _pixelBuffer;
    private readonly byte[][] _glyphs;
    private readonly float _advance;
    private readonly int _cellWidth;
    private readonly int _cellHeight;

    /// <summary>
    /// Width of the overlay in pixels.
    /// </summary>
    public int OverlayWidth { get; }

    /// <summary>
    /// Height of the overlay in pixels.
    /// </summary>
    public int OverlayHeight { get; }

    public OverlayRenderer()
    {
        Font font = LoadEmbeddedFont();

        OverlayWidth = 300;
        OverlayHeight = 90;

        _background = DrawBackground(font, OverlayWidth, OverlayHeight);
        _pixelBuffer = new byte[_background.Length];

        _advance = TextMeasurer.MeasureAdvance("0", new TextOptions(font)).Width;
        _cellWidth = (int)MathF.Ceiling(_advance) + 2 * GlyphPad;
        _cellHeight = (int)LineHeight;
        _glyphs = DrawGlyphs(font, _cellWidth, _cellHeight);

        Console.Error.WriteLine($"[VideoOverlay] OverlayRenderer created: {OverlayWidth}x{OverlayHeight}, font loaded");
    }

    private Font LoadEmbeddedFont()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "VideoOverlay.Resources.JetBrainsMono-Regular.ttf";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            // List available resources for debugging
            var resources = assembly.GetManifestResourceNames();
            Console.Error.WriteLine($"[VideoOverlay] Available resources: {string.Join(", ", resources)}");
            throw new InvalidOperationException($"Font resource not found: {resourceName}");
        }

        var collection = new FontCollection();
        var family = collection.Add(stream);
        return family.CreateFont(14, FontStyle.Regular);
    }

    /// <summary>
    /// The parts that never change: background, border and the runtime line, as RGBA.
    /// </summary>
    private static byte[] DrawBackground(Font font, int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        var borderColor = Color.FromRgba(100, 100, 100, 200);
        image.Mutate(ctx =>
        {
            ctx.Fill(Color.FromRgba(0, 0, 0, 180), new RectangleF(0, 0, width, height));
            ctx.Fill(borderColor, new RectangleF(0, 0, width, 2));
            ctx.Fill(borderColor, new RectangleF(0, height - 2, width, 2));
            ctx.Fill(borderColor, new RectangleF(0, 0, 2, height));
            ctx.Fill(borderColor, new RectangleF(width - 2, 0, 2, height));
            ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(TextX, TextY) },
                $"Hello from {RuntimeInformation.FrameworkDescription}", Color.White);
        });

        var pixels = new byte[width * height * 4];
        image.CopyPixelDataTo(pixels);
        return pixels;
    }

    /// <summary>
    /// The coverage (0-255) of each printable ASCII character, one cell each.
    /// </summary>
    private static byte[][] DrawGlyphs(Font font, int cellWidth, int cellHeight)
    {
        var glyphs = new byte[LastGlyph - FirstGlyph + 1][];
        using var cell = new Image<Rgba32>(cellWidth, cellHeight);
        var rgba = new byte[cellWidth * cellHeight * 4];
        for (char c = FirstGlyph; c <= LastGlyph; c++)
        {
            cell.Mutate(ctx =>
            {
                ctx.Clear(Color.Transparent);
                ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(GlyphPad, 0) }, c.ToString(), Color.White);
            });
            cell.CopyPixelDataTo(rgba);

            var coverage = new byte[cellWidth * cellHeight];
            for (int i = 0; i < coverage.Length; i++)
            {
                coverage[i] = rgba[i * 4 + 3];
            }
            glyphs[c - FirstGlyph] = coverage;
        }
        return glyphs;
    }

    /// <summary>
    /// Render the overlay with current frame count and GC stats.
    /// </summary>
    public void RenderOverlay(long frameCount)
    {
        _background.CopyTo(_pixelBuffer, 0);

        int gc0 = GC.CollectionCount(0);
        double heapMB = GC.GetTotalMemory(false) / 1_000_000.0;

        Span<char> line = stackalloc char[64];
        if (line.TryWrite($"Frame: {frameCount}", out int length))
        {
            DrawLine(line[..length], TextY + LineHeight);
        }
        if (line.TryWrite($"GC0: {gc0}  Heap: {heapMB:F1} MB", out length))
        {
            DrawLine(line[..length], TextY + LineHeight * 2);
        }
    }

    /// <summary>
    /// Blends white text into the pixel buffer, one glyph cell per character.
    /// </summary>
    private void DrawLine(ReadOnlySpan<char> text, float y)
    {
        int top = (int)y;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c <= FirstGlyph || c > LastGlyph)
            {
                continue;
            }

            int left = (int)MathF.Round(TextX + i * _advance) - GlyphPad;
            byte[] coverage = _glyphs[c - FirstGlyph];
            for (int row = 0; row < _cellHeight; row++)
            {
                int py = top + row;
                if ((uint)py >= (uint)OverlayHeight)
                {
                    continue;
                }

                for (int col = 0; col < _cellWidth; col++)
                {
                    int px = left + col;
                    int a = coverage[row * _cellWidth + col];
                    if (a == 0 || (uint)px >= (uint)OverlayWidth)
                    {
                        continue;
                    }

                    // White over the pixel, source-over with straight alpha.
                    int o = (py * OverlayWidth + px) * 4;
                    int dstWeight = _pixelBuffer[o + 3] * (255 - a);
                    int outAlpha = a + (dstWeight + 127) / 255;
                    int scale = outAlpha * 255;
                    for (int ch = 0; ch < 3; ch++)
                    {
                        _pixelBuffer[o + ch] = (byte)((255 * 255 * a + _pixelBuffer[o + ch] * dstWeight + scale / 2) / scale);
                    }
                    _pixelBuffer[o + 3] = (byte)outAlpha;
                }
            }
        }
    }

    /// <summary>
    /// Get the overlay pixels as RGBA byte array.
    /// </summary>
    public byte[] GetOverlayPixels() => _pixelBuffer;

#if DEBUG
    /// <summary>
    /// Save the current overlay to a file for debugging.
    /// </summary>
    public void SaveOverlayToFile(string path)
    {
        using var image = Image.LoadPixelData<Rgba32>(_pixelBuffer, OverlayWidth, OverlayHeight);
        image.SaveAsPng(path);
    }
#endif

    public void Dispose()
    {
    }
}
