// VLC filter owner structure
// Source: vlc-3.0.24-beta1/sdk/include/vlc/plugins/vlc_filter.h
// VLC Version: 3.0.24-beta1

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Filter owner structure (filter_owner_t from vlc_filter.h).
/// Size: 16 bytes. VLC 3 has sys(8) + union{ video.buffer_new(8) }.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VLCFilterOwner
{
    /// <summary>Owner system data (sys)</summary>
    public nint Sys;

    /// <summary>
    /// Video buffer new callback.
    /// Signature: picture_t* (*buffer_new)(filter_t*)
    /// </summary>
    public nint BufferNew;
}
