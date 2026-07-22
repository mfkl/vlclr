// Text canvas for rendering styled text to an ImageSharp image
// Renders text segments with outline, shadow, and background effects

using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
#if DEBUG
using SixLabors.ImageSharp.Formats.Png;
#endif
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using VLCLR.Text;

namespace VLCLR.Rendering;

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
    // Minimal ImageSharp configuration with no format decoders/encoders registered.
    // This allows the trimmer to strip all image codec code (PNG, JPEG, GIF, BMP, TIFF, etc.)
    // since we only create blank images, draw on them, and copy raw pixels out.
    private static readonly Configuration s_minimalConfig = new();

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
        _canvas = new Image<Rgba32>(s_minimalConfig, width, height);
        _pixelBuffer = null;
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

        // Get style from first segment (or use defaults)
        var primaryStyle = segments[0].Style;

        // Combine text from all segments
        string fullText = TextSegmentParser.GetCombinedText(segments);
        if (string.IsNullOrWhiteSpace(fullText))
        {
            Clear();
            return;
        }

        RenderText(fullText, primaryStyle, alignment);
    }

    /// <summary>
    /// Renders parsed text into a tightly-sized image no wider than
    /// <paramref name="maximumWidth"/>.
    /// </summary>
    /// <remarks>
    /// The returned image is owned by this canvas and remains valid until the
    /// next render, resize, or disposal. This path is intended for VLC
    /// subpicture regions, where VLC positions the compact image on the video.
    /// </remarks>
    public Image<Rgba32>? RenderRegion(
        IReadOnlyList<ParsedTextSegment> segments,
        int maximumWidth,
        TextAlignment alignment = TextAlignment.Center)
    {
        if (segments.Count == 0)
        {
            Clear();
            return null;
        }

        string fullText = TextSegmentParser.GetCombinedText(segments);
        if (string.IsNullOrWhiteSpace(fullText))
        {
            Clear();
            return null;
        }

        return RenderTextRegion(fullText, segments[0].Style, maximumWidth, alignment);
    }

    /// <summary>
    /// Renders text into a tightly-sized image no wider than
    /// <paramref name="maximumWidth"/>.
    /// </summary>
    /// <remarks>
    /// The returned image is owned by this canvas and remains valid until the
    /// next render, resize, or disposal.
    /// </remarks>
    public Image<Rgba32>? RenderTextRegion(
        string text,
        TextStyleWrapper style,
        int maximumWidth,
        TextAlignment alignment = TextAlignment.Center)
    {
        if (string.IsNullOrWhiteSpace(text) || maximumWidth <= 0)
        {
            Clear();
            return null;
        }

        Font font = GetFont(style);
        int outlinePadding = style.HasOutline ? style.OutlineWidth : 0;
        int backgroundPadding = style.HasBackground ? _options.BackgroundPadding : 0;
        int effectPadding = outlinePadding + backgroundPadding;
        int shadowOffset = style.HasShadow ? style.ShadowOffset : 0;

        int leftPadding = _options.CanvasMargin + effectPadding + Math.Max(0, -shadowOffset);
        int topPadding = _options.CanvasMargin + effectPadding + Math.Max(0, -shadowOffset);
        int rightPadding = _options.CanvasMargin + effectPadding + Math.Max(0, shadowOffset);
        int bottomPadding = _options.CanvasMargin + effectPadding + Math.Max(0, shadowOffset);
        int contentLimit = Math.Max(1, maximumWidth - leftPadding - rightPadding);

        var measureOptions = new TextOptions(font)
        {
            WrappingLength = contentLimit,
            WordBreaking = WordBreaking.Standard
        };
        FontRectangle textBounds = TextMeasurer.MeasureSize(text, measureOptions);
        // MeasureSize reports glyph bounds while wrapping uses advance width.
        // Keep one em of slack so a final word is not wrapped by that difference.
        int layoutSlack = Math.Max(2, style.FontSize);
        int contentWidth = Math.Clamp(
            (int)Math.Ceiling(textBounds.Width) + layoutSlack,
            1,
            contentLimit);

        // Measure once more at the actual width so the reported height includes
        // any wrapping caused by rounding to the compact region.
        measureOptions.WrappingLength = contentWidth;
        textBounds = TextMeasurer.MeasureSize(text, measureOptions);

        int regionWidth = Math.Min(maximumWidth, contentWidth + leftPadding + rightPadding);
        int regionHeight = Math.Max(1, (int)Math.Ceiling(textBounds.Height) + topPadding + bottomPadding);
        EnsureSize(regionWidth, regionHeight);

        var textOptions = new RichTextOptions(font)
        {
            Origin = new PointF(leftPadding, topPadding),
            WrappingLength = contentWidth,
            WordBreaking = WordBreaking.Standard,
            TextAlignment = alignment switch
            {
                TextAlignment.Left => SixLabors.Fonts.TextAlignment.Start,
                TextAlignment.Right => SixLabors.Fonts.TextAlignment.End,
                _ => SixLabors.Fonts.TextAlignment.Center
            }
        };

        DrawText(text, style, textOptions, leftPadding, topPadding, textBounds);
        return _canvas;
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

        Font font = GetFont(style);

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

        DrawText(text, style, textOptions, textX, textY, textBounds);
    }

    private static Font GetFont(TextStyleWrapper style) => FontManager.GetFont(
        style.FontName,
        style.FontSize,
        style.IsBold,
        style.IsItalic);

    private void DrawText(
        string text,
        TextStyleWrapper style,
        RichTextOptions textOptions,
        float backgroundX,
        float backgroundY,
        FontRectangle textBounds)
    {
        Color backgroundColor = ColorFromRgbAlpha(style.BackgroundColor, style.BackgroundAlpha);
        var bgRect = new RectangleF(
            backgroundX - _options.BackgroundPadding,
            backgroundY - _options.BackgroundPadding,
            textBounds.Width + (_options.BackgroundPadding * 2),
            textBounds.Height + (_options.BackgroundPadding * 2));

        Color shadowColor = ColorFromRgbAlpha(style.ShadowColor, style.ShadowAlpha);
        var shadowOptions = new RichTextOptions(textOptions)
        {
            Origin = new PointF(textOptions.Origin.X + style.ShadowOffset, textOptions.Origin.Y + style.ShadowOffset)
        };

        var textBrush = new SolidBrush(ColorFromRgbAlpha(style.ForegroundColor, style.ForegroundAlpha));
        var outlinePen = new SolidPen(
            ColorFromRgbAlpha(style.OutlineColor, style.OutlineAlpha),
            Math.Max(1f, style.OutlineWidth * 2f));

        // Queue every operation on one processing context. Drawing the outline
        // once and then restoring the fill produces the same outward outline
        // width as the former eight offset rasterizations with far less work.
        _canvas!.Mutate(ctx =>
        {
            ctx.Clear(Color.Transparent);

            if (style.HasBackground)
            {
                ctx.Fill(backgroundColor, bgRect);
            }

            if (style.HasShadow)
            {
                ctx.DrawText(shadowOptions, text, shadowColor);
            }

            if (style.HasOutline)
            {
                ctx.DrawText(textOptions, text, outlinePen);
                ctx.DrawText(textOptions, text, textBrush);
            }
            else
            {
                ctx.DrawText(textOptions, text, textBrush);
            }
        });
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
        if (_canvas == null)
        {
            return Array.Empty<byte>();
        }

        int requiredLength = checked(_width * _height * 4);
        if (_pixelBuffer == null || _pixelBuffer.Length != requiredLength)
        {
            _pixelBuffer = GC.AllocateUninitializedArray<byte>(requiredLength);
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
    /// Only available in Debug builds to avoid linking the PNG encoder in Release.
    /// </summary>
    /// <param name="path">File path to save to.</param>
    [System.Diagnostics.Conditional("DEBUG")]
    public void SaveDebugImage(string path)
    {
#if DEBUG
        _canvas?.Save(path, new PngEncoder());
#endif
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
