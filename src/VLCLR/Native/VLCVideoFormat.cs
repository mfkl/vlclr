// VLC video format structure
// Source: vlc/include/vlc_es.h
// VLC Version: 4.0.6

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Video format description (video_format_t from vlc_es.h)
/// Size on 64-bit: 152 bytes
///
/// Layout:
/// - offset 0-43: 11 unsigned ints (chroma through frame_rate_base)
/// - offset 44-47: padding for pointer alignment
/// - offset 48-55: p_palette pointer
/// - offset 56-79: 6 enums (orientation through chroma_location)
/// - offset 80-83: multiview_mode
/// - offset 84: b_multiview_right_eye_first + 3 padding
/// - offset 88-91: projection_mode
/// - offset 92-107: viewpoint pose (4 floats)
/// - offset 108-131: mastering (primaries + white_point + luminance)
/// - offset 132-135: lighting (MaxCLL + MaxFALL)
/// - offset 136-139: dovi (version + bitfields)
/// - offset 140-143: i_cubemap_padding
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VLCVideoFormat
{
    /// <summary>Picture chroma (i_chroma) - fourcc like 'RV32', 'I420', etc.</summary>
    public uint Chroma;           // offset 0

    /// <summary>Picture width (i_width)</summary>
    public uint Width;            // offset 4

    /// <summary>Picture height (i_height)</summary>
    public uint Height;           // offset 8

    /// <summary>Start offset of visible area X (i_x_offset)</summary>
    public uint XOffset;          // offset 12

    /// <summary>Start offset of visible area Y (i_y_offset)</summary>
    public uint YOffset;          // offset 16

    /// <summary>Width of visible area (i_visible_width)</summary>
    public uint VisibleWidth;     // offset 20

    /// <summary>Height of visible area (i_visible_height)</summary>
    public uint VisibleHeight;    // offset 24

    /// <summary>Sample aspect ratio numerator (i_sar_num)</summary>
    public uint SarNum;           // offset 28

    /// <summary>Sample aspect ratio denominator (i_sar_den)</summary>
    public uint SarDen;           // offset 32

    /// <summary>Frame rate numerator (i_frame_rate)</summary>
    public uint FrameRate;        // offset 36

    /// <summary>Frame rate denominator (i_frame_rate_base)</summary>
    public uint FrameRateBase;    // offset 40

    // Padding for 8-byte pointer alignment (44 % 8 = 4, need 4 more)
    private uint _padBeforePalette;  // offset 44

    /// <summary>Video palette pointer (p_palette) - usually null</summary>
    public nint Palette;          // offset 48

    /// <summary>Picture orientation</summary>
    public int Orientation;       // offset 56

    /// <summary>Color primaries</summary>
    public int Primaries;         // offset 60

    /// <summary>Transfer function</summary>
    public int Transfer;          // offset 64

    /// <summary>YCbCr color space</summary>
    public int Space;             // offset 68

    /// <summary>Color range (0-255 vs 16-235)</summary>
    public int ColorRange;        // offset 72

    /// <summary>YCbCr chroma location</summary>
    public int ChromaLocation;    // offset 76

    /// <summary>Multiview mode</summary>
    public int MultiviewMode;     // offset 80

    /// <summary>Multiview right eye first flag (bool)</summary>
    public byte MultiviewRightEyeFirst;  // offset 84

    // Padding to align next int
    private byte _pad1;           // offset 85
    private byte _pad2;           // offset 86
    private byte _pad3;           // offset 87

    /// <summary>Projection mode</summary>
    public int ProjectionMode;    // offset 88

    /// <summary>Viewpoint pose - 4 floats (yaw, pitch, roll, fov)</summary>
    public float PoseYaw;         // offset 92
    public float PosePitch;       // offset 96
    public float PoseRoll;        // offset 100
    public float PoseFov;         // offset 104

    // Mastering display color volume
    public ushort MasteringPrimariesGX;   // offset 108
    public ushort MasteringPrimariesGY;   // offset 110
    public ushort MasteringPrimariesBX;   // offset 112
    public ushort MasteringPrimariesBY;   // offset 114
    public ushort MasteringPrimariesRX;   // offset 116
    public ushort MasteringPrimariesRY;   // offset 118
    public ushort MasteringWhitePointX;   // offset 120
    public ushort MasteringWhitePointY;   // offset 122
    public uint MasteringMaxLuminance;    // offset 124
    public uint MasteringMinLuminance;    // offset 128

    // Content light level
    public ushort LightingMaxCLL;         // offset 132
    public ushort LightingMaxFALL;        // offset 134

    // Dolby Vision info
    public byte DoviVersionMajor;         // offset 136
    public byte DoviVersionMinor;         // offset 137
    public ushort DoviFlags;              // offset 138 (bitfields packed as 16 bits)

    /// <summary>Cubemap padding</summary>
    public uint CubemapPadding;           // offset 140

    // Additional padding to reach 152 bytes (discovered via memory scanning)
    // VLC 4.x video_format_t is 152 bytes, not 144
    private uint _endPad1;                // offset 144
    private uint _endPad2;                // offset 148
    // Total: 152 bytes (actual VLC 4.x video_format_t size)
}
