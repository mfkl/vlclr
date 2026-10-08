// Text segment parser for VLC subtitle regions
// Extracts text and styling from VLC's text_segment_t linked list

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VLCLR.Native;

namespace VLCLR.Text;

/// <summary>
/// A parsed text segment containing text and its associated style.
/// </summary>
/// <param name="Text">The UTF-8 text content of the segment.</param>
/// <param name="Style">The styling applied to this segment.</param>
public readonly record struct ParsedTextSegment(string Text, TextStyleWrapper Style)
{
    /// <summary>
    /// Returns true if this segment has no text or only whitespace.
    /// </summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);

    /// <inheritdoc/>
    public override string ToString() => $"\"{Text}\" {Style}";
}

/// <summary>
/// Parses VLC subpicture regions to extract text segments and styling.
/// Walks the text_segment_t linked list and converts to managed types.
/// </summary>
public static class TextSegmentParser
{
    /// <summary>
    /// Parses a text segment linked list and returns the combined text.
    /// </summary>
    /// <param name="textPtr">Pointer to first VLCTextSegment in chain.</param>
    /// <returns>Combined text from all segments, or empty string if no text found.</returns>
    public static unsafe string ParseText(nint textPtr)
    {
        if (textPtr == nint.Zero)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder();
        nint segmentPtr = textPtr;

        while (segmentPtr != nint.Zero)
        {
            ref VLCTextSegment segment = ref Unsafe.AsRef<VLCTextSegment>((void*)segmentPtr);

            if (segment.Text != nint.Zero)
            {
                string text = Marshal.PtrToStringUTF8(segment.Text) ?? string.Empty;
                builder.Append(text);
            }

            segmentPtr = segment.Next;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Parses a subpicture region to extract all text segments.
    /// </summary>
    /// <param name="regionPtr">Pointer to VLCSubpictureRegion.</param>
    /// <returns>List of parsed segments, or empty list if no text found.</returns>
    public static unsafe List<ParsedTextSegment> Parse(nint regionPtr)
    {
        var segments = new List<ParsedTextSegment>();

        if (regionPtr == nint.Zero)
        {
            return segments;
        }

        ref VLCSubpictureRegion region = ref Unsafe.AsRef<VLCSubpictureRegion>((void*)regionPtr);

        // Get the first text segment from the region
        nint segmentPtr = region.Text;

        // Walk the linked list of text segments
        while (segmentPtr != nint.Zero)
        {
            ref VLCTextSegment segment = ref Unsafe.AsRef<VLCTextSegment>((void*)segmentPtr);

            // Extract text string
            string text = string.Empty;
            if (segment.Text != nint.Zero)
            {
                text = Marshal.PtrToStringUTF8(segment.Text) ?? string.Empty;
            }

            // Extract style
            TextStyleWrapper style = TextStyleWrapper.FromNative(segment.Style);

            // Add segment if it has text
            if (!string.IsNullOrEmpty(text))
            {
                segments.Add(new ParsedTextSegment(text, style));
            }

            // Move to next segment in chain
            segmentPtr = segment.Next;
        }

        return segments;
    }

    /// <summary>
    /// Parses a subpicture region with visibility-optimized styling.
    /// </summary>
    /// <param name="regionPtr">Pointer to VLCSubpictureRegion.</param>
    /// <param name="forceWhiteText">If true, forces white text when VLC sends black.</param>
    /// <param name="forceOutline">If true, always enables outline for visibility.</param>
    /// <param name="outlineWidth">Outline width when forced (default: 3).</param>
    /// <returns>List of parsed segments with visibility-optimized styling.</returns>
    public static unsafe List<ParsedTextSegment> ParseWithVisibility(
        nint regionPtr, 
        bool forceWhiteText = true, 
        bool forceOutline = true, 
        int outlineWidth = 3)
    {
        var segments = new List<ParsedTextSegment>();

        if (regionPtr == nint.Zero)
        {
            return segments;
        }

        ref VLCSubpictureRegion region = ref Unsafe.AsRef<VLCSubpictureRegion>((void*)regionPtr);

        // Get the first text segment from the region
        nint segmentPtr = region.Text;

        // Walk the linked list of text segments
        while (segmentPtr != nint.Zero)
        {
            ref VLCTextSegment segment = ref Unsafe.AsRef<VLCTextSegment>((void*)segmentPtr);

            // Extract text string
            string text = string.Empty;
            if (segment.Text != nint.Zero)
            {
                text = Marshal.PtrToStringUTF8(segment.Text) ?? string.Empty;
            }

            // Extract style with visibility optimizations
            TextStyleWrapper style = TextStyleWrapper.FromNativeWithVisibility(
                segment.Style, forceWhiteText, forceOutline, outlineWidth);

            // Add segment if it has text
            if (!string.IsNullOrEmpty(text))
            {
                segments.Add(new ParsedTextSegment(text, style));
            }

            // Move to next segment in chain
            segmentPtr = segment.Next;
        }

        return segments;
    }

    /// <summary>
    /// Gets the combined text from all segments, useful for debugging.
    /// </summary>
    /// <param name="segments">List of parsed segments.</param>
    /// <returns>Concatenated text from all segments.</returns>
    public static string GetCombinedText(IReadOnlyList<ParsedTextSegment> segments)
    {
        if (segments.Count == 0)
        {
            return string.Empty;
        }

        if (segments.Count == 1)
        {
            return segments[0].Text;
        }

        var builder = new System.Text.StringBuilder();
        foreach (var segment in segments)
        {
            builder.Append(segment.Text);
        }
        return builder.ToString();
    }

    /// <summary>
    /// Parses region and returns a summary string for logging.
    /// </summary>
    /// <param name="regionPtr">Pointer to VLCSubpictureRegion.</param>
    /// <returns>Summary string describing the parsed content.</returns>
    public static string ParseAndDescribe(nint regionPtr)
    {
        var segments = Parse(regionPtr);

        if (segments.Count == 0)
        {
            return "[no text]";
        }

        string text = GetCombinedText(segments);
        // Truncate long text for logging
        if (text.Length > 50)
        {
            text = text[..47] + "...";
        }

        // Escape control characters for logging
        text = text.Replace("\n", "\\n").Replace("\r", "\\r");

        return $"[{segments.Count} segment(s): \"{text}\"]";
    }
}
