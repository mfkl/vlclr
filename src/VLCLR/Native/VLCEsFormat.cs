// VLC ES format structure
// Source: vlc/include/vlc_es.h
// VLC Version: 4.0.6

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// ES format definition (es_format_t from vlc_es.h)
/// Contains video_format_t in a union - we access .video directly
///
/// On 64-bit:
/// - Fields before union: 56 bytes (including padding for pointer alignment)
/// - Union (video_format_t): 152 bytes
/// - Fields after union: ~32 bytes
/// Total: ~240 bytes
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct VLCEsFormat
{
    /// <summary>ES category (i_cat): 0=UNKNOWN, 1=VIDEO, 2=AUDIO, 3=SPU, 4=DATA</summary>
    public int Category;          // offset 0, size 4

    /// <summary>FOURCC value (i_codec)</summary>
    public uint Codec;            // offset 4, size 4

    /// <summary>Original FOURCC from container (i_original_fourcc)</summary>
    public uint OriginalFourcc;   // offset 8, size 4

    /// <summary>ES identifier (i_id)</summary>
    public int Id;                // offset 12, size 4

    /// <summary>Group identifier (i_group)</summary>
    public int Group;             // offset 16, size 4

    /// <summary>Priority (i_priority)</summary>
    public int Priority;          // offset 20, size 4

    /// <summary>Language string pointer (psz_language)</summary>
    public nint Language;         // offset 24, size 8

    /// <summary>Description string pointer (psz_description)</summary>
    public nint Description;      // offset 32, size 8

    /// <summary>Number of extra languages (i_extra_languages)</summary>
    public uint ExtraLanguagesCount;  // offset 40, size 4

    // Padding for 8-byte pointer alignment
    private uint _padBeforeExtraLanguages;  // offset 44, size 4

    /// <summary>Extra languages pointer (p_extra_languages)</summary>
    public nint ExtraLanguages;   // offset 48, size 8

    /// <summary>Video format (in union with audio and subs) - starts at offset 56</summary>
    public VLCVideoFormat Video;

    /// <summary>Bitrate (i_bitrate)</summary>
    public uint Bitrate;

    /// <summary>Codec profile (i_profile)</summary>
    public int Profile;

    /// <summary>Codec level (i_level)</summary>
    public int Level;

    /// <summary>Whether data is packetized (b_packetized)</summary>
    public byte Packetized;

    // Padding for size_t alignment (8 bytes on 64-bit)
    // After i_level (offset 216) + b_packetized (1 byte at 220) = offset 221
    // Next 8-byte boundary is 224, so need 3 bytes padding
    private byte _pad1;
    private byte _pad2;
    private byte _pad3;

    /// <summary>Extra data length (i_extra)</summary>
    public nuint ExtraSize;

    /// <summary>Extra data pointer (p_extra)</summary>
    public nint Extra;
}
