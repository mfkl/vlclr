// VLC video format structure
// Source: vlc/include/vlc_es.h
// VLC Version: 4.0.6

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Video format description (video_format_t from vlc_es.h).
/// The native struct is 152 bytes on Windows and 144 bytes on Linux and macOS
/// (<see cref="Interop.VideoFormat.SizeOf"/>). This struct is
/// <see cref="Interop.VideoFormat.MinSizeOf"/> bytes, the part both layouts
/// share, so assigning a whole format into VLC memory
/// (<c>fmt_out.Video = fmt_in.Video</c>) never writes past the native struct.
/// The dovi bitfields and i_cubemap_padding sit at different offsets on the
/// two ABIs and are not exposed. Member offsets come from
/// <see cref="Interop.VideoFormat.Offsets"/>.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Interop.VideoFormat.MinSizeOf)]
public struct VLCVideoFormat
{
    /// <summary>Picture chroma (i_chroma) - fourcc like 'RV32', 'I420', etc.</summary>
    [FieldOffset(Interop.VideoFormat.Offsets.Chroma)] public uint Chroma;

    /// <summary>Picture width (i_width)</summary>
    [FieldOffset(Interop.VideoFormat.Offsets.Width)] public uint Width;

    /// <summary>Picture height (i_height)</summary>
    [FieldOffset(Interop.VideoFormat.Offsets.Height)] public uint Height;

    /// <summary>Start offset of visible area X (i_x_offset)</summary>
    [FieldOffset(Interop.VideoFormat.Offsets.XOffset)] public uint XOffset;

    /// <summary>Start offset of visible area Y (i_y_offset)</summary>
    [FieldOffset(Interop.VideoFormat.Offsets.YOffset)] public uint YOffset;

    /// <summary>Width of visible area (i_visible_width)</summary>
    [FieldOffset(Interop.VideoFormat.Offsets.VisibleWidth)] public uint VisibleWidth;

    /// <summary>Height of visible area (i_visible_height)</summary>
    [FieldOffset(Interop.VideoFormat.Offsets.VisibleHeight)] public uint VisibleHeight;

    /// <summary>Sample aspect ratio numerator (i_sar_num)</summary>
    [FieldOffset(Interop.VideoFormat.Offsets.SarNum)] public uint SarNum;

    /// <summary>Sample aspect ratio denominator (i_sar_den)</summary>
    [FieldOffset(Interop.VideoFormat.Offsets.SarDen)] public uint SarDen;

    /// <summary>Frame rate numerator (i_frame_rate)</summary>
    [FieldOffset(Interop.VideoFormat.Offsets.FrameRate)] public uint FrameRate;

    /// <summary>Frame rate denominator (i_frame_rate_base)</summary>
    [FieldOffset(Interop.VideoFormat.Offsets.FrameRateBase)] public uint FrameRateBase;

    /// <summary>Video palette pointer (p_palette) - usually null</summary>
    [FieldOffset(Interop.VideoFormat.Offsets.Palette)] public nint Palette;

    /// <summary>Picture orientation (orientation)</summary>
    [FieldOffset(Interop.VideoFormat.Offsets.Orientation)] public int Orientation;

    /// <summary>Color primaries (primaries)</summary>
    [FieldOffset(Interop.VideoFormat.Offsets.Primaries)] public int Primaries;

    /// <summary>Transfer function (transfer)</summary>
    [FieldOffset(Interop.VideoFormat.Offsets.Transfer)] public int Transfer;

    /// <summary>YCbCr color space (space)</summary>
    [FieldOffset(Interop.VideoFormat.Offsets.Space)] public int Space;

    /// <summary>Color range, 0-255 vs 16-235 (color_range)</summary>
    [FieldOffset(Interop.VideoFormat.Offsets.ColorRange)] public int ColorRange;

    /// <summary>YCbCr chroma location (chroma_location)</summary>
    [FieldOffset(Interop.VideoFormat.Offsets.ChromaLocation)] public int ChromaLocation;

    /// <summary>Multiview mode (multiview_mode)</summary>
    [FieldOffset(Interop.VideoFormat.Offsets.MultiviewMode)] public int MultiviewMode;

    /// <summary>Multiview right eye first flag (b_multiview_right_eye_first, bool)</summary>
    [FieldOffset(Interop.VideoFormat.Offsets.MultiviewRightEyeFirst)] public byte MultiviewRightEyeFirst;

    /// <summary>Projection mode (projection_mode)</summary>
    [FieldOffset(Interop.VideoFormat.Offsets.ProjectionMode)] public int ProjectionMode;

    /// <summary>Viewpoint yaw (pose.yaw)</summary>
    [FieldOffset(Interop.VideoFormat.Offsets.Pose + Interop.Viewpoint.Offsets.Yaw)] public float PoseYaw;

    /// <summary>Viewpoint pitch (pose.pitch)</summary>
    [FieldOffset(Interop.VideoFormat.Offsets.Pose + Interop.Viewpoint.Offsets.Pitch)] public float PosePitch;

    /// <summary>Viewpoint roll (pose.roll)</summary>
    [FieldOffset(Interop.VideoFormat.Offsets.Pose + Interop.Viewpoint.Offsets.Roll)] public float PoseRoll;

    /// <summary>Viewpoint field of view (pose.fov)</summary>
    [FieldOffset(Interop.VideoFormat.Offsets.Pose + Interop.Viewpoint.Offsets.Fov)] public float PoseFov;

    // Mastering display color volume (mastering.primaries[0..5], white_point[0..1], luminance)
    [FieldOffset(Interop.VideoFormat.Offsets.Mastering + Interop.VideoFormatMastering.Offsets.Primaries + 0 * sizeof(ushort))] public ushort MasteringPrimariesGX;
    [FieldOffset(Interop.VideoFormat.Offsets.Mastering + Interop.VideoFormatMastering.Offsets.Primaries + 1 * sizeof(ushort))] public ushort MasteringPrimariesGY;
    [FieldOffset(Interop.VideoFormat.Offsets.Mastering + Interop.VideoFormatMastering.Offsets.Primaries + 2 * sizeof(ushort))] public ushort MasteringPrimariesBX;
    [FieldOffset(Interop.VideoFormat.Offsets.Mastering + Interop.VideoFormatMastering.Offsets.Primaries + 3 * sizeof(ushort))] public ushort MasteringPrimariesBY;
    [FieldOffset(Interop.VideoFormat.Offsets.Mastering + Interop.VideoFormatMastering.Offsets.Primaries + 4 * sizeof(ushort))] public ushort MasteringPrimariesRX;
    [FieldOffset(Interop.VideoFormat.Offsets.Mastering + Interop.VideoFormatMastering.Offsets.Primaries + 5 * sizeof(ushort))] public ushort MasteringPrimariesRY;
    [FieldOffset(Interop.VideoFormat.Offsets.Mastering + Interop.VideoFormatMastering.Offsets.WhitePoint + 0 * sizeof(ushort))] public ushort MasteringWhitePointX;
    [FieldOffset(Interop.VideoFormat.Offsets.Mastering + Interop.VideoFormatMastering.Offsets.WhitePoint + 1 * sizeof(ushort))] public ushort MasteringWhitePointY;
    [FieldOffset(Interop.VideoFormat.Offsets.Mastering + Interop.VideoFormatMastering.Offsets.MaxLuminance)] public uint MasteringMaxLuminance;
    [FieldOffset(Interop.VideoFormat.Offsets.Mastering + Interop.VideoFormatMastering.Offsets.MinLuminance)] public uint MasteringMinLuminance;

    // Content light level (lighting.MaxCLL, lighting.MaxFALL)
    [FieldOffset(Interop.VideoFormat.Offsets.Lighting + Interop.VideoFormatLighting.Offsets.MaxCLL)] public ushort LightingMaxCLL;
    [FieldOffset(Interop.VideoFormat.Offsets.Lighting + Interop.VideoFormatLighting.Offsets.MaxFALL)] public ushort LightingMaxFALL;

    // Dolby Vision version bytes (dovi.version_major, dovi.version_minor)
    [FieldOffset(Interop.VideoFormat.Offsets.Dovi + Interop.VideoFormatDovi.Offsets.VersionMajor)] public byte DoviVersionMajor;
    [FieldOffset(Interop.VideoFormat.Offsets.Dovi + Interop.VideoFormatDovi.Offsets.VersionMinor)] public byte DoviVersionMinor;
}
