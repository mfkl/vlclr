// VLC filter owner structure
// Source: vlc/include/vlc_filter.h
// VLC Version: 4.0.6

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Filter owner structure (filter_owner_t from vlc_filter.h)
/// Contains callbacks union and attachments function
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Interop.FilterOwner.MaxSizeOf)]
public struct VLCFilterOwner
{
    /// <summary>
    /// Callbacks union - pointer to video/audio/sub callbacks
    /// For video filters, this points to filter_video_callbacks
    /// </summary>
    [FieldOffset(Interop.FilterOwner.Offsets.Video)] public nint Callbacks;

    /// <summary>
    /// Get attachments function pointer
    /// Signature: int (*pf_get_attachments)(filter_t*, input_attachment_t***, int*)
    /// </summary>
    [FieldOffset(Interop.FilterOwner.Offsets.GetAttachments)] public nint GetAttachments;

    /// <summary>
    /// Owner system data (sys)
    /// </summary>
    [FieldOffset(Interop.FilterOwner.Offsets.Sys)] public nint Sys;
}
