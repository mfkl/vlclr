// VLC text segment structure
// Source: vlc/include/vlc_text_style.h
// VLC Version: 4.0.6

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Text segment for subtitles (text_segment_t from vlc_text_style.h).
/// A text segment represents a portion of text with a single style.
/// Multiple segments are chained via p_next to represent styled text.
/// </summary>
/// <remarks>
/// Offsets and size come from the generated <c>Interop.TextSegment</c> layout:
/// - psz_text: pointer to UTF-8 text string
/// - style: pointer to text_style_t
/// - p_next: pointer to next segment in chain
/// - p_ruby: pointer to ruby annotations (for CJK text)
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = Interop.TextSegment.MaxSizeOf)]
public struct VLCTextSegment
{
    /// <summary>UTF-8 text string (psz_text)</summary>
    [FieldOffset(Interop.TextSegment.Offsets.Text)]
    public nint Text;

    /// <summary>Style applied to this segment (style), pointer to text_style_t</summary>
    [FieldOffset(Interop.TextSegment.Offsets.Style)]
    public nint Style;

    /// <summary>Next segment in chain (p_next)</summary>
    [FieldOffset(Interop.TextSegment.Offsets.Next)]
    public nint Next;

    /// <summary>Ruby annotations for CJK text (p_ruby)</summary>
    [FieldOffset(Interop.TextSegment.Offsets.Ruby)]
    public nint Ruby;
}

/// <summary>
/// Ruby annotation for text segments (text_segment_ruby_t from vlc_text_style.h).
/// Used for pronunciation guides above/below CJK characters.
/// </summary>
/// <remarks>
/// Offsets and size come from the generated <c>Interop.TextSegmentRuby</c> layout:
/// - psz_base: pointer to base text being annotated
/// - psz_rt: pointer to ruby text (pronunciation)
/// - p_next: pointer to next ruby in chain
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = Interop.TextSegmentRuby.MaxSizeOf)]
public struct VLCTextSegmentRuby
{
    /// <summary>Base text being annotated (psz_base)</summary>
    [FieldOffset(Interop.TextSegmentRuby.Offsets.Base)]
    public nint BaseText;

    /// <summary>Ruby/pronunciation text (psz_rt)</summary>
    [FieldOffset(Interop.TextSegmentRuby.Offsets.Rt)]
    public nint RubyText;

    /// <summary>Next ruby annotation in chain (p_next)</summary>
    [FieldOffset(Interop.TextSegmentRuby.Offsets.Next)]
    public nint Next;
}
