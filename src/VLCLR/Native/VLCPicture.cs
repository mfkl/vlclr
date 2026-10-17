// VLC picture structure
// Source: vlc/include/vlc_picture.h
// VLC Version: 4.0.6

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Video picture structure (picture_t from vlc_picture.h)
/// </summary>
/// <remarks>
/// The picture starts with a video_format_t, which is 152 bytes on Windows and
/// 144 on Linux and macOS, so every member after it is a ref property placed
/// for the running ABI from <see cref="Interop.Picture.Offsets"/>. The managed
/// struct is sized for the larger, Windows layout
/// (<see cref="Interop.Picture.MaxSizeOf"/>; <see cref="Interop.Picture.MinSizeOf"/> elsewhere).
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = Interop.Picture.MaxSizeOf)]
public unsafe struct VLCPicture
{
    /// <summary>The properties of the picture (video_frame_format_t = video_format_t)</summary>
    [FieldOffset(Interop.Picture.Offsets.Format)]
    public VLCVideoFormat Format;

    /// <summary>Array of planes (p[PICTURE_PLANE_MAX])</summary>
    [UnscopedRef]
    public ref VLCPlane Plane0 => ref Interop.CImportHelpers.FieldAt<VLCPicture, VLCPlane>(ref this, Interop.Picture.Offsets.P);
    [UnscopedRef]
    public ref VLCPlane Plane1 => ref Interop.CImportHelpers.FieldAt<VLCPicture, VLCPlane>(ref this, Interop.Picture.Offsets.P + 1 * Interop.Plane.MaxSizeOf);
    [UnscopedRef]
    public ref VLCPlane Plane2 => ref Interop.CImportHelpers.FieldAt<VLCPicture, VLCPlane>(ref this, Interop.Picture.Offsets.P + 2 * Interop.Plane.MaxSizeOf);
    [UnscopedRef]
    public ref VLCPlane Plane3 => ref Interop.CImportHelpers.FieldAt<VLCPicture, VLCPlane>(ref this, Interop.Picture.Offsets.P + 3 * Interop.Plane.MaxSizeOf);
    [UnscopedRef]
    public ref VLCPlane Plane4 => ref Interop.CImportHelpers.FieldAt<VLCPicture, VLCPlane>(ref this, Interop.Picture.Offsets.P + 4 * Interop.Plane.MaxSizeOf);

    /// <summary>Number of allocated planes (i_planes)</summary>
    [UnscopedRef]
    public ref int PlaneCount => ref Interop.CImportHelpers.FieldAt<VLCPicture, int>(ref this, Interop.Picture.Offsets.Planes);

    /// <summary>Display date (date) - vlc_tick_t is int64</summary>
    [UnscopedRef]
    public ref long Date => ref Interop.CImportHelpers.FieldAt<VLCPicture, long>(ref this, Interop.Picture.Offsets.Date);

    /// <summary>Force display (b_force)</summary>
    [UnscopedRef]
    public ref byte Force => ref Interop.CImportHelpers.FieldAt<VLCPicture, byte>(ref this, Interop.Picture.Offsets.Force);

    /// <summary>Still image (b_still)</summary>
    [UnscopedRef]
    public ref byte Still => ref Interop.CImportHelpers.FieldAt<VLCPicture, byte>(ref this, Interop.Picture.Offsets.Still);

    /// <summary>Progressive frame (b_progressive)</summary>
    [UnscopedRef]
    public ref byte Progressive => ref Interop.CImportHelpers.FieldAt<VLCPicture, byte>(ref this, Interop.Picture.Offsets.Progressive);

    /// <summary>Top field first (b_top_field_first)</summary>
    [UnscopedRef]
    public ref byte TopFieldFirst => ref Interop.CImportHelpers.FieldAt<VLCPicture, byte>(ref this, Interop.Picture.Offsets.TopFieldFirst);

    /// <summary>Left eye in multiview (b_multiview_left_eye)</summary>
    [UnscopedRef]
    public ref byte MultiviewLeftEye => ref Interop.CImportHelpers.FieldAt<VLCPicture, byte>(ref this, Interop.Picture.Offsets.MultiviewLeftEye);

    /// <summary>Number of displayed fields (i_nb_fields)</summary>
    [UnscopedRef]
    public ref uint FieldCount => ref Interop.CImportHelpers.FieldAt<VLCPicture, uint>(ref this, Interop.Picture.Offsets.NbFields);

    /// <summary>Picture context pointer (context)</summary>
    [UnscopedRef]
    public ref nint Context => ref Interop.CImportHelpers.FieldAt<VLCPicture, nint>(ref this, Interop.Picture.Offsets.Context);

    /// <summary>Private system data (p_sys)</summary>
    [UnscopedRef]
    public ref nint Sys => ref Interop.CImportHelpers.FieldAt<VLCPicture, nint>(ref this, Interop.Picture.Offsets.Sys);

    /// <summary>Next picture in FIFO (p_next)</summary>
    [UnscopedRef]
    public ref nint Next => ref Interop.CImportHelpers.FieldAt<VLCPicture, nint>(ref this, Interop.Picture.Offsets.Next);

    /// <summary>Reference count (refs). The C vlc_atomic_rc_t holds an 8-byte atomic_uintptr_t; this reads its low 32 bits, which is correct on little-endian targets</summary>
    [UnscopedRef]
    public ref uint Refs => ref Interop.CImportHelpers.FieldAt<VLCPicture, uint>(ref this, Interop.Picture.Offsets.Refs);

    /// <summary>
    /// Get a plane by index (safe accessor)
    /// </summary>
    public VLCPlane GetPlane(int index)
    {
        return index switch
        {
            0 => Plane0,
            1 => Plane1,
            2 => Plane2,
            3 => Plane3,
            4 => Plane4,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }
}
