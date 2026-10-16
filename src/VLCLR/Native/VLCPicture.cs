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
/// 144 on Linux and macOS (see <see cref="VLCAbi"/>), so every member after
/// it is a ref property placed for the running ABI. The managed struct is
/// sized for the larger, Windows layout (376 bytes; 368 elsewhere).
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 376)]
public unsafe struct VLCPicture
{
    /// <summary>The properties of the picture (video_frame_format_t = video_format_t)</summary>
    [FieldOffset(0)]
    public VLCVideoFormat Format;

    private static int PlanesOffset => VLCAbi.VideoFormatSize;

    /// <summary>Array of planes (p[PICTURE_PLANE_MAX])</summary>
    [UnscopedRef]
    public ref VLCPlane Plane0 => ref VLCAbi.Field<VLCPicture, VLCPlane>(ref this, PlanesOffset);
    [UnscopedRef]
    public ref VLCPlane Plane1 => ref VLCAbi.Field<VLCPicture, VLCPlane>(ref this, PlanesOffset + 32);
    [UnscopedRef]
    public ref VLCPlane Plane2 => ref VLCAbi.Field<VLCPicture, VLCPlane>(ref this, PlanesOffset + 64);
    [UnscopedRef]
    public ref VLCPlane Plane3 => ref VLCAbi.Field<VLCPicture, VLCPlane>(ref this, PlanesOffset + 96);
    [UnscopedRef]
    public ref VLCPlane Plane4 => ref VLCAbi.Field<VLCPicture, VLCPlane>(ref this, PlanesOffset + 128);

    /// <summary>Number of allocated planes (i_planes)</summary>
    [UnscopedRef]
    public ref int PlaneCount => ref VLCAbi.Field<VLCPicture, int>(ref this, PlanesOffset + 160);

    /// <summary>Display date (date) - vlc_tick_t is int64</summary>
    [UnscopedRef]
    public ref long Date => ref VLCAbi.Field<VLCPicture, long>(ref this, PlanesOffset + 168);

    /// <summary>Force display (b_force)</summary>
    [UnscopedRef]
    public ref byte Force => ref VLCAbi.Field<VLCPicture, byte>(ref this, PlanesOffset + 176);

    /// <summary>Still image (b_still)</summary>
    [UnscopedRef]
    public ref byte Still => ref VLCAbi.Field<VLCPicture, byte>(ref this, PlanesOffset + 177);

    /// <summary>Progressive frame (b_progressive)</summary>
    [UnscopedRef]
    public ref byte Progressive => ref VLCAbi.Field<VLCPicture, byte>(ref this, PlanesOffset + 178);

    /// <summary>Top field first (b_top_field_first)</summary>
    [UnscopedRef]
    public ref byte TopFieldFirst => ref VLCAbi.Field<VLCPicture, byte>(ref this, PlanesOffset + 179);

    /// <summary>Left eye in multiview (b_multiview_left_eye)</summary>
    [UnscopedRef]
    public ref byte MultiviewLeftEye => ref VLCAbi.Field<VLCPicture, byte>(ref this, PlanesOffset + 180);

    /// <summary>Number of displayed fields (i_nb_fields)</summary>
    [UnscopedRef]
    public ref uint FieldCount => ref VLCAbi.Field<VLCPicture, uint>(ref this, PlanesOffset + 184);

    /// <summary>Picture context pointer (context)</summary>
    [UnscopedRef]
    public ref nint Context => ref VLCAbi.Field<VLCPicture, nint>(ref this, PlanesOffset + 192);

    /// <summary>Private system data (p_sys)</summary>
    [UnscopedRef]
    public ref nint Sys => ref VLCAbi.Field<VLCPicture, nint>(ref this, PlanesOffset + 200);

    /// <summary>Next picture in FIFO (p_next)</summary>
    [UnscopedRef]
    public ref nint Next => ref VLCAbi.Field<VLCPicture, nint>(ref this, PlanesOffset + 208);

    /// <summary>Reference count (refs) - vlc_atomic_rc_t contains atomic_uint</summary>
    [UnscopedRef]
    public ref uint Refs => ref VLCAbi.Field<VLCPicture, uint>(ref this, PlanesOffset + 216);

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
