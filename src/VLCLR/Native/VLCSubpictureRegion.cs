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
/// Layout and size come from <see cref="Interop.SubpictureRegion"/>. Members after fmt sit at
/// ABI-dependent offsets (they differ between Windows and Linux/macOS), so they are ref properties.
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = Interop.SubpictureRegion.MaxSizeOf)]
public struct VLCSubpictureRegion
{
    /// <summary>Format of the picture (fmt)</summary>
    [FieldOffset(Interop.SubpictureRegion.Offsets.Fmt)]
    public VLCVideoFormat Format;

    /// <summary>Picture comprising this region (p_picture)</summary>
    [UnscopedRef]
    public ref nint Picture => ref Interop.CImportHelpers.FieldAt<VLCSubpictureRegion, nint>(ref this, Interop.SubpictureRegion.Offsets.Picture);

    /// <summary>Position is absolute in the movie (b_absolute)</summary>
    [UnscopedRef]
    public ref byte IsAbsolute => ref Interop.CImportHelpers.FieldAt<VLCSubpictureRegion, byte>(ref this, Interop.SubpictureRegion.Offsets.Absolute);

    /// <summary>Position the region in window (b_in_window)</summary>
    [UnscopedRef]
    public ref byte IsInWindow => ref Interop.CImportHelpers.FieldAt<VLCSubpictureRegion, byte>(ref this, Interop.SubpictureRegion.Offsets.InWindow);


    /// <summary>X position relative to alignment (i_x)</summary>
    [UnscopedRef]
    public ref int X => ref Interop.CImportHelpers.FieldAt<VLCSubpictureRegion, int>(ref this, Interop.SubpictureRegion.Offsets.X);

    /// <summary>Y position relative to alignment (i_y)</summary>
    [UnscopedRef]
    public ref int Y => ref Interop.CImportHelpers.FieldAt<VLCSubpictureRegion, int>(ref this, Interop.SubpictureRegion.Offsets.Y);

    /// <summary>Alignment flags SUBPICTURE_ALIGN_xxx (i_align)</summary>
    [UnscopedRef]
    public ref int Align => ref Interop.CImportHelpers.FieldAt<VLCSubpictureRegion, int>(ref this, Interop.SubpictureRegion.Offsets.Align);

    /// <summary>Transparency/alpha value (i_alpha)</summary>
    [UnscopedRef]
    public ref int Alpha => ref Interop.CImportHelpers.FieldAt<VLCSubpictureRegion, int>(ref this, Interop.SubpictureRegion.Offsets.Alpha);


    /// <summary>Subtitle text segments (p_text)</summary>
    [UnscopedRef]
    public ref nint Text => ref Interop.CImportHelpers.FieldAt<VLCSubpictureRegion, nint>(ref this, Interop.SubpictureRegion.Offsets.Text);

    /// <summary>Text flags VLC_SUBPIC_TEXT_FLAG_xxx (text_flags)</summary>
    [UnscopedRef]
    public ref int TextFlags => ref Interop.CImportHelpers.FieldAt<VLCSubpictureRegion, int>(ref this, Interop.SubpictureRegion.Offsets.TextFlags);

    /// <summary>Horizontal rendering/cropping target (i_max_width)</summary>
    [UnscopedRef]
    public ref int MaxWidth => ref Interop.CImportHelpers.FieldAt<VLCSubpictureRegion, int>(ref this, Interop.SubpictureRegion.Offsets.MaxWidth);

    /// <summary>Vertical rendering/cropping target (i_max_height)</summary>
    [UnscopedRef]
    public ref int MaxHeight => ref Interop.CImportHelpers.FieldAt<VLCSubpictureRegion, int>(ref this, Interop.SubpictureRegion.Offsets.MaxHeight);


    /// <summary>List node prev pointer (node.prev)</summary>
    [UnscopedRef]
    public ref nint NodePrev => ref Interop.CImportHelpers.FieldAt<VLCSubpictureRegion, nint>(ref this, Interop.SubpictureRegion.Offsets.Node + Interop.List.Offsets.Prev);

    /// <summary>List node next pointer (node.next)</summary>
    [UnscopedRef]
    public ref nint NodeNext => ref Interop.CImportHelpers.FieldAt<VLCSubpictureRegion, nint>(ref this, Interop.SubpictureRegion.Offsets.Node + Interop.List.Offsets.Next);
}

/// <summary>
/// VLC list node structure (vlc_list from vlc_list.h).
/// A doubly linked list node with prev/next pointers.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Interop.List.MaxSizeOf)]
public struct VLCListNode
{
    /// <summary>Previous node pointer</summary>
    [FieldOffset(Interop.List.Offsets.Prev)]
    public nint Prev;

    /// <summary>Next node pointer</summary>
    [FieldOffset(Interop.List.Offsets.Next)]
    public nint Next;
}

/// <summary>
/// Subpicture region alignment flags
/// </summary>
public static class VLCSubpictureAlign
{
    /// <summary>Align to left edge</summary>
    public const int Left = Interop.VlcSubpicture.SubpictureAlignLeft;

    /// <summary>Align to right edge</summary>
    public const int Right = Interop.VlcSubpicture.SubpictureAlignRight;

    /// <summary>Align to top edge</summary>
    public const int Top = Interop.VlcSubpicture.SubpictureAlignTop;

    /// <summary>Align to bottom edge</summary>
    public const int Bottom = Interop.VlcSubpicture.SubpictureAlignBottom;

    /// <summary>Mask for all alignment flags</summary>
    public const int Mask = Interop.VlcSubpicture.SubpictureAlignMask;

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
    public const int NoRegionBackground = Interop.VlcSubpicture.SubpicTextFlagNoRegionBg;

    /// <summary>Decoder sends row/column based output</summary>
    public const int GridMode = Interop.VlcSubpicture.SubpicTextFlagGridMode;

    /// <summary>Don't try to balance wrapped text lines</summary>
    public const int TextNotBalanced = Interop.VlcSubpicture.SubpicTextFlagTextNotBalanced;

    /// <summary>Mark the region as containing text</summary>
    public const int IsText = Interop.VlcSubpicture.SubpicTextFlagIsText;
}
