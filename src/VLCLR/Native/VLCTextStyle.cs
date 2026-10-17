// VLC text style structure
// Source: vlc/include/vlc_text_style.h
// VLC Version: 4.0.6

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Text style structure (text_style_t from vlc_text_style.h)
/// Used for formatting text in subtitle rendering.
/// </summary>
/// <remarks>
/// Offsets and size come from the generated <c>Interop.TextStyle</c> layout.
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = Interop.TextStyle.MaxSizeOf)]
public struct VLCTextStyle
{
    /// <summary>Font family name (psz_fontname)</summary>
    [FieldOffset(Interop.TextStyle.Offsets.Fontname)]
    public nint FontName;

    /// <summary>Monospace font family name (psz_monofontname)</summary>
    [FieldOffset(Interop.TextStyle.Offsets.Monofontname)]
    public nint MonoFontName;

    /// <summary>Feature flags indicating which fields are set (i_features)</summary>
    [FieldOffset(Interop.TextStyle.Offsets.Features)]
    public ushort Features;

    /// <summary>Style flags for bold, italic, etc. (i_style_flags)</summary>
    [FieldOffset(Interop.TextStyle.Offsets.StyleFlags)]
    public ushort StyleFlags;

    /// <summary>Font size relative to video height in percent (f_font_relsize)</summary>
    [FieldOffset(Interop.TextStyle.Offsets.FontRelsize)]
    public float FontRelativeSize;

    /// <summary>Font size in pixels (i_font_size)</summary>
    [FieldOffset(Interop.TextStyle.Offsets.FontSize)]
    public int FontSize;

    /// <summary>Font color in 0x00RRGGBB format (i_font_color)</summary>
    [FieldOffset(Interop.TextStyle.Offsets.FontColor)]
    public uint FontColor;

    /// <summary>Font alpha/transparency, 255 = opaque (i_font_alpha)</summary>
    [FieldOffset(Interop.TextStyle.Offsets.FontAlpha)]
    public byte FontAlpha;

    /// <summary>Spacing between glyphs in pixels (i_spacing)</summary>
    [FieldOffset(Interop.TextStyle.Offsets.Spacing)]
    public int Spacing;

    /// <summary>Outline color in 0x00RRGGBB format (i_outline_color)</summary>
    [FieldOffset(Interop.TextStyle.Offsets.OutlineColor)]
    public uint OutlineColor;

    /// <summary>Outline alpha/transparency (i_outline_alpha)</summary>
    [FieldOffset(Interop.TextStyle.Offsets.OutlineAlpha)]
    public byte OutlineAlpha;

    /// <summary>Outline width in pixels (i_outline_width)</summary>
    [FieldOffset(Interop.TextStyle.Offsets.OutlineWidth)]
    public int OutlineWidth;

    /// <summary>Shadow color in 0x00RRGGBB format (i_shadow_color)</summary>
    [FieldOffset(Interop.TextStyle.Offsets.ShadowColor)]
    public uint ShadowColor;

    /// <summary>Shadow alpha/transparency (i_shadow_alpha)</summary>
    [FieldOffset(Interop.TextStyle.Offsets.ShadowAlpha)]
    public byte ShadowAlpha;

    /// <summary>Shadow width/offset in pixels (i_shadow_width)</summary>
    [FieldOffset(Interop.TextStyle.Offsets.ShadowWidth)]
    public int ShadowWidth;

    /// <summary>Background color in 0x00RRGGBB format (i_background_color)</summary>
    [FieldOffset(Interop.TextStyle.Offsets.BackgroundColor)]
    public uint BackgroundColor;

    /// <summary>Background alpha/transparency (i_background_alpha)</summary>
    [FieldOffset(Interop.TextStyle.Offsets.BackgroundAlpha)]
    public byte BackgroundAlpha;

    /// <summary>Line wrapping mode (e_wrapinfo)</summary>
    [FieldOffset(Interop.TextStyle.Offsets.Wrapinfo)]
    public VLCTextWrapMode WrapMode;
}

/// <summary>
/// Text wrapping mode enumeration
/// </summary>
public enum VLCTextWrapMode
{
    /// <summary>Breaks on whitespace or fallback on character</summary>
    Default = (int)Interop.VlcTextStyle.StyleWrapDefault,
    /// <summary>Breaks at character level only</summary>
    Character = (int)Interop.VlcTextStyle.StyleWrapChar,
    /// <summary>No line breaks except explicit ones</summary>
    None = (int)Interop.VlcTextStyle.StyleWrapNone
}

/// <summary>
/// Feature flags indicating which style fields have been explicitly set.
/// Used for merging styles - only set fields are merged.
/// </summary>
public static class VLCTextStyleFeatures
{
    /// <summary>No features set, all defaults</summary>
    public const ushort NoDefaults = (ushort)Interop.VlcTextStyle.StyleNoDefaults;

    /// <summary>All features set</summary>
    public const ushort FullySet = (ushort)Interop.VlcTextStyle.StyleFullySet;

    /// <summary>Font color is set</summary>
    public const ushort HasFontColor = (ushort)Interop.VlcTextStyle.StyleHasFontColor;

    /// <summary>Font alpha is set</summary>
    public const ushort HasFontAlpha = (ushort)Interop.VlcTextStyle.StyleHasFontAlpha;

    /// <summary>Style flags (bold, italic, etc.) are set</summary>
    public const ushort HasFlags = (ushort)Interop.VlcTextStyle.StyleHasFlags;

    /// <summary>Outline color is set</summary>
    public const ushort HasOutlineColor = (ushort)Interop.VlcTextStyle.StyleHasOutlineColor;

    /// <summary>Outline alpha is set</summary>
    public const ushort HasOutlineAlpha = (ushort)Interop.VlcTextStyle.StyleHasOutlineAlpha;

    /// <summary>Shadow color is set</summary>
    public const ushort HasShadowColor = (ushort)Interop.VlcTextStyle.StyleHasShadowColor;

    /// <summary>Shadow alpha is set</summary>
    public const ushort HasShadowAlpha = (ushort)Interop.VlcTextStyle.StyleHasShadowAlpha;

    /// <summary>Background color is set</summary>
    public const ushort HasBackgroundColor = (ushort)Interop.VlcTextStyle.StyleHasBackgroundColor;

    /// <summary>Background alpha is set</summary>
    public const ushort HasBackgroundAlpha = (ushort)Interop.VlcTextStyle.StyleHasBackgroundAlpha;

    /// <summary>Wrap info is set</summary>
    public const ushort HasWrapInfo = (ushort)Interop.VlcTextStyle.StyleHasWrapInfo;
}

/// <summary>
/// Style flags for text formatting (bold, italic, outline, etc.)
/// </summary>
public static class VLCTextStyleFlags
{
    /// <summary>Bold text</summary>
    public const ushort Bold = (ushort)Interop.VlcTextStyle.StyleBold;

    /// <summary>Italic text</summary>
    public const ushort Italic = (ushort)Interop.VlcTextStyle.StyleItalic;

    /// <summary>Draw outline around text</summary>
    public const ushort Outline = (ushort)Interop.VlcTextStyle.StyleOutline;

    /// <summary>Draw drop shadow</summary>
    public const ushort Shadow = (ushort)Interop.VlcTextStyle.StyleShadow;

    /// <summary>Draw background box</summary>
    public const ushort Background = (ushort)Interop.VlcTextStyle.StyleBackground;

    /// <summary>Underlined text</summary>
    public const ushort Underline = (ushort)Interop.VlcTextStyle.StyleUnderline;

    /// <summary>Strikethrough text</summary>
    public const ushort Strikeout = (ushort)Interop.VlcTextStyle.StyleStrikeout;

    /// <summary>Half-width characters</summary>
    public const ushort HalfWidth = (ushort)Interop.VlcTextStyle.StyleHalfwidth;

    /// <summary>Use monospaced font</summary>
    public const ushort Monospaced = (ushort)Interop.VlcTextStyle.StyleMonospaced;

    /// <summary>Double-width characters</summary>
    public const ushort DoubleWidth = (ushort)Interop.VlcTextStyle.StyleDoublewidth;

    /// <summary>Blinking foreground</summary>
    public const ushort BlinkForeground = (ushort)Interop.VlcTextStyle.StyleBlinkForeground;

    /// <summary>Blinking background</summary>
    public const ushort BlinkBackground = (ushort)Interop.VlcTextStyle.StyleBlinkBackground;
}

/// <summary>
/// Alpha/transparency constants for text styles
/// </summary>
public static class VLCTextStyleAlpha
{
    /// <summary>Fully opaque (255)</summary>
    public const byte Opaque = (byte)Interop.VlcTextStyle.StyleAlphaOpaque;

    /// <summary>Fully transparent (0)</summary>
    public const byte Transparent = (byte)Interop.VlcTextStyle.StyleAlphaTransparent;
}

/// <summary>
/// Default values for text styles
/// </summary>
public static class VLCTextStyleDefaults
{
    /// <summary>Default font size in pixels</summary>
    public const int FontSize = Interop.VlcTextStyle.StyleDefaultFontSize;

    /// <summary>Default relative font size (percentage of video height)</summary>
    public const float RelativeFontSize = (float)Interop.VlcTextStyle.StyleDefaultRelFontSize;
}
