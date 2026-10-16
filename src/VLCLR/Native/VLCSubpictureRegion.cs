// VLC subpicture region structure
// Source: vlc/include/vlc_subpicture.h
// VLC Version: 4.0.6

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Subpicture region structure (subpicture_region_t from vlc_subpicture.h).
/// A region represents a subtitle or overlay positioned on the video.
/// Subtitles contain a list of regions.
/// </summary>
/// <remarks>
/// Layout on 64-bit, Windows offsets (8 less on Linux and macOS for every
/// member after fmt, which are ref properties placed for the running ABI):
/// - fmt (video_format_t): 152 bytes at offset 0 (144 on Linux and macOS)
/// - p_picture: 8 bytes at offset 152
/// - b_absolute + b_in_window: 2 bytes at offset 160, then 2 bytes padding
/// - i_x, i_y, i_align, i_alpha: 4 ints = 16 bytes at offset 164
/// - p_text: 8 bytes at offset 180 (needs padding to 8-byte alignment, so offset 184)
/// - text_flags, i_max_width, i_max_height: 3 ints = 12 bytes
/// - node (vlc_list): 16 bytes (two pointers)
/// Total: 224 bytes (216 on Linux and macOS); the managed struct uses 224.
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 224)]
public struct VLCSubpictureRegion
{
    /// <summary>Format of the picture (fmt)</summary>
    [FieldOffset(0)]
    public VLCVideoFormat Format;

    private static int FormatEnd => VLCAbi.VideoFormatSize;

    /// <summary>Picture comprising this region (p_picture)</summary>
    [UnscopedRef]
    public ref nint Picture => ref VLCAbi.Field<VLCSubpictureRegion, nint>(ref this, FormatEnd);

    /// <summary>Position is absolute in the movie (b_absolute)</summary>
    [UnscopedRef]
    public ref byte IsAbsolute => ref VLCAbi.Field<VLCSubpictureRegion, byte>(ref this, FormatEnd + 8);

    /// <summary>Position the region in window (b_in_window)</summary>
    [UnscopedRef]
    public ref byte IsInWindow => ref VLCAbi.Field<VLCSubpictureRegion, byte>(ref this, FormatEnd + 9);

    // 2 bytes padding at FormatEnd + 10

    /// <summary>X position relative to alignment (i_x)</summary>
    [UnscopedRef]
    public ref int X => ref VLCAbi.Field<VLCSubpictureRegion, int>(ref this, FormatEnd + 12);

    /// <summary>Y position relative to alignment (i_y)</summary>
    [UnscopedRef]
    public ref int Y => ref VLCAbi.Field<VLCSubpictureRegion, int>(ref this, FormatEnd + 16);

    /// <summary>Alignment flags SUBPICTURE_ALIGN_xxx (i_align)</summary>
    [UnscopedRef]
    public ref int Align => ref VLCAbi.Field<VLCSubpictureRegion, int>(ref this, FormatEnd + 20);

    /// <summary>Transparency/alpha value (i_alpha)</summary>
    [UnscopedRef]
    public ref int Alpha => ref VLCAbi.Field<VLCSubpictureRegion, int>(ref this, FormatEnd + 24);

    // 4 bytes padding at FormatEnd + 28 for 8-byte alignment of p_text

    /// <summary>Subtitle text segments (p_text)</summary>
    [UnscopedRef]
    public ref nint Text => ref VLCAbi.Field<VLCSubpictureRegion, nint>(ref this, FormatEnd + 32);

    /// <summary>Text flags VLC_SUBPIC_TEXT_FLAG_xxx (text_flags)</summary>
    [UnscopedRef]
    public ref int TextFlags => ref VLCAbi.Field<VLCSubpictureRegion, int>(ref this, FormatEnd + 40);

    /// <summary>Horizontal rendering/cropping target (i_max_width)</summary>
    [UnscopedRef]
    public ref int MaxWidth => ref VLCAbi.Field<VLCSubpictureRegion, int>(ref this, FormatEnd + 44);

    /// <summary>Vertical rendering/cropping target (i_max_height)</summary>
    [UnscopedRef]
    public ref int MaxHeight => ref VLCAbi.Field<VLCSubpictureRegion, int>(ref this, FormatEnd + 48);

    // 4 bytes padding at FormatEnd + 52 for 8-byte alignment of node

    /// <summary>List node prev pointer (node.prev)</summary>
    [UnscopedRef]
    public ref nint NodePrev => ref VLCAbi.Field<VLCSubpictureRegion, nint>(ref this, FormatEnd + 56);

    /// <summary>List node next pointer (node.next)</summary>
    [UnscopedRef]
    public ref nint NodeNext => ref VLCAbi.Field<VLCSubpictureRegion, nint>(ref this, FormatEnd + 64);
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

    /// <summary>
    /// Converts VLC alignment flags to horizontal TextAlignment enum.
    /// </summary>
    /// <param name="vlcAlign">VLC alignment bitmask.</param>
    /// <returns>Corresponding TextAlignment value.</returns>
    public static Rendering.TextAlignment ToTextAlignment(int vlcAlign)
    {
        if ((vlcAlign & Left) != 0)
            return Rendering.TextAlignment.Left;
        if ((vlcAlign & Right) != 0)
            return Rendering.TextAlignment.Right;
        return Rendering.TextAlignment.Center;
    }

    /// <summary>
    /// Converts VLC alignment flags to vertical TextVerticalPosition enum.
    /// </summary>
    /// <param name="vlcAlign">VLC alignment bitmask.</param>
    /// <returns>Corresponding TextVerticalPosition value.</returns>
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
    /// <summary>Render background under text only, not whole region</summary>
    public const int NoRegionBackground = 1 << 4;

    /// <summary>Decoder sends row/column based output</summary>
    public const int GridMode = 1 << 5;

    /// <summary>Don't try to balance wrapped text lines</summary>
    public const int TextNotBalanced = 1 << 6;

    /// <summary>Mark the region as containing text</summary>
    public const int IsText = 1 << 7;
}
