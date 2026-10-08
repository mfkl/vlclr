// VLC filter structure
// Source: vlc-3.0.24-beta1/sdk/include/vlc/plugins/vlc_filter.h
// VLC Version: 3.0.24-beta1
//
// VLC 3 has NO vlc_filter_operations; callbacks are inline in filter_t.

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Filter structure (filter_t from vlc_filter.h).
/// Size on 64-bit: 672 bytes.
///
/// VLC 3 uses inline callback unions instead of an operations pointer.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 672)]
public struct VLCFilter
{
    /// <summary>VLC object header (struct vlc_object_t obj) - 40 bytes</summary>
    [FieldOffset(0)]
    public VLCObjectHeader Obj;

    /// <summary>Module pointer (p_module)</summary>
    [FieldOffset(40)]
    public nint Module;

    /// <summary>Private system data (p_sys)</summary>
    [FieldOffset(48)]
    public nint Sys;

    /// <summary>Input format (fmt_in)</summary>
    [FieldOffset(56)]
    public VLCEsFormat FormatIn;

    /// <summary>Output format (fmt_out) - after fmt_in (56 + 264 = 320)</summary>
    [FieldOffset(320)]
    public VLCEsFormat FormatOut;

    /// <summary>Allow format out change flag (b_allow_fmt_out_change)</summary>
    [FieldOffset(584)]
    public byte AllowFormatOutChange;

    /// <summary>Requested filter shortcut name (psz_name)</summary>
    [FieldOffset(592)]
    public nint Name;

    /// <summary>Filter configuration chain (p_cfg)</summary>
    [FieldOffset(600)]
    public nint Config;

    /// <summary>
    /// Video filter callback union (pf_video_filter).
    /// Signature: picture_t* (*pf_video_filter)(filter_t*, picture_t*)
    /// </summary>
    [FieldOffset(608)]
    public nint VideoFilter;

    /// <summary>
    /// Audio drain callback union (pf_audio_drain).
    /// Overlaps with video filter union for different filter types.
    /// </summary>
    [FieldOffset(616)]
    public nint AudioDrain;

    /// <summary>
    /// Flush callback (pf_flush).
    /// Signature: void (*pf_flush)(filter_t*)
    /// </summary>
    [FieldOffset(624)]
    public nint Flush;

    /// <summary>
    /// Change viewpoint callback (pf_change_viewpoint).
    /// Signature: void (*pf_change_viewpoint)(filter_t*, const vlc_viewpoint_t*)
    /// </summary>
    [FieldOffset(632)]
    public nint ChangeViewpoint;

    /// <summary>
    /// Video mouse callback union (pf_video_mouse).
    /// Signature: int (*pf_video_mouse)(filter_t*, vlc_mouse_t*, const vlc_mouse_t*, const vlc_mouse_t*)
    /// </summary>
    [FieldOffset(640)]
    public nint VideoMouse;

    /// <summary>
    /// Get attachments callback (pf_get_attachments).
    /// Signature: int (*pf_get_attachments)(filter_t*, input_attachment_t***, int*)
    /// </summary>
    [FieldOffset(648)]
    public nint GetAttachments;

    /// <summary>Filter owner (owner) - 16 bytes</summary>
    [FieldOffset(656)]
    public VLCFilterOwner Owner;
}
