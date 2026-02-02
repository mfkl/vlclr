// VLC subpicture region structure
// Source: vlc/include/vlc_subpicture.h
// VLC Version: 4.0.6

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Subpicture region structure (subpicture_region_t from vlc_subpicture.h).
/// A region represents a subtitle or overlay positioned on the video.
/// Subtitles contain a list of regions.
/// </summary>
/// <remarks>
/// Layout on 64-bit:
/// - fmt (video_format_t): 152 bytes at offset 0
/// - p_picture: 8 bytes at offset 152
/// - b_absolute + b_in_window: 2 bytes at offset 160, then 2 bytes padding
/// - i_x, i_y, i_align, i_alpha: 4 ints = 16 bytes at offset 164
/// - p_text: 8 bytes at offset 180 (needs padding to 8-byte alignment, so offset 184)
/// - text_flags, i_max_width, i_max_height: 3 ints = 12 bytes
/// - node (vlc_list): 16 bytes (two pointers)
/// Total size needs verification
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 224)]
public struct VLCSubpictureRegion
{
    /// <summary>Format of the picture (fmt)</summary>
    [FieldOffset(0)]
    public VLCVideoFormat Format;

    /// <summary>Picture comprising this region (p_picture)</summary>
    [FieldOffset(152)]
    public nint Picture;

    /// <summary>Position is absolute in the movie (b_absolute)</summary>
    [FieldOffset(160)]
    public byte IsAbsolute;

    /// <summary>Position the region in window (b_in_window)</summary>
    [FieldOffset(161)]
    public byte IsInWindow;

    // 2 bytes padding at 162-163

    /// <summary>X position relative to alignment (i_x)</summary>
    [FieldOffset(164)]
    public int X;

    /// <summary>Y position relative to alignment (i_y)</summary>
    [FieldOffset(168)]
    public int Y;

    /// <summary>Alignment flags SUBPICTURE_ALIGN_xxx (i_align)</summary>
    [FieldOffset(172)]
    public int Align;

    /// <summary>Transparency/alpha value (i_alpha)</summary>
    [FieldOffset(176)]
    public int Alpha;

    // Padding to 8-byte boundary at offset 180 (4 bytes padding)

    /// <summary>Subtitle text segments (p_text)</summary>
    [FieldOffset(184)]
    public nint Text;

    /// <summary>Text flags VLC_SUBPIC_TEXT_FLAG_xxx (text_flags)</summary>
    [FieldOffset(192)]
    public int TextFlags;

    /// <summary>Horizontal rendering/cropping target (i_max_width)</summary>
    [FieldOffset(196)]
    public int MaxWidth;

    /// <summary>Vertical rendering/cropping target (i_max_height)</summary>
    [FieldOffset(200)]
    public int MaxHeight;

    // 4 bytes padding at 204-207 for 8-byte alignment of node

    /// <summary>List node prev pointer (node.prev)</summary>
    [FieldOffset(208)]
    public nint NodePrev;

    /// <summary>List node next pointer (node.next)</summary>
    [FieldOffset(216)]
    public nint NodeNext;
}

/// <summary>
/// VLC list node structure (vlc_list from vlc_list.h).
/// A doubly linked list node with prev/next pointers.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VLCListNode
{
    /// <summary>Previous node pointer</summary>
    public nint Prev;

    /// <summary>Next node pointer</summary>
    public nint Next;
}

/// <summary>
/// Subpicture region alignment flags
/// </summary>
public static class VLCSubpictureAlign
{
    /// <summary>Align to left edge</summary>
    public const int Left = 0x1;

    /// <summary>Align to right edge</summary>
    public const int Right = 0x2;

    /// <summary>Align to top edge</summary>
    public const int Top = 0x4;

    /// <summary>Align to bottom edge</summary>
    public const int Bottom = 0x8;

    /// <summary>Mask for all alignment flags</summary>
    public const int Mask = Left | Right | Top | Bottom;
}

/// <summary>
/// Text region flags for subpictures
/// </summary>
public static class VLCSubpictureTextFlags
{
    /// <summary>Render background under text only, not whole region</summary>
    public const int NoRegionBackground = 1 << 4;

    /// <summary>Decoder sends row/column based output</summary>
    public const int GridMode = 1 << 5;

    /// <summary>Don't try to balance wrapped text lines</summary>
    public const int TextNotBalanced = 1 << 6;

    /// <summary>Mark the region as containing text</summary>
    public const int IsText = 1 << 7;
}
