// VLC filter structure
// Source: vlc/include/vlc_filter.h
// VLC Version: 4.0.6

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Filter structure (filter_t from vlc_filter.h)
/// IMPORTANT: The first 3 members must match decoder_t layout
/// </summary>
/// <remarks>
/// es_format_t is 240 bytes on Windows and 232 on Linux and macOS, so every
/// member after fmt_in is a ref property placed for the running ABI from
/// <see cref="Interop.Filter.Offsets"/>. The managed struct is sized for the
/// larger, Windows layout (<see cref="Interop.Filter.MaxSizeOf"/>;
/// <see cref="Interop.Filter.MinSizeOf"/> elsewhere), so don't copy a whole
/// VLCFilter out of or into VLC memory: use a pointer to it.
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = Interop.Filter.MaxSizeOf)]
public unsafe struct VLCFilter
{
    /// <summary>VLC object header (struct vlc_object_t obj) - 24 bytes</summary>
    [FieldOffset(Interop.Filter.Offsets.Obj)]
    public VLCObjectHeader Obj;

    /// <summary>Module pointer (p_module)</summary>
    [FieldOffset(Interop.Filter.Offsets.Module)]
    public nint Module;

    /// <summary>Private system data (p_sys) - filter implementation stores its state here</summary>
    [FieldOffset(Interop.Filter.Offsets.Sys)]
    public nint Sys;

    /// <summary>Input format (fmt_in)</summary>
    /// <remarks>
    /// A pointer because VLCEsFormat is sized for Windows (240 bytes) and
    /// es_format_t is 232 bytes on Linux and macOS: assigning a whole
    /// VLCEsFormat would write past fmt_in there. Write its members instead.
    /// </remarks>
    public VLCEsFormat* FormatIn => (VLCEsFormat*)((byte*)Unsafe.AsPointer(ref this) + Interop.Filter.Offsets.FmtIn);

    /// <summary>Video context input (vctx_in)</summary>
    [UnscopedRef]
    public ref nint VideoContextIn => ref Interop.CImportHelpers.FieldAt<VLCFilter, nint>(ref this, Interop.Filter.Offsets.VctxIn);

    /// <summary>Output format (fmt_out)</summary>
    /// <remarks>
    /// A pointer because VLCEsFormat is sized for Windows (240 bytes) and
    /// es_format_t is 232 bytes on Linux and macOS: assigning a whole
    /// VLCEsFormat would write past fmt_out, into vctx_out, there. Write its
    /// members instead.
    /// </remarks>
    public VLCEsFormat* FormatOut => (VLCEsFormat*)((byte*)Unsafe.AsPointer(ref this) + Interop.Filter.Offsets.FmtOut);

    /// <summary>Video context output (vctx_out)</summary>
    [UnscopedRef]
    public ref nint VideoContextOut => ref Interop.CImportHelpers.FieldAt<VLCFilter, nint>(ref this, Interop.Filter.Offsets.VctxOut);

    /// <summary>Allow format out change flag (b_allow_fmt_out_change)</summary>
    [UnscopedRef]
    public ref byte AllowFormatOutChange => ref Interop.CImportHelpers.FieldAt<VLCFilter, byte>(ref this, Interop.Filter.Offsets.AllowFmtOutChange);

    /// <summary>Requested filter shortcut name (psz_name)</summary>
    [UnscopedRef]
    public ref nint Name => ref Interop.CImportHelpers.FieldAt<VLCFilter, nint>(ref this, Interop.Filter.Offsets.Name);

    /// <summary>Filter configuration chain (p_cfg)</summary>
    [UnscopedRef]
    public ref nint Config => ref Interop.CImportHelpers.FieldAt<VLCFilter, nint>(ref this, Interop.Filter.Offsets.Cfg);

    /// <summary>Filter operations pointer (ops) - MUST be set by Open callback</summary>
    [UnscopedRef]
    public ref nint Operations => ref Interop.CImportHelpers.FieldAt<VLCFilter, nint>(ref this, Interop.Filter.Offsets.Ops);

    /// <summary>Filter owner (owner) - 24 bytes</summary>
    [UnscopedRef]
    public ref VLCFilterOwner Owner => ref Interop.CImportHelpers.FieldAt<VLCFilter, VLCFilterOwner>(ref this, Interop.Filter.Offsets.Owner);
}
