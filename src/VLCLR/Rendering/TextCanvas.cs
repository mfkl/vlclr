// Text canvas for rendering styled text to an ImageSharp image
// Renders text segments with outline, shadow, and background effects

using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using VLCLR.Text;

namespace VLCLR.Rendering;

/// <summary>
/// Horizontal alignment for rendered text.
/// </summary>
public enum TextAlignment
{
    Left,
    Center,
    Right
}

/// <summary>
/// Configuration options for text canvas rendering.
/// </summary>
public sealed class TextCanvasOptions
{
    /// <summary>
    /// Padding around text for background box (in pixels).
    /// </summary>
    public int BackgroundPadding { get; init; } = 4;

    /// <summary>
    /// Margin from canvas edges (in pixels).
    /// </summary>
    public int CanvasMargin { get; init; } = 10;

    /// <summary>
    /// Vertical position mode for text placement.
    /// </summary>
    public TextVerticalPosition VerticalPosition { get; init; } = TextVerticalPosition.Bottom;

    /// <summary>
    /// Custom Y position (0.0 = top, 1.0 = bottom) when VerticalPosition is Custom.
    /// </summary>
    public float CustomVerticalPosition { get; init; } = 0.9f;
}

/// <summary>
/// Vertical positioning mode for rendered text.
/// </summary>
public enum TextVerticalPosition
{
    /// <summary>Text positioned near top of canvas.</summary>
    Top,
    /// <summary>Text positioned in center of canvas.</summary>
    Center,
    /// <summary>Text positioned near bottom of canvas (typical for subtitles).</summary>
    Bottom,
    /// <summary>Text positioned at custom vertical position.</summary>
    Custom
}

/// <summary>
/// Renders styled text segments to an ImageSharp image.
/// Supports text with outline, shadow, and background box effects.
/// </summary>
/// <remarks>
/// This class is designed for efficient rendering of subtitle-like text overlays.
/// It reuses the canvas buffer when dimensions don't change, and renders effects
/// in the correct order: background → shadow → outline → foreground text.
/// </remarks>
public sealed class TextCanvas : IDisposable
{
    private Image<Rgba32>? _canvas;
    private int _width;
    private int _height;
    private byte[]? _pixelBuffer;
    private bool _disposed;
    private readonly TextCanvasOptions _options;

    /// <summary>
    /// Current canvas width in pixels.
    /// </summary>
    public int Width => _width;

    /// <summary>
    /// Current canvas height in pixels.
    /// </summary>
    public int Height => _height;

    /// <summary>
    /// Creates a new text canvas with the specified dimensions and default options.
    /// </summary>
    /// <param name="width">Canvas width in pixels.</param>
    /// <param name="height">Canvas height in pixels.</param>
    public TextCanvas(int width, int height) : this(width, height, new TextCanvasOptions())
    {
    }

    /// <summary>
    /// Creates a new text canvas with the specified dimensions and options.
    /// </summary>
    /// <param name="width">Canvas width in pixels.</param>
    /// <param name="height">Canvas height in pixels.</param>
    /// <param name="options">Rendering options.</param>
    public TextCanvas(int width, int height, TextCanvasOptions options)
    {
        _options = options ?? new TextCanvasOptions();
        EnsureSize(width, height);
    }

    /// <summary>
    /// Ensures the canvas is at least the specified size.
    /// Reallocates only if dimensions change.
    /// </summary>
    public void EnsureSize(int width, int height)
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
    /// Renders a list of parsed text segments to the canvas.
    /// </summary>
    /// <param name="segments">Text segments to render.</param>
    /// <param name="alignment">Horizontal text alignment.</param>
    public void Render(IReadOnlyList<ParsedTextSegment> segments, TextAlignment alignment = TextAlignment.Center)
    {
        if (_canvas == null || segments.Count == 0)
        {
            return;
        }

        // Clear canvas to transparent
        _canvas.Mutate(ctx => ctx.Clear(Color.Transparent));

        // Get style from first segment (or use defaults)
        var primaryStyle = segments[0].Style;

        // Combine text from all segments
        string fullText = TextSegmentParser.GetCombinedText(segments);
        if (string.IsNullOrWhiteSpace(fullText))
        {
            return;
        }

        RenderText(fullText, primaryStyle, alignment);
    }

    /// <summary>
    /// Renders text with the specified style wrapper.
    /// </summary>
    /// <param name="text">Text to render.</param>
    /// <param name="style">Style configuration.</param>
    /// <param name="alignment">Horizontal text alignment.</param>
    public void RenderText(string text, TextStyleWrapper style, TextAlignment alignment = TextAlignment.Center)
    {
        if (_canvas == null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // Clear canvas to transparent
        _canvas.Mutate(ctx => ctx.Clear(Color.Transparent));

        // Get font for rendering
        Font font = FontManager.GetFont(
            style.FontName,
            style.FontSize,
            style.IsBold,
            style.IsItalic);

        // Measure text to calculate layout
        FontRectangle textBounds = TextMeasurer.MeasureSize(text, new TextOptions(font));

        // Account for stroke extending beyond measured glyph bounds
        float outlineExtra = style.HasOutline ? style.OutlineWidth : 0;
        float effectiveMargin = _options.CanvasMargin + outlineExtra;

        // Calculate position based on alignment
        float textX = alignment switch
        {
            TextAlignment.Left => effectiveMargin,
            TextAlignment.Right => _width - textBounds.Width - effectiveMargin,
            _ => (_width - textBounds.Width) / 2 // Center
        };

        // Calculate vertical position
        float textY = _options.VerticalPosition switch
        {
            TextVerticalPosition.Top => effectiveMargin + _options.BackgroundPadding,
            TextVerticalPosition.Center => (_height - textBounds.Height) / 2,
            TextVerticalPosition.Bottom => _height - textBounds.Height - effectiveMargin - _options.BackgroundPadding,
            TextVerticalPosition.Custom => (_height * _options.CustomVerticalPosition) - (textBounds.Height / 2),
            _ => _height - textBounds.Height - effectiveMargin - _options.BackgroundPadding
        };

        // Clamp to canvas bounds
        textX = Math.Max(effectiveMargin, Math.Min(textX, _width - textBounds.Width - effectiveMargin));
        textY = Math.Max(effectiveMargin, Math.Min(textY, _height - textBounds.Height - effectiveMargin));

        // Create render options
        var textOptions = new RichTextOptions(font)
        {
            Origin = new PointF(textX, textY),
            WrappingLength = _width - (effectiveMargin * 2),
            WordBreaking = WordBreaking.Standard
        };

        // Render in correct order: background → shadow → outline → text
        if (style.HasBackground)
        {
            DrawBackground(textX, textY, textBounds, style);
        }

        if (style.HasShadow)
        {
            DrawShadow(textOptions, text, style);
        }

        DrawForeground(textOptions, text, style);
    }

    /// <summary>
    /// Draws a semi-transparent background box behind the text.
    /// </summary>
    private void DrawBackground(float textX, float textY, FontRectangle textBounds, TextStyleWrapper style)
    {
        var bgColor = ColorFromRgbAlpha(style.BackgroundColor, style.BackgroundAlpha);
        var bgRect = new RectangleF(
            textX - _options.BackgroundPadding,
            textY - _options.BackgroundPadding,
            textBounds.Width + (_options.BackgroundPadding * 2),
            textBounds.Height + (_options.BackgroundPadding * 2));

        _canvas!.Mutate(ctx => ctx.Fill(bgColor, bgRect));
    }

    /// <summary>
    /// Draws a drop shadow offset from the text.
    /// </summary>
    private void DrawShadow(RichTextOptions baseOptions, string text, TextStyleWrapper style)
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
    /// Draws the foreground text with the specified style.
    /// When outline is enabled, draws text at 8 offsets (cardinal + diagonal)
    /// then the fill on top.
    /// </summary>
    private void DrawForeground(RichTextOptions options, string text, TextStyleWrapper style)
    {
        var textColor = ColorFromRgbAlpha(style.ForegroundColor, style.ForegroundAlpha);
        var brush = new SolidBrush(textColor);

        if (style.HasOutline)
        {
            var outlineColor = ColorFromRgbAlpha(style.OutlineColor, style.OutlineAlpha);
            int w = style.OutlineWidth;
            _canvas!.Mutate(ctx =>
            {
                // Draw outline at cardinal and diagonal offsets
                foreach (var (dx, dy) in new (int, int)[]
                {
                    (-w, 0), (w, 0), (0, -w), (0, w),
                    (-w, -w), (-w, w), (w, -w), (w, w)
                })
                {
                    var outlineOptions = new RichTextOptions(options.Font)
                    {
                        Origin = new PointF(options.Origin.X + dx, options.Origin.Y + dy),
                        WrappingLength = options.WrappingLength,
                        WordBreaking = options.WordBreaking
                    };
                    ctx.DrawText(outlineOptions, text, outlineColor);
                }

                // Fill on top
                ctx.DrawText(options, text, brush);
            });
        }
        else
        {
            _canvas!.Mutate(ctx => ctx.DrawText(options, text, brush));
        }
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

    /// <summary>
    /// Clears the canvas to transparent.
    /// </summary>
    public void Clear()
    {
        _canvas?.Mutate(ctx => ctx.Clear(Color.Transparent));
    }

    /// <inheritdoc/>
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
