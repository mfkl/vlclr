// VLC text style structure
// Source: vlc-3.0.24-beta1/sdk/include/vlc/plugins/vlc_text_style.h
// VLC Version: 3.0.24-beta1

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Text style structure (text_style_t from vlc_text_style.h).
/// Size on 64-bit: 88 bytes.
///
/// VLC 3 has karaoke background color/alpha fields not present in VLC 4.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 88)]
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

    [FieldOffset(33)] private byte _pad0;
    [FieldOffset(34)] private ushort _pad1;

    /// <summary>Spacing between glyphs in pixels (i_spacing)</summary>
    [FieldOffset(36)]
    public int Spacing;

    /// <summary>Outline color in 0x00RRGGBB format (i_outline_color)</summary>
    [FieldOffset(40)]
    public uint OutlineColor;

    /// <summary>Outline alpha/transparency (i_outline_alpha)</summary>
    [FieldOffset(44)]
    public byte OutlineAlpha;

    [FieldOffset(45)] private byte _pad2;
    [FieldOffset(46)] private ushort _pad3;

    /// <summary>Outline width in pixels (i_outline_width)</summary>
    [FieldOffset(48)]
    public int OutlineWidth;

    /// <summary>Shadow color in 0x00RRGGBB format (i_shadow_color)</summary>
    [FieldOffset(52)]
    public uint ShadowColor;

    /// <summary>Shadow alpha/transparency (i_shadow_alpha)</summary>
    [FieldOffset(56)]
    public byte ShadowAlpha;

    [FieldOffset(57)] private byte _pad4;
    [FieldOffset(58)] private ushort _pad5;

    /// <summary>Shadow width/offset in pixels (i_shadow_width)</summary>
    [FieldOffset(60)]
    public int ShadowWidth;

    /// <summary>Background color in 0x00RRGGBB format (i_background_color)</summary>
    [FieldOffset(64)]
    public uint BackgroundColor;

    /// <summary>Background alpha/transparency (i_background_alpha)</summary>
    [FieldOffset(68)]
    public byte BackgroundAlpha;

    [FieldOffset(69)] private byte _pad6;
    [FieldOffset(70)] private ushort _pad7;

    /// <summary>Karaoke background color in 0x00RRGGBB format (i_karaoke_background_color)</summary>
    [FieldOffset(72)]
    public uint KaraokeBackgroundColor;

    /// <summary>Karaoke background alpha/transparency (i_karaoke_background_alpha)</summary>
    [FieldOffset(76)]
    public byte KaraokeBackgroundAlpha;

    [FieldOffset(77)] private byte _pad8;
    [FieldOffset(78)] private ushort _pad9;

    /// <summary>Line wrapping mode (e_wrapinfo)</summary>
    [FieldOffset(80)]
    public VLCTextWrapMode WrapMode;
}

public enum VLCTextWrapMode
{
    Default = 0,
    Character = 1,
    None = 2
}

public static class VLCTextStyleFeatures
{
    public const ushort NoDefaults = 0x0;
    public const ushort FullySet = 0xFFFF;
    public const ushort HasFontColor = 1 << 0;
    public const ushort HasFontAlpha = 1 << 1;
    public const ushort HasFlags = 1 << 2;
    public const ushort HasOutlineColor = 1 << 3;
    public const ushort HasOutlineAlpha = 1 << 4;
    public const ushort HasShadowColor = 1 << 5;
    public const ushort HasShadowAlpha = 1 << 6;
    public const ushort HasBackgroundColor = 1 << 7;
    public const ushort HasBackgroundAlpha = 1 << 8;
    public const ushort HasKaraokeBackgroundColor = 1 << 9;
    public const ushort HasKaraokeBackgroundAlpha = 1 << 10;
    public const ushort HasWrapInfo = 1 << 11;
}

public static class VLCTextStyleFlags
{
    public const ushort Bold = 1 << 0;
    public const ushort Italic = 1 << 1;
    public const ushort Outline = 1 << 2;
    public const ushort Shadow = 1 << 3;
    public const ushort Background = 1 << 4;
    public const ushort Underline = 1 << 5;
    public const ushort Strikeout = 1 << 6;
    public const ushort HalfWidth = 1 << 7;
    public const ushort Monospaced = 1 << 8;
    public const ushort DoubleWidth = 1 << 9;
    public const ushort BlinkForeground = 1 << 10;
    public const ushort BlinkBackground = 1 << 11;
}

public static class VLCTextStyleAlpha
{
    public const byte Opaque = 0xFF;
    public const byte Transparent = 0x00;
}

public static class VLCTextStyleDefaults
{
    public const int FontSize = 20;
    public const float RelativeFontSize = 6.25f;
}
