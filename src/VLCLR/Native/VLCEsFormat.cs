// VLC ES format structure
// Source: vlc/include/vlc_es.h
// VLC Version: 4.0.6

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// ES format definition (es_format_t from vlc_es.h)
/// Contains video_format_t in a union - we access .video directly.
/// Offsets come from <see cref="Interop.EsFormat.Offsets"/>. The union is
/// <see cref="Interop.VideoFormat.SizeOf"/> bytes (152 on Windows, 144 on Linux
/// and macOS), so the members after it are ref properties placed for the ABI
/// the plugin runs on. The managed struct is sized for the larger layout
/// (<see cref="Interop.EsFormat.MaxSizeOf"/>), so a copy always holds every field.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Interop.EsFormat.MaxSizeOf)]
public unsafe struct VLCEsFormat
{
    /// <summary>ES category (i_cat): 0=UNKNOWN, 1=VIDEO, 2=AUDIO, 3=SPU, 4=DATA</summary>
    [FieldOffset(Interop.EsFormat.Offsets.Cat)]
    public int Category;

    /// <summary>FOURCC value (i_codec)</summary>
    [FieldOffset(Interop.EsFormat.Offsets.Codec)]
    public uint Codec;

    /// <summary>Original FOURCC from container (i_original_fourcc)</summary>
    [FieldOffset(Interop.EsFormat.Offsets.OriginalFourcc)]
    public uint OriginalFourcc;

    /// <summary>ES identifier (i_id)</summary>
    [FieldOffset(Interop.EsFormat.Offsets.Id)]
    public int Id;

    /// <summary>Group identifier (i_group)</summary>
    [FieldOffset(Interop.EsFormat.Offsets.Group)]
    public int Group;

    /// <summary>Priority (i_priority)</summary>
    [FieldOffset(Interop.EsFormat.Offsets.Priority)]
    public int Priority;

    /// <summary>Language string pointer (psz_language)</summary>
    [FieldOffset(Interop.EsFormat.Offsets.Language)]
    public nint Language;

    /// <summary>Description string pointer (psz_description)</summary>
    [FieldOffset(Interop.EsFormat.Offsets.Description)]
    public nint Description;

    /// <summary>Number of extra languages (i_extra_languages)</summary>
    [FieldOffset(Interop.EsFormat.Offsets.ExtraLanguagesCount)]
    public uint ExtraLanguagesCount;

    /// <summary>Extra languages pointer (p_extra_languages)</summary>
    [FieldOffset(Interop.EsFormat.Offsets.ExtraLanguages)]
    public nint ExtraLanguages;

    /// <summary>Video format (in union with audio and subs)</summary>
    [FieldOffset(Interop.EsFormat.Offsets.Video)]
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

    /// <summary>Bitrate (i_bitrate)</summary>
    [UnscopedRef]
    public ref uint Bitrate => ref Interop.CImportHelpers.FieldAt<VLCEsFormat, uint>(ref this, Interop.EsFormat.Offsets.Bitrate);

    /// <summary>Codec profile (i_profile)</summary>
    [UnscopedRef]
    public ref int Profile => ref Interop.CImportHelpers.FieldAt<VLCEsFormat, int>(ref this, Interop.EsFormat.Offsets.Profile);

    /// <summary>Codec level (i_level)</summary>
    [UnscopedRef]
    public ref int Level => ref Interop.CImportHelpers.FieldAt<VLCEsFormat, int>(ref this, Interop.EsFormat.Offsets.Level);

    /// <summary>Whether data is packetized (b_packetized)</summary>
    [UnscopedRef]
    public ref byte Packetized => ref Interop.CImportHelpers.FieldAt<VLCEsFormat, byte>(ref this, Interop.EsFormat.Offsets.Packetized);

    /// <summary>Extra data length (i_extra)</summary>
    [UnscopedRef]
    public ref nuint ExtraSize => ref Interop.CImportHelpers.FieldAt<VLCEsFormat, nuint>(ref this, Interop.EsFormat.Offsets.ExtraSize);

    /// <summary>Extra data pointer (p_extra)</summary>
    [UnscopedRef]
    public ref nint Extra => ref Interop.CImportHelpers.FieldAt<VLCEsFormat, nint>(ref this, Interop.EsFormat.Offsets.Extra);
}
