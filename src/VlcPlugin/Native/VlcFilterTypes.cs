// VLC Filter Types - C# structures for direct VLC video filter integration
// Based on vlc_filter.h, vlc_picture.h, vlc_es.h from VLC 4.x

using System.Runtime.InteropServices;

namespace VlcPlugin.Native;

/// <summary>
/// Maximum number of planes in a picture (PICTURE_PLANE_MAX = VOUT_MAX_PLANES = 5)
/// </summary>
public static class VlcFilterConstants
{
    public const int PICTURE_PLANE_MAX = 5;
}

/// <summary>
/// Description of a planar graphic field (plane_t from vlc_picture.h)
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VlcPlane
{
    /// <summary>Start of the plane's data (p_pixels)</summary>
    public nint Pixels;

    /// <summary>Number of lines, including margins (i_lines)</summary>
    public int Lines;

    /// <summary>Number of bytes in a line, including margins (i_pitch)</summary>
    public int Pitch;

    /// <summary>Size of a macropixel, defaults to 1 (i_pixel_pitch)</summary>
    public int PixelPitch;

    /// <summary>How many visible lines are there? (i_visible_lines)</summary>
    public int VisibleLines;

    /// <summary>How many bytes for visible pixels are there? (i_visible_pitch)</summary>
    public int VisiblePitch;
}

/// <summary>
/// Video format description (video_format_t from vlc_es.h)
/// This is a large structure - we define only the fields we need to access
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VlcVideoFormat
{
    /// <summary>Picture chroma (i_chroma) - fourcc like 'RV32', 'I420', etc.</summary>
    public uint Chroma;

    /// <summary>Picture width (i_width)</summary>
    public uint Width;

    /// <summary>Picture height (i_height)</summary>
    public uint Height;

    /// <summary>Start offset of visible area X (i_x_offset)</summary>
    public uint XOffset;

    /// <summary>Start offset of visible area Y (i_y_offset)</summary>
    public uint YOffset;

    /// <summary>Width of visible area (i_visible_width)</summary>
    public uint VisibleWidth;

    /// <summary>Height of visible area (i_visible_height)</summary>
    public uint VisibleHeight;

    /// <summary>Sample aspect ratio numerator (i_sar_num)</summary>
    public uint SarNum;

    /// <summary>Sample aspect ratio denominator (i_sar_den)</summary>
    public uint SarDen;

    /// <summary>Frame rate numerator (i_frame_rate)</summary>
    public uint FrameRate;

    /// <summary>Frame rate denominator (i_frame_rate_base)</summary>
    public uint FrameRateBase;

    /// <summary>Video palette pointer (p_palette) - usually null</summary>
    public nint Palette;

    /// <summary>Picture orientation</summary>
    public int Orientation;

    /// <summary>Color primaries</summary>
    public int Primaries;

    /// <summary>Transfer function</summary>
    public int Transfer;

    /// <summary>YCbCr color space</summary>
    public int Space;

    /// <summary>Color range (0-255 vs 16-235)</summary>
    public int ColorRange;

    /// <summary>YCbCr chroma location</summary>
    public int ChromaLocation;

    /// <summary>Multiview mode</summary>
    public int MultiviewMode;

    /// <summary>Multiview right eye first flag</summary>
    public byte MultiviewRightEyeFirst;

    // Padding to align next field
    private byte _pad1;
    private byte _pad2;
    private byte _pad3;

    /// <summary>Projection mode</summary>
    public int ProjectionMode;

    /// <summary>Viewpoint pose - 4 floats (yaw, pitch, roll, fov)</summary>
    public float PoseYaw;
    public float PosePitch;
    public float PoseRoll;
    public float PoseFov;

    // Mastering display color volume (6 uint16 primaries + 2 uint16 white point + 2 uint32 luminance)
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

    // Content light level
    public ushort LightingMaxCLL;
    public ushort LightingMaxFALL;

    // Dolby Vision info (packed bits)
    public byte DoviVersionMajor;
    public byte DoviVersionMinor;
    public ushort DoviFlags; // profile:7, level:6, rpu:1, el:1, bl:1

    /// <summary>Cubemap padding</summary>
    public uint CubemapPadding;
}

/// <summary>
/// ES format definition (es_format_t from vlc_es.h)
/// Contains video_format_t in a union - we access .video directly
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct VlcEsFormat
{
    /// <summary>ES category (i_cat): 0=UNKNOWN, 1=VIDEO, 2=AUDIO, 3=SPU, 4=DATA</summary>
    public int Category;

    /// <summary>FOURCC value (i_codec)</summary>
    public uint Codec;

    /// <summary>Original FOURCC from container (i_original_fourcc)</summary>
    public uint OriginalFourcc;

    /// <summary>ES identifier (i_id)</summary>
    public int Id;

    /// <summary>Group identifier (i_group)</summary>
    public int Group;

    /// <summary>Priority (i_priority)</summary>
    public int Priority;

    /// <summary>Language string pointer (psz_language)</summary>
    public nint Language;

    /// <summary>Description string pointer (psz_description)</summary>
    public nint Description;

    /// <summary>Number of extra languages (i_extra_languages)</summary>
    public uint ExtraLanguagesCount;

    /// <summary>Extra languages pointer (p_extra_languages)</summary>
    public nint ExtraLanguages;

    /// <summary>Video format (in union with audio and subs)</summary>
    public VlcVideoFormat Video;

    /// <summary>Bitrate (i_bitrate)</summary>
    public uint Bitrate;

    /// <summary>Codec profile (i_profile)</summary>
    public int Profile;

    /// <summary>Codec level (i_level)</summary>
    public int Level;

    /// <summary>Whether data is packetized (b_packetized)</summary>
    public byte Packetized;

    // Padding for alignment
    private byte _pad1;
    private byte _pad2;
    private byte _pad3;
    private int _pad4; // Align to 8-byte boundary for pointers

    /// <summary>Extra data length (i_extra)</summary>
    public nuint ExtraSize;

    /// <summary>Extra data pointer (p_extra)</summary>
    public nint Extra;
}

/// <summary>
/// Video picture structure (picture_t from vlc_picture.h)
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct VlcPicture
{
    /// <summary>The properties of the picture (video_frame_format_t = video_format_t)</summary>
    public VlcVideoFormat Format;

    /// <summary>Array of planes (p[PICTURE_PLANE_MAX])</summary>
    public VlcPlane Plane0;
    public VlcPlane Plane1;
    public VlcPlane Plane2;
    public VlcPlane Plane3;
    public VlcPlane Plane4;

    /// <summary>Number of allocated planes (i_planes)</summary>
    public int PlaneCount;

    // Padding for alignment
    private int _pad1;

    /// <summary>Display date (date) - vlc_tick_t is int64</summary>
    public long Date;

    /// <summary>Force display (b_force)</summary>
    public byte Force;

    /// <summary>Still image (b_still)</summary>
    public byte Still;

    /// <summary>Progressive frame (b_progressive)</summary>
    public byte Progressive;

    /// <summary>Top field first (b_top_field_first)</summary>
    public byte TopFieldFirst;

    /// <summary>Left eye in multiview (b_multiview_left_eye)</summary>
    public byte MultiviewLeftEye;

    // Padding
    private byte _pad2;
    private byte _pad3;
    private byte _pad4;

    /// <summary>Number of displayed fields (i_nb_fields)</summary>
    public uint FieldCount;

    // Padding for alignment before pointer
    private int _pad5;

    /// <summary>Picture context pointer (context)</summary>
    public nint Context;

    /// <summary>Private system data (p_sys)</summary>
    public nint Sys;

    /// <summary>Next picture in FIFO (p_next)</summary>
    public nint Next;

    /// <summary>Reference count (refs) - vlc_atomic_rc_t contains atomic_uint</summary>
    public uint Refs;

    /// <summary>
    /// Get a plane by index (safe accessor)
    /// </summary>
    public VlcPlane GetPlane(int index)
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

/// <summary>
/// Filter operations structure (vlc_filter_operations from vlc_filter.h)
/// Video filter uses filter_video callback
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VlcFilterOperations
{
    /// <summary>
    /// Filter a picture (filter_video) - first member of union
    /// Signature: picture_t* (*filter_video)(filter_t*, picture_t*)
    /// </summary>
    public nint FilterVideo;

    /// <summary>
    /// Drain callback (union, overlaps with drain_audio for audio filters)
    /// </summary>
    public nint Drain;

    /// <summary>
    /// Flush callback
    /// Signature: void (*flush)(filter_t*)
    /// </summary>
    public nint Flush;

    /// <summary>
    /// Change viewpoint callback
    /// Signature: void (*change_viewpoint)(filter_t*, const vlc_viewpoint_t*)
    /// </summary>
    public nint ChangeViewpoint;

    /// <summary>
    /// Video mouse callback
    /// Signature: int (*video_mouse)(filter_t*, vlc_mouse_t*, const vlc_mouse_t*)
    /// </summary>
    public nint VideoMouse;

    /// <summary>
    /// Close callback - release filter resources
    /// Signature: void (*close)(filter_t*)
    /// </summary>
    public nint Close;
}

/// <summary>
/// Filter video callbacks structure (filter_video_callbacks from vlc_filter.h)
/// Used in filter_owner_t
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VlcFilterVideoCallbacks
{
    /// <summary>
    /// Allocate new picture buffer
    /// Signature: picture_t* (*buffer_new)(filter_t*)
    /// </summary>
    public nint BufferNew;

    /// <summary>
    /// Hold decoder device
    /// Signature: vlc_decoder_device* (*hold_device)(vlc_object_t*, void* sys)
    /// </summary>
    public nint HoldDevice;
}

/// <summary>
/// Filter owner structure (filter_owner_t from vlc_filter.h)
/// Contains callbacks union and attachments function
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VlcFilterOwner
{
    /// <summary>
    /// Callbacks union - pointer to video/audio/sub callbacks
    /// For video filters, this points to filter_video_callbacks
    /// </summary>
    public nint Callbacks;

    /// <summary>
    /// Get attachments function pointer
    /// Signature: int (*pf_get_attachments)(filter_t*, input_attachment_t***, int*)
    /// </summary>
    public nint GetAttachments;

    /// <summary>
    /// Owner system data (sys)
    /// </summary>
    public nint Sys;
}

/// <summary>
/// Filter structure (filter_t from vlc_filter.h)
/// IMPORTANT: The first 3 members must match decoder_t layout
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VlcFilter
{
    /// <summary>VLC object header (struct vlc_object_t obj)</summary>
    public VlcObjectHeader Obj;

    /// <summary>Module pointer (p_module)</summary>
    public nint Module;

    /// <summary>Private system data (p_sys) - filter implementation stores its state here</summary>
    public nint Sys;

    /// <summary>Input format (fmt_in)</summary>
    public VlcEsFormat FormatIn;

    /// <summary>Video context input (vctx_in)</summary>
    public nint VideoContextIn;

    /// <summary>Output format (fmt_out)</summary>
    public VlcEsFormat FormatOut;

    /// <summary>Video context output (vctx_out)</summary>
    public nint VideoContextOut;

    /// <summary>Allow format out change flag (b_allow_fmt_out_change)</summary>
    public byte AllowFormatOutChange;

    // Padding for alignment before pointer
    private byte _pad1;
    private byte _pad2;
    private byte _pad3;
    private int _pad4;

    /// <summary>Requested filter shortcut name (psz_name)</summary>
    public nint Name;

    /// <summary>Filter configuration chain (p_cfg)</summary>
    public nint Config;

    /// <summary>Filter operations pointer (ops) - MUST be set by Open callback</summary>
    public nint Operations;

    /// <summary>Filter owner (owner)</summary>
    public VlcFilterOwner Owner;
}

/// <summary>
/// VLC object header - first member of all VLC objects
/// This is struct vlc_object_t from vlc_objects.h
/// We only define what we need to calculate offsets
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VlcObjectHeader
{
    /// <summary>Logger pointer</summary>
    public nint Logger;

    /// <summary>Parent object pointer</summary>
    public nint Parent;

    /// <summary>Object type name</summary>
    public nint TypeName;

    /// <summary>Header suffix - flags, etc. The exact size depends on VLC build</summary>
    public nint Flags;

    /// <summary>Reference count</summary>
    public uint Refs;

    // Additional padding - vlc_object_t size varies by platform
    // On 64-bit, it's typically 40-48 bytes
    private uint _pad1;
    private nint _pad2;
}

/// <summary>
/// Chroma description returned by vlc_fourcc_GetChromaDescription
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VlcChromaDescription
{
    /// <summary>Number of planes</summary>
    public uint PlaneCount;

    /// <summary>Pixel size in bytes</summary>
    public uint PixelSize;

    /// <summary>Pixel size in bits</summary>
    public uint PixelBits;

    // Additional fields we don't need but exist in the structure
    // plane width/height divisors, etc.
}
