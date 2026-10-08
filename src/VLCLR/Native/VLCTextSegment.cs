// VLC text segment structure
// Source: vlc-3.0.24-beta1/sdk/include/vlc/plugins/vlc_text_style.h
// VLC Version: 3.0.24-beta1

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Text segment for subtitles (text_segment_t from vlc_text_style.h).
/// Size on 64-bit: 24 bytes.
///
/// VLC 3 has only: psz_text, style, p_next. No Ruby fields (VLC 4 only).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
public struct VLCTextSegment
{
    /// <summary>UTF-8 text string (psz_text)</summary>
    [FieldOffset(0)]
    public nint Text;

    /// <summary>Style applied to this segment (style), pointer to text_style_t</summary>
    [FieldOffset(8)]
    public nint Style;

    /// <summary>Next segment in chain (p_next)</summary>
    [FieldOffset(16)]
    public nint Next;
}
