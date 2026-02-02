// Subtitle canvas for rendering text to an ImageSharp image
// Renders parsed subtitle segments with styling (outline, shadow, background)

using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SubtitleRenderer;

/// <summary>
/// Horizontal alignment for subtitle text.
/// </summary>
public enum SubtitleAlignment
{
    Left,
    Center,
    Right
}

/// <summary>
/// Renders subtitle text segments to an ImageSharp image.
/// Supports styled text with outline, shadow, and background box.
/// </summary>
public sealed class SubtitleCanvas : IDisposable
{
    private Image<Rgba32>? _canvas;
    private int _width;
    private int _height;
    private byte[]? _pixelBuffer;
    private bool _disposed;

    // Padding around text for background box
    private const int BackgroundPadding = 4;

    // Margin from canvas edges
    private const int CanvasMargin = 10;

    /// <summary>
    /// Current canvas width in pixels.
    /// </summary>
    public int Width => _width;

    /// <summary>
    /// Current canvas height in pixels.
    /// </summary>
    public int Height => _height;

    /// <summary>
    /// Creates a new subtitle canvas with the specified dimensions.
    /// </summary>
    /// <param name="width">Canvas width in pixels.</param>
    /// <param name="height">Canvas height in pixels.</param>
    public SubtitleCanvas(int width, int height)
    {
        EnsureSize(width, height);
    }

    /// <summary>
    /// Ensures the canvas is at least the specified size.
    /// Reallocates only if dimensions change.
    /// </summary>
    private void EnsureSize(int width, int height)
    {
        if (_canvas != null && _width == width && _height == height)
        {
            return;
        }

        _canvas?.Dispose();
        _width = width;
        _height = height;
        _canvas = new Image<Rgba32>(width, height);
        _pixelBuffer = new byte[width * height * 4];
    }

    /// <summary>
    /// Renders a list of parsed segments to the canvas.
    /// </summary>
    /// <param name="segments">Text segments to render.</param>
    /// <param name="alignment">Horizontal text alignment.</param>
    public void Render(IReadOnlyList<ParsedSegment> segments, SubtitleAlignment alignment = SubtitleAlignment.Center)
    {
        if (_canvas == null || segments.Count == 0)
        {
            return;
        }

        // Clear canvas to transparent
        _canvas.Mutate(ctx => ctx.Clear(Color.Transparent));

        // Combine all segments into text lines
        // For now, treat all segments as a single text block
        // Future: handle inline styling changes

        // Get style from first segment (or use defaults)
        var primaryStyle = segments[0].Style;

        // Combine text from all segments
        string fullText = TextSegmentParser.GetCombinedText(segments);
        if (string.IsNullOrWhiteSpace(fullText))
        {
            return;
        }

        // Get font for rendering
        Font font = FontManager.GetFont(
            primaryStyle.FontName,
            primaryStyle.FontSize,
            primaryStyle.IsBold,
            primaryStyle.IsItalic);

        // Measure text to calculate layout
        FontRectangle textBounds = TextMeasurer.MeasureSize(fullText, new TextOptions(font));

        // Calculate position based on alignment
        float textX = alignment switch
        {
            SubtitleAlignment.Left => CanvasMargin,
            SubtitleAlignment.Right => _width - textBounds.Width - CanvasMargin,
            _ => (_width - textBounds.Width) / 2 // Center
        };

        // Position text near bottom of canvas (typical subtitle position)
        float textY = _height - textBounds.Height - CanvasMargin - BackgroundPadding;

        // Clamp to canvas bounds
        textX = Math.Max(CanvasMargin, Math.Min(textX, _width - textBounds.Width - CanvasMargin));
        textY = Math.Max(CanvasMargin, textY);

        // Create render options
        var textOptions = new RichTextOptions(font)
        {
            Origin = new PointF(textX, textY),
            WrappingLength = _width - (CanvasMargin * 2),
            WordBreaking = WordBreaking.Standard
        };

        // 1. Draw background box (if enabled)
        if (primaryStyle.HasBackground)
        {
            DrawBackground(textX, textY, textBounds, primaryStyle);
        }

        // 2. Draw shadow (if enabled)
        if (primaryStyle.HasShadow)
        {
            DrawShadow(textOptions, fullText, primaryStyle);
        }

        // 3. Draw outline (if enabled)
        if (primaryStyle.HasOutline)
        {
            DrawOutline(textOptions, fullText, font, primaryStyle);
        }

        // 4. Draw foreground text
        DrawText(textOptions, fullText, primaryStyle);
    }

    /// <summary>
    /// Draws a semi-transparent background box behind the text.
    /// </summary>
    private void DrawBackground(float textX, float textY, FontRectangle textBounds, SubtitleStyle style)
    {
        var bgColor = ColorFromRgbAlpha(style.BackgroundColor, style.BackgroundAlpha);
        var bgRect = new RectangleF(
            textX - BackgroundPadding,
            textY - BackgroundPadding,
            textBounds.Width + (BackgroundPadding * 2),
            textBounds.Height + (BackgroundPadding * 2));

        _canvas!.Mutate(ctx => ctx.Fill(bgColor, bgRect));
    }

    /// <summary>
    /// Draws a drop shadow offset from the text.
    /// </summary>
    private void DrawShadow(RichTextOptions baseOptions, string text, SubtitleStyle style)
    {
        var shadowColor = ColorFromRgbAlpha(style.ShadowColor, style.ShadowAlpha);
        int offset = style.ShadowOffset;

        var shadowOptions = new RichTextOptions(baseOptions.Font)
        {
            Origin = new PointF(baseOptions.Origin.X + offset, baseOptions.Origin.Y + offset),
            WrappingLength = baseOptions.WrappingLength,
            WordBreaking = baseOptions.WordBreaking
        };

        _canvas!.Mutate(ctx => ctx.DrawText(shadowOptions, text, shadowColor));
    }

    /// <summary>
    /// Draws an outline/stroke around the text.
    /// Uses multiple offset draws to simulate stroke.
    /// </summary>
    private void DrawOutline(RichTextOptions baseOptions, string text, Font font, SubtitleStyle style)
    {
        var outlineColor = ColorFromRgbAlpha(style.OutlineColor, style.OutlineAlpha);
        int outlineWidth = style.OutlineWidth;

        // Draw text at multiple offsets to create outline effect
        // This is a simple approach; ImageSharp doesn't have native text outline
        for (int dx = -outlineWidth; dx <= outlineWidth; dx++)
        {
            for (int dy = -outlineWidth; dy <= outlineWidth; dy++)
            {
                // Skip center (that's where the fill goes)
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                // Only draw at outline distance (rough circle)
                if (Math.Abs(dx) + Math.Abs(dy) > outlineWidth * 2)
                {
                    continue;
                }

                var offsetOptions = new RichTextOptions(font)
                {
                    Origin = new PointF(baseOptions.Origin.X + dx, baseOptions.Origin.Y + dy),
                    WrappingLength = baseOptions.WrappingLength,
                    WordBreaking = baseOptions.WordBreaking
                };

                _canvas!.Mutate(ctx => ctx.DrawText(offsetOptions, text, outlineColor));
            }
        }
    }

    /// <summary>
    /// Draws the foreground text with the specified style.
    /// </summary>
    private void DrawText(RichTextOptions options, string text, SubtitleStyle style)
    {
        var textColor = ColorFromRgbAlpha(style.ForegroundColor, style.ForegroundAlpha);
        _canvas!.Mutate(ctx => ctx.DrawText(options, text, textColor));
    }

    /// <summary>
    /// Converts a 0xRRGGBB color and alpha byte to ImageSharp Color.
    /// </summary>
    private static Color ColorFromRgbAlpha(uint rgb, byte alpha)
    {
        byte r = (byte)((rgb >> 16) & 0xFF);
        byte g = (byte)((rgb >> 8) & 0xFF);
        byte b = (byte)(rgb & 0xFF);
        return Color.FromRgba(r, g, b, alpha);
    }

    /// <summary>
    /// Gets the rendered image pixels as RGBA byte array.
    /// </summary>
    public byte[] GetPixels()
    {
        if (_canvas == null || _pixelBuffer == null)
        {
            return Array.Empty<byte>();
        }

        _canvas.CopyPixelDataTo(_pixelBuffer);
        return _pixelBuffer;
    }

    /// <summary>
    /// Gets the underlying Image for direct access.
    /// </summary>
    public Image<Rgba32>? GetImage() => _canvas;

    /// <summary>
    /// Saves the current canvas to a PNG file for debugging.
    /// </summary>
    /// <param name="path">File path to save to.</param>
    public void SaveDebugImage(string path)
    {
        _canvas?.SaveAsPng(path);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _canvas?.Dispose();
        _canvas = null;
        _pixelBuffer = null;
        _disposed = true;
    }
}
