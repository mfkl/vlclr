// VLC filter video callbacks structure
// Source: vlc/include/vlc_filter.h
// VLC Version: 4.0.6

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Filter video callbacks structure (filter_video_callbacks from vlc_filter.h)
/// Used in filter_owner_t
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Interop.FilterVideoCallbacks.MaxSizeOf)]
public struct VLCFilterVideoCallbacks
{
    /// <summary>
    /// Allocate new picture buffer
    /// Signature: picture_t* (*buffer_new)(filter_t*)
    /// </summary>
    [FieldOffset(Interop.FilterVideoCallbacks.Offsets.BufferNew)] public nint BufferNew;

    /// <summary>
    /// Hold decoder device
    /// Signature: vlc_decoder_device* (*hold_device)(vlc_object_t*, void* sys)
    /// </summary>
    [FieldOffset(Interop.FilterVideoCallbacks.Offsets.HoldDevice)] public nint HoldDevice;
}
