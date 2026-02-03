using System.Runtime.InteropServices;
using System.Text;
using VLCLR.Native;
using VLCLR.Text;
using Xunit;

namespace VLCLR.Tests;

/// <summary>
/// Tests for TextSegmentParser class.
/// Verifies parsing of VLC text_segment_t linked lists.
/// </summary>
public class TextSegmentParserTests
{
    #region Parse Tests - Null/Empty Input

    [Fact]
    public void Parse_WithZeroPointer_ReturnsEmptyList()
    {
        var segments = TextSegmentParser.Parse(nint.Zero);

        Assert.Empty(segments);
    }

    [Fact]
    public unsafe void Parse_WithNoTextSegment_ReturnsEmptyList()
    {
        // Create a region with null Text pointer
        nint regionPtr = Marshal.AllocHGlobal(Marshal.SizeOf<VLCSubpictureRegion>());
        try
        {
            // Zero the region
            for (int i = 0; i < Marshal.SizeOf<VLCSubpictureRegion>(); i++)
            {
                Marshal.WriteByte(regionPtr, i, 0);
            }

            var segments = TextSegmentParser.Parse(regionPtr);

            Assert.Empty(segments);
        }
        finally
        {
            Marshal.FreeHGlobal(regionPtr);
        }
    }

    #endregion

    #region Parse Tests - Single Segment

    [Fact]
    public unsafe void Parse_SingleSegment_ExtractsText()
    {
        using var fixture = new TextSegmentFixture();
        fixture.AddSegment("Hello World");

        var segments = TextSegmentParser.Parse(fixture.RegionPtr);

        Assert.Single(segments);
        Assert.Equal("Hello World", segments[0].Text);
    }

    [Fact]
    public unsafe void Parse_SingleSegmentWithStyle_ExtractsStyle()
    {
        using var fixture = new TextSegmentFixture();
        fixture.AddSegment("Styled Text", fontColor: 0xFF0000, bold: true);

        var segments = TextSegmentParser.Parse(fixture.RegionPtr);

        Assert.Single(segments);
        Assert.Equal("Styled Text", segments[0].Text);
        Assert.Equal(0xFF0000u, segments[0].Style.ForegroundColor);
        Assert.True(segments[0].Style.IsBold);
    }

    [Fact]
    public unsafe void Parse_EmptyTextSegment_SkipsSegment()
    {
        using var fixture = new TextSegmentFixture();
        fixture.AddSegment(""); // Empty text

        var segments = TextSegmentParser.Parse(fixture.RegionPtr);

        Assert.Empty(segments); // Empty segments are skipped
    }

    #endregion

    #region Parse Tests - Multiple Segments

    [Fact]
    public unsafe void Parse_MultipleSegments_ExtractsAll()
    {
        using var fixture = new TextSegmentFixture();
        fixture.AddSegment("First");
        fixture.AddSegment("Second");
        fixture.AddSegment("Third");

        var segments = TextSegmentParser.Parse(fixture.RegionPtr);

        Assert.Equal(3, segments.Count);
        Assert.Equal("First", segments[0].Text);
        Assert.Equal("Second", segments[1].Text);
        Assert.Equal("Third", segments[2].Text);
    }

    [Fact]
    public unsafe void Parse_MultipleSegments_DifferentStyles()
    {
        using var fixture = new TextSegmentFixture();
        fixture.AddSegment("Bold", bold: true);
        fixture.AddSegment("Italic", italic: true);
        fixture.AddSegment("BoldItalic", bold: true, italic: true);

        var segments = TextSegmentParser.Parse(fixture.RegionPtr);

        Assert.Equal(3, segments.Count);
        Assert.True(segments[0].Style.IsBold);
        Assert.False(segments[0].Style.IsItalic);
        Assert.False(segments[1].Style.IsBold);
        Assert.True(segments[1].Style.IsItalic);
        Assert.True(segments[2].Style.IsBold);
        Assert.True(segments[2].Style.IsItalic);
    }

    #endregion

    #region ParseWithVisibility Tests

    [Fact]
    public void ParseWithVisibility_WithZeroPointer_ReturnsEmptyList()
    {
        var segments = TextSegmentParser.ParseWithVisibility(nint.Zero);

        Assert.Empty(segments);
    }

    [Fact]
    public unsafe void ParseWithVisibility_ForcesWhiteText()
    {
        using var fixture = new TextSegmentFixture();
        fixture.AddSegment("Black text", fontColor: 0x000000); // Black

        var segments = TextSegmentParser.ParseWithVisibility(fixture.RegionPtr, forceWhiteText: true);

        Assert.Single(segments);
        Assert.Equal(0xFFFFFFu, segments[0].Style.ForegroundColor); // Forced to white
    }

    [Fact]
    public unsafe void ParseWithVisibility_ForcesOutline()
    {
        using var fixture = new TextSegmentFixture();
        fixture.AddSegment("No outline");

        var segments = TextSegmentParser.ParseWithVisibility(fixture.RegionPtr, forceOutline: true, outlineWidth: 5);

        Assert.Single(segments);
        Assert.True(segments[0].Style.HasOutline);
        Assert.Equal(5, segments[0].Style.OutlineWidth);
    }

    #endregion

    #region GetCombinedText Tests

    [Fact]
    public void GetCombinedText_EmptyList_ReturnsEmptyString()
    {
        var result = TextSegmentParser.GetCombinedText(new List<ParsedTextSegment>());

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetCombinedText_SingleSegment_ReturnsText()
    {
        var segments = new List<ParsedTextSegment>
        {
            new("Hello", new TextStyleWrapper())
        };

        var result = TextSegmentParser.GetCombinedText(segments);

        Assert.Equal("Hello", result);
    }

    [Fact]
    public void GetCombinedText_MultipleSegments_ConcatenatesText()
    {
        var segments = new List<ParsedTextSegment>
        {
            new("Hello ", new TextStyleWrapper()),
            new("World", new TextStyleWrapper()),
            new("!", new TextStyleWrapper())
        };

        var result = TextSegmentParser.GetCombinedText(segments);

        Assert.Equal("Hello World!", result);
    }

    #endregion

    #region ParseAndDescribe Tests

    [Fact]
    public void ParseAndDescribe_WithZeroPointer_ReturnsNoText()
    {
        var result = TextSegmentParser.ParseAndDescribe(nint.Zero);

        Assert.Equal("[no text]", result);
    }

    [Fact]
    public unsafe void ParseAndDescribe_ShortText_ReturnsFullText()
    {
        using var fixture = new TextSegmentFixture();
        fixture.AddSegment("Short text");

        var result = TextSegmentParser.ParseAndDescribe(fixture.RegionPtr);

        Assert.Contains("Short text", result);
        Assert.Contains("1 segment", result);
    }

    [Fact]
    public unsafe void ParseAndDescribe_LongText_TruncatesText()
    {
        using var fixture = new TextSegmentFixture();
        fixture.AddSegment(new string('A', 100)); // Long text

        var result = TextSegmentParser.ParseAndDescribe(fixture.RegionPtr);

        Assert.Contains("...", result); // Truncated
        Assert.True(result.Length < 100);
    }

    [Fact]
    public unsafe void ParseAndDescribe_EscapesNewlines()
    {
        using var fixture = new TextSegmentFixture();
        fixture.AddSegment("Line1\nLine2\rLine3");

        var result = TextSegmentParser.ParseAndDescribe(fixture.RegionPtr);

        Assert.Contains("\\n", result);
        Assert.Contains("\\r", result);
    }

    #endregion

    #region ParsedTextSegment Tests

    [Fact]
    public void ParsedTextSegment_IsEmpty_TrueForEmpty()
    {
        var segment = new ParsedTextSegment("", new TextStyleWrapper());

        Assert.True(segment.IsEmpty);
    }

    [Fact]
    public void ParsedTextSegment_IsEmpty_TrueForWhitespace()
    {
        var segment = new ParsedTextSegment("   ", new TextStyleWrapper());

        Assert.True(segment.IsEmpty);
    }

    [Fact]
    public void ParsedTextSegment_IsEmpty_FalseForContent()
    {
        var segment = new ParsedTextSegment("Hello", new TextStyleWrapper());

        Assert.False(segment.IsEmpty);
    }

    [Fact]
    public void ParsedTextSegment_ToString_ContainsTextAndStyle()
    {
        var style = new TextStyleWrapper { FontName = "Arial", FontSize = 24 };
        var segment = new ParsedTextSegment("Test", style);

        var result = segment.ToString();

        Assert.Contains("Test", result);
    }

    #endregion

    #region Test Fixture

    /// <summary>
    /// Helper class to create VLC text segment structures in memory for testing.
    /// </summary>
    private sealed unsafe class TextSegmentFixture : IDisposable
    {
        private readonly List<nint> _allocations = new();
        private nint _regionPtr;
        private nint _lastSegmentPtr;

        public nint RegionPtr
        {
            get
            {
                if (_regionPtr == nint.Zero)
                {
                    _regionPtr = AllocateZeroed<VLCSubpictureRegion>();
                }
                return _regionPtr;
            }
        }

        public void AddSegment(string text, uint fontColor = 0xFFFFFF, bool bold = false, bool italic = false)
        {
            // Allocate text string
            byte[] textBytes = Encoding.UTF8.GetBytes(text + '\0');
            nint textPtr = Marshal.AllocHGlobal(textBytes.Length);
            _allocations.Add(textPtr);
            Marshal.Copy(textBytes, 0, textPtr, textBytes.Length);

            // Allocate style
            nint stylePtr = AllocateZeroed<VLCTextStyle>();
            ref VLCTextStyle style = ref *(VLCTextStyle*)stylePtr;
            style.FontSize = 24;
            style.FontColor = fontColor;
            style.FontAlpha = 255;
            ushort flags = 0;
            if (bold) flags |= VLCTextStyleFlags.Bold;
            if (italic) flags |= VLCTextStyleFlags.Italic;
            style.StyleFlags = flags;

            // Allocate segment
            nint segmentPtr = AllocateZeroed<VLCTextSegment>();
            ref VLCTextSegment segment = ref *(VLCTextSegment*)segmentPtr;
            segment.Text = textPtr;
            segment.Style = stylePtr;
            segment.Next = nint.Zero;

            // Link to list
            if (_lastSegmentPtr == nint.Zero)
            {
                // First segment - link from region
                ref VLCSubpictureRegion region = ref *(VLCSubpictureRegion*)RegionPtr;
                region.Text = segmentPtr;
            }
            else
            {
                // Chain from previous segment
                ref VLCTextSegment prev = ref *(VLCTextSegment*)_lastSegmentPtr;
                prev.Next = segmentPtr;
            }

            _lastSegmentPtr = segmentPtr;
        }

        private nint AllocateZeroed<T>() where T : unmanaged
        {
            int size = Marshal.SizeOf<T>();
            nint ptr = Marshal.AllocHGlobal(size);
            _allocations.Add(ptr);

            for (int i = 0; i < size; i++)
            {
                Marshal.WriteByte(ptr, i, 0);
            }

            return ptr;
        }

        public void Dispose()
        {
            foreach (var ptr in _allocations)
            {
                Marshal.FreeHGlobal(ptr);
            }
            _allocations.Clear();
            _regionPtr = nint.Zero;
            _lastSegmentPtr = nint.Zero;
        }
    }

    #endregion
}
