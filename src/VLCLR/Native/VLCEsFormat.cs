// VLC ES format structure
// Source: vlc/include/vlc_es.h
// VLC Version: 4.0.6

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// ES format definition (es_format_t from vlc_es.h)
/// Contains video_format_t in a union - we access .video directly
///
/// On 64-bit:
/// - Fields before union: 56 bytes (including padding for pointer alignment)
/// - Union (video_format_t): 152 bytes on Windows, 144 on Linux and macOS
/// - Fields after union: 32 bytes, reached through ref properties placed for
///   the ABI the plugin runs on (see <see cref="VLCAbi"/>)
/// Total: 240 bytes on Windows, 232 on Linux and macOS. The managed struct
/// is sized for the larger layout, so a copy always holds every field.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 240)]
public unsafe struct VLCEsFormat
{
    /// <summary>ES category (i_cat): 0=UNKNOWN, 1=VIDEO, 2=AUDIO, 3=SPU, 4=DATA</summary>
    [FieldOffset(0)]
    public int Category;          // offset 0, size 4

    /// <summary>FOURCC value (i_codec)</summary>
    [FieldOffset(4)]
    public uint Codec;            // offset 4, size 4

    /// <summary>Original FOURCC from container (i_original_fourcc)</summary>
    [FieldOffset(8)]
    public uint OriginalFourcc;   // offset 8, size 4

    /// <summary>ES identifier (i_id)</summary>
    [FieldOffset(12)]
    public int Id;                // offset 12, size 4

    /// <summary>Group identifier (i_group)</summary>
    [FieldOffset(16)]
    public int Group;             // offset 16, size 4

    /// <summary>Priority (i_priority)</summary>
    [FieldOffset(20)]
    public int Priority;          // offset 20, size 4

    /// <summary>Language string pointer (psz_language)</summary>
    [FieldOffset(24)]
    public nint Language;         // offset 24, size 8

    /// <summary>Description string pointer (psz_description)</summary>
    [FieldOffset(32)]
    public nint Description;      // offset 32, size 8

    /// <summary>Number of extra languages (i_extra_languages)</summary>
    [FieldOffset(40)]
    public uint ExtraLanguagesCount;  // offset 40, size 4

    // Padding for 8-byte pointer alignment
    [FieldOffset(44)]
    private uint _padBeforeExtraLanguages;  // offset 44, size 4

    /// <summary>Extra languages pointer (p_extra_languages)</summary>
    [FieldOffset(48)]
    public nint ExtraLanguages;   // offset 48, size 8

    /// <summary>Video format (in union with audio and subs) - starts at offset 56</summary>
    [FieldOffset(56)]
    public VLCVideoFormat Video;

    /// <summary>
    /// Gets the audio-format view of the ES format union.
    /// Only valid when the ES format represents audio. VLC audio-filter chains
    /// can leave <see cref="Category"/> as unknown while still populating it.
    /// </summary>
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

    private static int TailOffset => 56 + VLCAbi.VideoFormatSize;

    /// <summary>Bitrate (i_bitrate)</summary>
    [UnscopedRef]
    public ref uint Bitrate => ref VLCAbi.Field<VLCEsFormat, uint>(ref this, TailOffset);

    /// <summary>Codec profile (i_profile)</summary>
    [UnscopedRef]
    public ref int Profile => ref VLCAbi.Field<VLCEsFormat, int>(ref this, TailOffset + 4);

    /// <summary>Codec level (i_level)</summary>
    [UnscopedRef]
    public ref int Level => ref VLCAbi.Field<VLCEsFormat, int>(ref this, TailOffset + 8);

    /// <summary>Whether data is packetized (b_packetized)</summary>
    [UnscopedRef]
    public ref byte Packetized => ref VLCAbi.Field<VLCEsFormat, byte>(ref this, TailOffset + 12);

    /// <summary>Extra data length (i_extra)</summary>
    [UnscopedRef]
    public ref nuint ExtraSize => ref VLCAbi.Field<VLCEsFormat, nuint>(ref this, TailOffset + 16);

    /// <summary>Extra data pointer (p_extra)</summary>
    [UnscopedRef]
    public ref nint Extra => ref VLCAbi.Field<VLCEsFormat, nint>(ref this, TailOffset + 24);
}
