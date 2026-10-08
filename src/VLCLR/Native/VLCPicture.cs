// VLC picture structure
// Source: vlc-3.0.24-beta1/sdk/include/vlc/plugins/vlc_picture.h
// VLC Version: 3.0.24-beta1

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Video picture structure (picture_t from vlc_picture.h).
/// Size on 64-bit: 384 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct VLCPicture
{
    /// <summary>The properties of the picture (video_frame_format_t = video_format_t)</summary>
    public VLCVideoFormat Format;

    /// <summary>Array of planes (p[PICTURE_PLANE_MAX])</summary>
    public VLCPlane Plane0;
    public VLCPlane Plane1;
    public VLCPlane Plane2;
    public VLCPlane Plane3;
    public VLCPlane Plane4;

    /// <summary>Number of allocated planes (i_planes)</summary>
    public int PlaneCount;

    // 4 bytes padding

    /// <summary>Display date (date) - vlc_tick_t is int64</summary>
    public long Date;

    /// <summary>Force display (b_force)</summary>
    public byte Force;

    /// <summary>Progressive frame (b_progressive)</summary>
    public byte Progressive;

    /// <summary>Top field first (b_top_field_first)</summary>
    public byte TopFieldFirst;

    private byte _pad0;

    /// <summary>Number of displayed fields (i_nb_fields)</summary>
    public uint FieldCount;

    /// <summary>Picture context pointer (context)</summary>
    public nint Context;

    /// <summary>Private system data (p_sys)</summary>
    public nint Sys;

    /// <summary>Next picture in FIFO (p_next)</summary>
    public nint Next;

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
