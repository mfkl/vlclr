// VLC filter structure
// Source: vlc/include/vlc_filter.h
// VLC Version: 4.0.6

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Filter structure (filter_t from vlc_filter.h)
/// IMPORTANT: The first 3 members must match decoder_t layout
/// es_format_t is 240 bytes on Windows and 232 on Linux and macOS (see
/// <see cref="VLCAbi"/>), so every member after fmt_in is a ref property
/// placed for the running ABI: ops is at 560 on Windows, 544 elsewhere.
/// The managed struct is sized for the larger, Windows layout.
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

    /// <summary>Input format (fmt_in)</summary>
    [FieldOffset(40)]
    public VLCEsFormat FormatIn;

    private static int VideoContextInOffset => 40 + VLCAbi.EsFormatSize;
    private static int FormatOutEndOffset => 48 + 2 * VLCAbi.EsFormatSize;

    /// <summary>Video context input (vctx_in)</summary>
    [UnscopedRef]
    public ref nint VideoContextIn => ref VLCAbi.Field<VLCFilter, nint>(ref this, VideoContextInOffset);

    /// <summary>Output format (fmt_out)</summary>
    [UnscopedRef]
    public ref VLCEsFormat FormatOut => ref VLCAbi.Field<VLCFilter, VLCEsFormat>(ref this, VideoContextInOffset + 8);

    /// <summary>Video context output (vctx_out)</summary>
    [UnscopedRef]
    public ref nint VideoContextOut => ref VLCAbi.Field<VLCFilter, nint>(ref this, FormatOutEndOffset);

    /// <summary>Allow format out change flag (b_allow_fmt_out_change)</summary>
    [UnscopedRef]
    public ref byte AllowFormatOutChange => ref VLCAbi.Field<VLCFilter, byte>(ref this, FormatOutEndOffset + 8);

    /// <summary>Requested filter shortcut name (psz_name)</summary>
    [UnscopedRef]
    public ref nint Name => ref VLCAbi.Field<VLCFilter, nint>(ref this, FormatOutEndOffset + 16);

    /// <summary>Filter configuration chain (p_cfg)</summary>
    [UnscopedRef]
    public ref nint Config => ref VLCAbi.Field<VLCFilter, nint>(ref this, FormatOutEndOffset + 24);

    /// <summary>Filter operations pointer (ops) - MUST be set by Open callback</summary>
    [UnscopedRef]
    public ref nint Operations => ref VLCAbi.Field<VLCFilter, nint>(ref this, FormatOutEndOffset + 32);

    /// <summary>Filter owner (owner) - 24 bytes</summary>
    [UnscopedRef]
    public ref VLCFilterOwner Owner => ref VLCAbi.Field<VLCFilter, VLCFilterOwner>(ref this, FormatOutEndOffset + 40);
}
