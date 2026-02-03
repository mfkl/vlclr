// Subtitle style wrapper for VLC text_style_t
// Provides C# properties for text styling from VLC native structure

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VLCLR.Native;

namespace SubtitleRenderer;

/// <summary>
/// Wrapper class for VLC's text_style_t structure.
/// Provides easy access to text styling properties with sensible defaults.
/// </summary>
public sealed class SubtitleStyle
{
    // Default values for subtitle rendering
    private const string DefaultFontName = "JetBrains Mono";
    private const int DefaultFontSize = 24;
    private const uint DefaultFontColor = 0xFFFFFF; // White
    private const byte DefaultFontAlpha = 255; // Opaque
    private const uint DefaultOutlineColor = 0x000000; // Black
    private const int DefaultOutlineWidth = 2;
    private const uint DefaultShadowColor = 0x000000; // Black
    private const int DefaultShadowOffset = 2;
    private const uint DefaultBackgroundColor = 0x000000; // Black
    private const byte DefaultBackgroundAlpha = 128; // Semi-transparent

    /// <summary>Font family name.</summary>
    public string FontName { get; init; } = DefaultFontName;

    /// <summary>Font size in pixels.</summary>
    public int FontSize { get; init; } = DefaultFontSize;

    /// <summary>Font size relative to video height (0.0 - 100.0).</summary>
    public float FontRelativeSize { get; init; }

    /// <summary>Foreground/text color in 0xRRGGBB format.</summary>
    public uint ForegroundColor { get; init; } = DefaultFontColor;

    /// <summary>Foreground alpha (0 = transparent, 255 = opaque).</summary>
    public byte ForegroundAlpha { get; init; } = DefaultFontAlpha;

    /// <summary>Outline/stroke color in 0xRRGGBB format.</summary>
    public uint OutlineColor { get; init; } = DefaultOutlineColor;

    /// <summary>Outline alpha (0 = transparent, 255 = opaque).</summary>
    public byte OutlineAlpha { get; init; } = DefaultFontAlpha;

    /// <summary>Outline width in pixels.</summary>
    public int OutlineWidth { get; init; } = DefaultOutlineWidth;

    /// <summary>Shadow color in 0xRRGGBB format.</summary>
    public uint ShadowColor { get; init; } = DefaultShadowColor;

    /// <summary>Shadow alpha (0 = transparent, 255 = opaque).</summary>
    public byte ShadowAlpha { get; init; } = DefaultFontAlpha;

    /// <summary>Shadow offset in pixels.</summary>
    public int ShadowOffset { get; init; } = DefaultShadowOffset;

    /// <summary>Background color in 0xRRGGBB format.</summary>
    public uint BackgroundColor { get; init; } = DefaultBackgroundColor;

    /// <summary>Background alpha (0 = transparent, 255 = opaque).</summary>
    public byte BackgroundAlpha { get; init; } = DefaultBackgroundAlpha;

    /// <summary>Whether text should be bold.</summary>
    public bool IsBold { get; init; }

    /// <summary>Whether text should be italic.</summary>
    public bool IsItalic { get; init; }

    /// <summary>Whether text should be underlined.</summary>
    public bool IsUnderline { get; init; }

    /// <summary>Whether text should have strikethrough.</summary>
    public bool IsStrikeout { get; init; }

    /// <summary>Whether to draw outline around text.</summary>
    public bool HasOutline { get; init; }

    /// <summary>Whether to draw drop shadow.</summary>
    public bool HasShadow { get; init; }

    /// <summary>Whether to draw background box.</summary>
    public bool HasBackground { get; init; }

    /// <summary>
    /// Creates a SubtitleStyle from a native VLC text_style_t pointer.
    /// </summary>
    /// <param name="stylePtr">Pointer to VLCTextStyle struct, or nint.Zero for defaults.</param>
    /// <returns>SubtitleStyle with values from native struct or defaults.</returns>
    public static unsafe SubtitleStyle FromNative(nint stylePtr)
    {
        if (stylePtr == nint.Zero)
        {
            return new SubtitleStyle();
        }

        ref VLCTextStyle style = ref Unsafe.AsRef<VLCTextStyle>((void*)stylePtr);

        // Extract font name from native pointer
        string fontName = DefaultFontName;
        if (style.FontName != nint.Zero)
        {
            string? nativeName = Marshal.PtrToStringUTF8(style.FontName);
            if (!string.IsNullOrEmpty(nativeName))
            {
                fontName = nativeName;
            }
        }

        // Extract style flags
        ushort flags = style.StyleFlags;

        // Force white text if VLC passes black (black on dark video is invisible)
        var fgColor = style.FontColor == 0x000000 ? 0xFFFFFF : style.FontColor;
        
        // Always enable outline for visibility with thicker width
        var hasOutline = true;
        var outlineWidth = 3;

        return new SubtitleStyle
        {
            FontName = fontName,
            FontSize = style.FontSize > 0 ? style.FontSize : DefaultFontSize,
            FontRelativeSize = style.FontRelativeSize,
            ForegroundColor = fgColor,
            ForegroundAlpha = style.FontAlpha > 0 ? style.FontAlpha : DefaultFontAlpha,
            OutlineColor = style.OutlineColor,
            OutlineAlpha = style.OutlineAlpha > 0 ? style.OutlineAlpha : DefaultFontAlpha,
            OutlineWidth = outlineWidth,
            ShadowColor = style.ShadowColor,
            ShadowAlpha = style.ShadowAlpha > 0 ? style.ShadowAlpha : DefaultFontAlpha,
            ShadowOffset = style.ShadowWidth > 0 ? style.ShadowWidth : DefaultShadowOffset,
            BackgroundColor = style.BackgroundColor,
            BackgroundAlpha = style.BackgroundAlpha,
            IsBold = (flags & VLCTextStyleFlags.Bold) != 0,
            IsItalic = (flags & VLCTextStyleFlags.Italic) != 0,
            IsUnderline = (flags & VLCTextStyleFlags.Underline) != 0,
            IsStrikeout = (flags & VLCTextStyleFlags.Strikeout) != 0,
            HasOutline = hasOutline,
            HasShadow = (flags & VLCTextStyleFlags.Shadow) != 0,
            HasBackground = (flags & VLCTextStyleFlags.Background) != 0
        };
    }

    /// <summary>
    /// Returns a string representation for debugging.
    /// </summary>
    public override string ToString()
    {
        var attrs = new System.Text.StringBuilder();
        if (IsBold) attrs.Append("B");
        if (IsItalic) attrs.Append("I");
        if (IsUnderline) attrs.Append("U");
        if (IsStrikeout) attrs.Append("S");
        if (HasOutline) attrs.Append("O");
        if (HasShadow) attrs.Append("H"); // sHadow
        if (HasBackground) attrs.Append("G"); // backGround

        return $"[{FontName} {FontSize}px #{ForegroundColor:X6} {(attrs.Length > 0 ? attrs.ToString() : "-")}]";
    }
}
