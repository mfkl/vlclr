// VLC video format structure
// Source: vlc-3.0.24-beta1/sdk/include/vlc/plugins/vlc_es.h
// VLC Version: 3.0.24-beta1

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Video format description (video_format_t from vlc_es.h).
/// Size on 64-bit: 176 bytes.
///
/// VLC 3 has i_bits_per_pixel, RGB masks/shifts, and b_color_range_full.
/// CLR handles alignment padding naturally with LayoutKind.Sequential.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VLCVideoFormat
{
    public uint Chroma;              // i_chroma
    public uint Width;               // i_width
    public uint Height;              // i_height
    public uint XOffset;             // i_x_offset
    public uint YOffset;             // i_y_offset
    public uint VisibleWidth;        // i_visible_width
    public uint VisibleHeight;       // i_visible_height
    public uint BitsPerPixel;        // i_bits_per_pixel
    public uint SarNum;              // i_sar_num
    public uint SarDen;              // i_sar_den
    public uint FrameRate;           // i_frame_rate
    public uint FrameRateBase;       // i_frame_rate_base
    public uint RedMask;             // i_rmask
    public uint GreenMask;           // i_gmask
    public uint BlueMask;            // i_bmask
    public int RedRightShift;        // i_rrshift
    public int RedLeftShift;         // i_lrshift
    public int GreenRightShift;      // i_rgshift
    public int GreenLeftShift;       // i_lgshift
    public int BlueRightShift;       // i_rbshift
    public int BlueLeftShift;        // i_lbshift
    // CLR inserts 4 bytes padding here for nint alignment
    public nint Palette;             // p_palette
    public int Orientation;          // video_orientation_t
    public int Primaries;            // video_color_primaries_t
    public int Transfer;             // video_transfer_func_t
    public int Space;                // video_color_space_t
    public byte ColorRangeFull;      // b_color_range_full
    // CLR inserts 3 bytes padding here for int alignment
    public int ChromaLocation;       // video_chroma_location_t
    public int MultiviewMode;        // video_multiview_mode_t
    public int ProjectionMode;       // video_projection_mode_t
    public float PoseYaw;            // pose.yaw
    public float PosePitch;          // pose.pitch
    public float PoseRoll;           // pose.roll
    public float PoseFov;            // pose.fov
    public ushort MasteringPrimariesGX;
    public ushort MasteringPrimariesGY;
    public ushort MasteringPrimariesBX;
    public ushort MasteringPrimariesBY;
    public ushort MasteringPrimariesRX;
    public ushort MasteringPrimariesRY;
    public ushort MasteringWhitePointX;
    public ushort MasteringWhitePointY;
    public uint MasteringMaxLuminance;
    public uint MasteringMinLuminance;
    public ushort LightingMaxCLL;
    public ushort LightingMaxFALL;
    public uint CubemapPadding;      // i_cubemap_padding
}
