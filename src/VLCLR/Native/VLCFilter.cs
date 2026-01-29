// VLC filter structure
// Source: vlc/include/vlc_filter.h
// VLC Version: 4.0.6

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Filter structure (filter_t from vlc_filter.h)
/// IMPORTANT: The first 3 members must match decoder_t layout
/// Uses explicit layout based on memory analysis with VLC 4.x:
/// - es_format_t = 240 bytes (56 + 152 video_format_t + 32)
/// - ops at offset 560
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 592)]
public struct VLCFilter
{
    /// <summary>VLC object header (struct vlc_object_t obj) - 24 bytes</summary>
    [FieldOffset(0)]
    public VLCObjectHeader Obj;

    /// <summary>Module pointer (p_module)</summary>
    [FieldOffset(24)]
    public nint Module;

    /// <summary>Private system data (p_sys) - filter implementation stores its state here</summary>
    [FieldOffset(32)]
    public nint Sys;

    /// <summary>Input format (fmt_in) - 240 bytes</summary>
    [FieldOffset(40)]
    public VLCEsFormat FormatIn;

    /// <summary>Video context input (vctx_in)</summary>
    [FieldOffset(280)]
    public nint VideoContextIn;

    /// <summary>Output format (fmt_out) - 240 bytes</summary>
    [FieldOffset(288)]
    public VLCEsFormat FormatOut;

    /// <summary>Video context output (vctx_out)</summary>
    [FieldOffset(528)]
    public nint VideoContextOut;

    /// <summary>Allow format out change flag (b_allow_fmt_out_change)</summary>
    [FieldOffset(536)]
    public byte AllowFormatOutChange;

    /// <summary>Requested filter shortcut name (psz_name)</summary>
    [FieldOffset(544)]
    public nint Name;

    /// <summary>Filter configuration chain (p_cfg)</summary>
    [FieldOffset(552)]
    public nint Config;

    /// <summary>Filter operations pointer (ops) - MUST be set by Open callback</summary>
    [FieldOffset(560)]
    public nint Operations;

    /// <summary>Filter owner (owner) - 24 bytes</summary>
    [FieldOffset(568)]
    public VLCFilterOwner Owner;
}
