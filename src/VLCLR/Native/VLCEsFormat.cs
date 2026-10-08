// VLC ES format structure
// Source: vlc-3.0.24-beta1/sdk/include/vlc/plugins/vlc_es.h
// VLC Version: 3.0.24-beta1

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// ES format definition (es_format_t from vlc_es.h).
/// Size on 64-bit: 264 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct VLCEsFormat
{
    public int Category;          // i_cat
    public uint Codec;            // i_codec
    public uint OriginalFourcc;   // i_original_fourcc
    public int Id;                // i_id
    public int Group;             // i_group
    public int Priority;          // i_priority
    public nint Language;         // psz_language
    public nint Description;      // psz_description
    public uint ExtraLanguagesCount; // i_extra_languages
    public nint ExtraLanguages;   // p_extra_languages

    /// <summary>Video format (in union with audio and subs)</summary>
    public VLCVideoFormat Video;

    /// <summary>Audio format view of the union</summary>
    public VLCAudioFormat Audio
    {
        readonly get
        {
            fixed (VLCVideoFormat* union = &Video)
                return *(VLCAudioFormat*)union;
        }
        set
        {
            fixed (VLCVideoFormat* union = &Video)
                *(VLCAudioFormat*)union = value;
        }
    }

    public uint Bitrate;          // i_bitrate
    public int Profile;           // i_profile
    public int Level;             // i_level
    public byte Packetized;       // b_packetized
    public int ExtraSize;         // i_extra (native int, not nuint)
    public nint Extra;            // p_extra
}
