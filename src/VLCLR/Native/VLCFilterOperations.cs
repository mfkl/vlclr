// VLC filter operations structure
// Source: vlc/include/vlc_filter.h
// VLC Version: 4.0.6

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Filter operations structure (vlc_filter_operations from vlc_filter.h)
/// Video filter uses filter_video callback
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VLCFilterOperations
{
    /// <summary>
    /// Filter a picture (filter_video) - first member of union
    /// Signature: picture_t* (*filter_video)(filter_t*, picture_t*)
    /// </summary>
    public nint FilterVideo;

    /// <summary>
    /// Drain callback (union, overlaps with drain_audio for audio filters)
    /// </summary>
    public nint Drain;

    /// <summary>
    /// Flush callback
    /// Signature: void (*flush)(filter_t*)
    /// </summary>
    public nint Flush;

    /// <summary>
    /// Change viewpoint callback
    /// Signature: void (*change_viewpoint)(filter_t*, const vlc_viewpoint_t*)
    /// </summary>
    public nint ChangeViewpoint;

    /// <summary>
    /// Video mouse callback
    /// Signature: int (*video_mouse)(filter_t*, vlc_mouse_t*, const vlc_mouse_t*)
    /// </summary>
    public nint VideoMouse;

    /// <summary>
    /// Close callback - release filter resources
    /// Signature: void (*close)(filter_t*)
    /// </summary>
    public nint Close;
}
