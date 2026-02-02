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
/// Layout verified against VLC 4.x headers:
/// - Two pointer fields (8 bytes each on 64-bit)
/// - Two uint16 fields (4 bytes total)
/// - Float + int + uint32 + uint8 groups with alignment padding
/// - Final enum is int (4 bytes)
/// Total size: 80 bytes on 64-bit
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 80)]
public struct VLCTextStyle
{
    /// <summary>Font family name (psz_fontname)</summary>
    [FieldOffset(0)]
    public nint FontName;

    /// <summary>Monospace font family name (psz_monofontname)</summary>
    [FieldOffset(8)]
    public nint MonoFontName;

    /// <summary>Feature flags indicating which fields are set (i_features)</summary>
    [FieldOffset(16)]
    public ushort Features;

    /// <summary>Style flags for bold, italic, etc. (i_style_flags)</summary>
    [FieldOffset(18)]
    public ushort StyleFlags;

    /// <summary>Font size relative to video height in percent (f_font_relsize)</summary>
    [FieldOffset(20)]
    public float FontRelativeSize;

    /// <summary>Font size in pixels (i_font_size)</summary>
    [FieldOffset(24)]
    public int FontSize;

    /// <summary>Font color in 0x00RRGGBB format (i_font_color)</summary>
    [FieldOffset(28)]
    public uint FontColor;

    /// <summary>Font alpha/transparency, 255 = opaque (i_font_alpha)</summary>
    [FieldOffset(32)]
    public byte FontAlpha;

    // Padding: 3 bytes at offset 33-35

    /// <summary>Spacing between glyphs in pixels (i_spacing)</summary>
    [FieldOffset(36)]
    public int Spacing;

    /// <summary>Outline color in 0x00RRGGBB format (i_outline_color)</summary>
    [FieldOffset(40)]
    public uint OutlineColor;

    /// <summary>Outline alpha/transparency (i_outline_alpha)</summary>
    [FieldOffset(44)]
    public byte OutlineAlpha;

    // Padding: 3 bytes at offset 45-47

    /// <summary>Outline width in pixels (i_outline_width)</summary>
    [FieldOffset(48)]
    public int OutlineWidth;

    /// <summary>Shadow color in 0x00RRGGBB format (i_shadow_color)</summary>
    [FieldOffset(52)]
    public uint ShadowColor;

    /// <summary>Shadow alpha/transparency (i_shadow_alpha)</summary>
    [FieldOffset(56)]
    public byte ShadowAlpha;

    // Padding: 3 bytes at offset 57-59

    /// <summary>Shadow width/offset in pixels (i_shadow_width)</summary>
    [FieldOffset(60)]
    public int ShadowWidth;

    /// <summary>Background color in 0x00RRGGBB format (i_background_color)</summary>
    [FieldOffset(64)]
    public uint BackgroundColor;

    /// <summary>Background alpha/transparency (i_background_alpha)</summary>
    [FieldOffset(68)]
    public byte BackgroundAlpha;

    // Padding: 3 bytes at offset 69-71

    /// <summary>Line wrapping mode (e_wrapinfo)</summary>
    [FieldOffset(72)]
    public VLCTextWrapMode WrapMode;

    // Total size: 76 bytes used, padded to 80 for 8-byte alignment
}

/// <summary>
/// Text wrapping mode enumeration
/// </summary>
public enum VLCTextWrapMode
{
    /// <summary>Breaks on whitespace or fallback on character</summary>
    Default = 0,
    /// <summary>Breaks at character level only</summary>
    Character = 1,
    /// <summary>No line breaks except explicit ones</summary>
    None = 2
}

/// <summary>
/// Feature flags indicating which style fields have been explicitly set.
/// Used for merging styles - only set fields are merged.
/// </summary>
public static class VLCTextStyleFeatures
{
    /// <summary>No features set, all defaults</summary>
    public const ushort NoDefaults = 0x0;

    /// <summary>All features set</summary>
    public const ushort FullySet = 0xFFFF;

    /// <summary>Font color is set</summary>
    public const ushort HasFontColor = 1 << 0;

    /// <summary>Font alpha is set</summary>
    public const ushort HasFontAlpha = 1 << 1;

    /// <summary>Style flags (bold, italic, etc.) are set</summary>
    public const ushort HasFlags = 1 << 2;

    /// <summary>Outline color is set</summary>
    public const ushort HasOutlineColor = 1 << 3;

    /// <summary>Outline alpha is set</summary>
    public const ushort HasOutlineAlpha = 1 << 4;

    /// <summary>Shadow color is set</summary>
    public const ushort HasShadowColor = 1 << 5;

    /// <summary>Shadow alpha is set</summary>
    public const ushort HasShadowAlpha = 1 << 6;

    /// <summary>Background color is set</summary>
    public const ushort HasBackgroundColor = 1 << 7;

    /// <summary>Background alpha is set</summary>
    public const ushort HasBackgroundAlpha = 1 << 8;

    /// <summary>Wrap info is set</summary>
    public const ushort HasWrapInfo = 1 << 9;
}

/// <summary>
/// Style flags for text formatting (bold, italic, outline, etc.)
/// </summary>
public static class VLCTextStyleFlags
{
    /// <summary>Bold text</summary>
    public const ushort Bold = 1 << 0;

    /// <summary>Italic text</summary>
    public const ushort Italic = 1 << 1;

    /// <summary>Draw outline around text</summary>
    public const ushort Outline = 1 << 2;

    /// <summary>Draw drop shadow</summary>
    public const ushort Shadow = 1 << 3;

    /// <summary>Draw background box</summary>
    public const ushort Background = 1 << 4;

    /// <summary>Underlined text</summary>
    public const ushort Underline = 1 << 5;

    /// <summary>Strikethrough text</summary>
    public const ushort Strikeout = 1 << 6;

    /// <summary>Half-width characters</summary>
    public const ushort HalfWidth = 1 << 7;

    /// <summary>Use monospaced font</summary>
    public const ushort Monospaced = 1 << 8;

    /// <summary>Double-width characters</summary>
    public const ushort DoubleWidth = 1 << 9;

    /// <summary>Blinking foreground</summary>
    public const ushort BlinkForeground = 1 << 10;

    /// <summary>Blinking background</summary>
    public const ushort BlinkBackground = 1 << 11;
}

/// <summary>
/// Alpha/transparency constants for text styles
/// </summary>
public static class VLCTextStyleAlpha
{
    /// <summary>Fully opaque (255)</summary>
    public const byte Opaque = 0xFF;

    /// <summary>Fully transparent (0)</summary>
    public const byte Transparent = 0x00;
}

/// <summary>
/// Default values for text styles
/// </summary>
public static class VLCTextStyleDefaults
{
    /// <summary>Default font size in pixels</summary>
    public const int FontSize = 20;

    /// <summary>Default relative font size (percentage of video height)</summary>
    public const float RelativeFontSize = 6.25f;
}
