// VLC subpicture region structure
// Source: vlc-3.0.24-beta1/sdk/include/vlc/plugins/vlc_subpicture.h
// VLC Version: 3.0.24-beta1

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Subpicture region structure (subpicture_region_t from vlc_subpicture.h).
/// Size on 64-bit: 240 bytes.
///
/// VLC 3 has: fmt, p_picture, i_x, i_y, i_align, i_alpha, p_text,
/// i_text_align, b_noregionbg, b_gridmode, b_balanced_text,
/// i_max_width, i_max_height, p_next, p_private.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 240)]
public struct VLCSubpictureRegion
{
    /// <summary>Format of the picture (fmt)</summary>
    [FieldOffset(0)]
    public VLCVideoFormat Format;

    /// <summary>Picture comprising this region (p_picture)</summary>
    [FieldOffset(176)]
    public nint Picture;

    /// <summary>X position relative to alignment (i_x)</summary>
    [FieldOffset(184)]
    public int X;

    /// <summary>Y position relative to alignment (i_y)</summary>
    [FieldOffset(188)]
    public int Y;

    /// <summary>Alignment flags SUBPICTURE_ALIGN_xxx (i_align)</summary>
    [FieldOffset(192)]
    public int Align;

    /// <summary>Transparency/alpha value (i_alpha)</summary>
    [FieldOffset(196)]
    public int Alpha;

    /// <summary>Subtitle text segments (p_text)</summary>
    [FieldOffset(200)]
    public nint Text;

    /// <summary>Text content alignment flags (i_text_align)</summary>
    [FieldOffset(208)]
    public int TextAlign;

    /// <summary>Render background under text only (b_noregionbg)</summary>
    [FieldOffset(212)]
    public byte NoRegionBg;

    /// <summary>Decoder sends row/cols based output (b_gridmode)</summary>
    [FieldOffset(213)]
    public byte GridMode;

    /// <summary>Try to balance wrapped text lines (b_balanced_text)</summary>
    [FieldOffset(214)]
    public byte BalancedText;

    [FieldOffset(215)] private byte _pad0;

    /// <summary>Horizontal rendering/cropping target (i_max_width)</summary>
    [FieldOffset(216)]
    public int MaxWidth;

    /// <summary>Vertical rendering/cropping target (i_max_height)</summary>
    [FieldOffset(220)]
    public int MaxHeight;

    /// <summary>Next region in the list (p_next)</summary>
    [FieldOffset(224)]
    public nint Next;

    /// <summary>Private data for spu_t only (p_private)</summary>
    [FieldOffset(232)]
    public nint Private;
}

/// <summary>
/// Subpicture region alignment flags
/// </summary>
public static class VLCSubpictureAlign
{
    public const int Left = 0x1;
    public const int Right = 0x2;
    public const int Top = 0x4;
    public const int Bottom = 0x8;
    public const int Mask = Left | Right | Top | Bottom;

    public static Rendering.TextAlignment ToTextAlignment(int vlcAlign)
    {
        if ((vlcAlign & Left) != 0)
            return Rendering.TextAlignment.Left;
        if ((vlcAlign & Right) != 0)
            return Rendering.TextAlignment.Right;
        return Rendering.TextAlignment.Center;
    }

    public static Rendering.TextVerticalPosition ToVerticalPosition(int vlcAlign)
    {
        if ((vlcAlign & Top) != 0)
            return Rendering.TextVerticalPosition.Top;
        if ((vlcAlign & Bottom) != 0)
            return Rendering.TextVerticalPosition.Bottom;
        return Rendering.TextVerticalPosition.Center;
    }
}

/// <summary>
/// Text region flags for subpictures
/// </summary>
public static class VLCSubpictureTextFlags
{
    public const int NoRegionBackground = 1 << 4;
    public const int GridMode = 1 << 5;
    public const int TextNotBalanced = 1 << 6;
    public const int IsText = 1 << 7;
}
