// Text canvas for rendering styled text to an ImageSharp image
// Renders text segments with outline, shadow, and background effects

using System.Runtime.InteropServices;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
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
/// The text is laid out and rasterized once per render, into a coverage mask
/// the fill, the shadow (the mask shifted) and the outline (the mask grown by
/// the outline width) are blended from: drawing each with ImageSharp laid the
/// text out again each time and stroked the outline as a new path, about
/// 2 MB per render for one outlined line.
/// </remarks>
public sealed class TextCanvas : IDisposable
{
    // Minimal ImageSharp configuration with no format decoders/encoders registered.
    // This allows the trimmer to strip all image codec code (PNG, JPEG, GIF, BMP, TIFF, etc.)
    // since we only create blank images, draw on them, and copy raw pixels out.
    // Contiguous buffers let the blending work on one span per image.
    private static readonly Configuration s_minimalConfig = new() { PreferContiguousImageBuffers = true };

    private Image<Rgba32>? _canvas;
    private Image<A8>? _mask;
    private byte[] _outline = [];
    private readonly Dictionary<int, DiscKernel> _kernels = new();
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
        _mask?.Dispose();
        _width = width;
        _height = height;
        _canvas = new Image<Rgba32>(s_minimalConfig, width, height);
        _mask = new Image<A8>(s_minimalConfig, width, height);
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
        var bgRect = new RectangleF(
            backgroundX - _options.BackgroundPadding,
            backgroundY - _options.BackgroundPadding,
            textBounds.Width + (_options.BackgroundPadding * 2),
            textBounds.Height + (_options.BackgroundPadding * 2));

        // The background is the one shape ImageSharp still draws: an
        // antialiased rectangle at a fractional position.
        Image<Rgba32> canvasImage = _canvas!;
        Image<A8> maskImage = _mask!;
        Color backgroundColor = ColorFromRgbAlpha(style.BackgroundColor, style.BackgroundAlpha);
        bool hasBackground = style.HasBackground;
        canvasImage.Mutate(ctx =>
        {
            ctx.Clear(Color.Transparent);
            if (hasBackground)
            {
                ctx.Fill(backgroundColor, bgRect);
            }
        });

        // Lay out and rasterize the text once.
        IPathCollection glyphs = TextBuilder.GenerateGlyphs(text, textOptions);
        maskImage.Mutate(ctx => ctx.Clear(Color.Transparent).Fill(Color.White, glyphs));

        if (!canvasImage.DangerousTryGetSinglePixelMemory(out Memory<Rgba32> canvasMemory)
            || !maskImage.DangerousTryGetSinglePixelMemory(out Memory<A8> maskMemory))
        {
            throw new InvalidOperationException("Text canvas buffers are not contiguous.");
        }

        Span<Rgba32> canvas = canvasMemory.Span;
        ReadOnlySpan<byte> mask = MemoryMarshal.AsBytes(maskMemory.Span);

        if (style.HasShadow)
        {
            BlendShifted(canvas, mask, style.ShadowOffset, style.ShadowColor, style.ShadowAlpha);
        }

        if (style.HasOutline)
        {
            // The pen this replaces was centered on the glyph edges, twice the
            // outline width wide: it covered the width outside the edge and the
            // width inside, which shows only under a translucent fill.
            int radius = Math.Max(1, style.OutlineWidth);
            ReadOnlySpan<byte> outline = Outline(mask, radius, insideBandOnly: style.ForegroundAlpha < 255);
            Blend(canvas, outline, style.OutlineColor, style.OutlineAlpha);
        }

        Blend(canvas, mask, style.ForegroundColor, style.ForegroundAlpha);
    }

    /// <summary>
    /// The coverage of the mask grown by <paramref name="radius"/> pixels (a disc),
    /// less, when <paramref name="insideBandOnly"/>, the mask shrunk by as much.
    /// </summary>
    private ReadOnlySpan<byte> Outline(ReadOnlySpan<byte> mask, int radius, bool insideBandOnly)
    {
        if (_outline.Length < mask.Length)
        {
            _outline = new byte[mask.Length];
        }

        Span<byte> outline = _outline.AsSpan(0, mask.Length);
        if (!_kernels.TryGetValue(radius, out DiscKernel? kernel))
        {
            kernel = new DiscKernel(radius);
            _kernels[radius] = kernel;
        }

        // Only the ink's bounds grown by the kernel can be covered: the text
        // is a small part of the canvas, and the kernel grows as r².
        outline.Clear();
        int minX = _width, maxX = -1, minY = -1, maxY = -1;
        for (int y = 0; y < _height; y++)
        {
            ReadOnlySpan<byte> row = mask.Slice(y * _width, _width);
            int first = row.IndexOfAnyExcept((byte)0);
            if (first < 0)
            {
                continue;
            }
            minX = Math.Min(minX, first);
            maxX = Math.Max(maxX, row.LastIndexOfAnyExcept((byte)0));
            minY = minY < 0 ? y : minY;
            maxY = y;
        }
        if (maxY < 0)
        {
            return outline;
        }

        int x0 = Math.Max(0, minX - radius - 1), x1 = Math.Min(_width - 1, maxX + radius + 1);
        int y0 = Math.Max(0, minY - radius - 1), y1 = Math.Min(_height - 1, maxY + radius + 1);

        // A pixel q of coverage c puts the glyph edge about c - 1/2 pixel past
        // its center toward p, so the grown edge covers p by r - |pq| + c.
        int reach = radius * 255;
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                int grown = 0;
                int outside = 0;
                for (int k = 0; k < kernel.Count; k++)
                {
                    int sx = x + kernel.Dx[k];
                    int sy = y + kernel.Dy[k];
                    int coverage = (uint)sx < (uint)_width && (uint)sy < (uint)_height ? mask[sy * _width + sx] : 0;
                    int left = reach - kernel.Distance[k];
                    if (coverage > 0)
                    {
                        grown = Math.Max(grown, left + coverage);
                    }
                    if (insideBandOnly && coverage < 255)
                    {
                        outside = Math.Max(outside, left + 255 - coverage);
                    }
                }

                grown = Math.Min(grown, 255);
                int shrunk = 255 - Math.Min(outside, 255);
                outline[y * _width + x] = (byte)(insideBandOnly ? Math.Max(0, grown - shrunk) : grown);
            }
        }

        return outline;
    }

    /// <summary>
    /// The offsets closer than <c>radius + 1</c> pixels, with their distance
    /// (in 1/255 pixel).
    /// </summary>
    private sealed class DiscKernel
    {
        public readonly int[] Dx;
        public readonly int[] Dy;
        public readonly int[] Distance;
        public readonly int Count;

        public DiscKernel(int radius)
        {
            var dx = new List<int>();
            var dy = new List<int>();
            var distance = new List<int>();
            for (int y = -radius - 1; y <= radius + 1; y++)
            {
                for (int x = -radius - 1; x <= radius + 1; x++)
                {
                    float d = MathF.Sqrt(x * x + y * y);
                    if (d < radius + 1)
                    {
                        dx.Add(x);
                        dy.Add(y);
                        distance.Add((int)MathF.Round(d * 255));
                    }
                }
            }

            Dx = dx.ToArray();
            Dy = dy.ToArray();
            Distance = distance.ToArray();
            Count = Distance.Length;
        }
    }

    /// <summary>A color over the canvas, with the coverage of the mask shifted by <paramref name="offset"/> on both axes.</summary>
    private void BlendShifted(Span<Rgba32> canvas, ReadOnlySpan<byte> mask, int offset, uint rgb, byte alpha)
    {
        for (int y = 0; y < _height; y++)
        {
            int sy = y - offset;
            if ((uint)sy >= (uint)_height)
            {
                continue;
            }

            for (int x = 0; x < _width; x++)
            {
                int sx = x - offset;
                if ((uint)sx < (uint)_width)
                {
                    Over(ref canvas[y * _width + x], rgb, alpha, mask[sy * _width + sx]);
                }
            }
        }
    }

    /// <summary>A color over the canvas, with the coverage of the mask.</summary>
    private static void Blend(Span<Rgba32> canvas, ReadOnlySpan<byte> coverage, uint rgb, byte alpha)
    {
        for (int i = 0; i < canvas.Length; i++)
        {
            Over(ref canvas[i], rgb, alpha, coverage[i]);
        }
    }

    /// <summary>Source-over with straight alpha, as ImageSharp's normal blending.</summary>
    private static void Over(ref Rgba32 dst, uint rgb, byte alpha, byte coverage)
    {
        if (coverage == 0 || alpha == 0)
        {
            return;
        }

        float sa = alpha * coverage / (255f * 255f);
        float da = dst.A / 255f;
        float outA = sa + da * (1 - sa);
        float dstWeight = da * (1 - sa);
        dst.R = (byte)MathF.Round((((rgb >> 16) & 0xFF) * sa + dst.R * dstWeight) / outA);
        dst.G = (byte)MathF.Round((((rgb >> 8) & 0xFF) * sa + dst.G * dstWeight) / outA);
        dst.B = (byte)MathF.Round(((rgb & 0xFF) * sa + dst.B * dstWeight) / outA);
        dst.A = (byte)MathF.Round(outA * 255);
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
        _mask?.Dispose();
        _mask = null;
        _pixelBuffer = null;
        _disposed = true;
    }
}
