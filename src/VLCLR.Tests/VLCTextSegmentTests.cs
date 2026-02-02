using System.Runtime.InteropServices;
using VLCLR.Native;
using Xunit;

namespace VLCLR.Tests;

/// <summary>
/// Tests for VLCTextSegment and VLCTextSegmentRuby struct layouts.
/// Verifies that C# struct definitions match VLC 4.x text_segment_t layout.
/// </summary>
public class VLCTextSegmentTests
{
    #region Struct Size Tests

    [Fact]
    public void VLCTextSegment_Size_Is32Bytes()
    {
        // text_segment_t is 32 bytes on 64-bit (4 pointers)
        Assert.Equal(32, Marshal.SizeOf<VLCTextSegment>());
    }

    [Fact]
    public void VLCTextSegmentRuby_Size_Is24Bytes()
    {
        // text_segment_ruby_t is 24 bytes on 64-bit (3 pointers)
        Assert.Equal(24, Marshal.SizeOf<VLCTextSegmentRuby>());
    }

    #endregion

    #region VLCTextSegment Field Offset Tests

    [Fact]
    public unsafe void VLCTextSegment_Text_IsAtOffset0()
    {
        VLCTextSegment segment = default;
        byte* basePtr = (byte*)&segment;
        byte* fieldPtr = (byte*)&segment.Text;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCTextSegment_Style_IsAtOffset8()
    {
        VLCTextSegment segment = default;
        byte* basePtr = (byte*)&segment;
        byte* fieldPtr = (byte*)&segment.Style;
        Assert.Equal(8, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCTextSegment_Next_IsAtOffset16()
    {
        VLCTextSegment segment = default;
        byte* basePtr = (byte*)&segment;
        byte* fieldPtr = (byte*)&segment.Next;
        Assert.Equal(16, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCTextSegment_Ruby_IsAtOffset24()
    {
        VLCTextSegment segment = default;
        byte* basePtr = (byte*)&segment;
        byte* fieldPtr = (byte*)&segment.Ruby;
        Assert.Equal(24, (int)(fieldPtr - basePtr));
    }

    #endregion

    #region VLCTextSegmentRuby Field Offset Tests

    [Fact]
    public unsafe void VLCTextSegmentRuby_BaseText_IsAtOffset0()
    {
        VLCTextSegmentRuby ruby = default;
        byte* basePtr = (byte*)&ruby;
        byte* fieldPtr = (byte*)&ruby.BaseText;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCTextSegmentRuby_RubyText_IsAtOffset8()
    {
        VLCTextSegmentRuby ruby = default;
        byte* basePtr = (byte*)&ruby;
        byte* fieldPtr = (byte*)&ruby.RubyText;
        Assert.Equal(8, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCTextSegmentRuby_Next_IsAtOffset16()
    {
        VLCTextSegmentRuby ruby = default;
        byte* basePtr = (byte*)&ruby;
        byte* fieldPtr = (byte*)&ruby.Next;
        Assert.Equal(16, (int)(fieldPtr - basePtr));
    }

    #endregion

    #region Memory Marshaling Tests

    [Fact]
    public unsafe void VLCTextSegment_CanMarshalToNativeMemory()
    {
        VLCTextSegment segment = new()
        {
            Text = unchecked((nint)0x1000),
            Style = unchecked((nint)0x2000),
            Next = unchecked((nint)0x3000),
            Ruby = unchecked((nint)0x4000)
        };

        nint ptr = Marshal.AllocHGlobal(Marshal.SizeOf<VLCTextSegment>());
        try
        {
            Marshal.StructureToPtr(segment, ptr, false);
            VLCTextSegment readBack = Marshal.PtrToStructure<VLCTextSegment>(ptr);

            Assert.Equal(unchecked((nint)0x1000), readBack.Text);
            Assert.Equal(unchecked((nint)0x2000), readBack.Style);
            Assert.Equal(unchecked((nint)0x3000), readBack.Next);
            Assert.Equal(unchecked((nint)0x4000), readBack.Ruby);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [Fact]
    public unsafe void VLCTextSegment_LinkedListCanBeTraversed()
    {
        // Simulate a linked list of 3 segments
        nint segment1Memory = Marshal.AllocHGlobal(32);
        nint segment2Memory = Marshal.AllocHGlobal(32);
        nint segment3Memory = Marshal.AllocHGlobal(32);

        try
        {
            // Set up segment 1 -> segment 2 -> segment 3 -> null
            ref VLCTextSegment segment1 = ref *(VLCTextSegment*)segment1Memory;
            ref VLCTextSegment segment2 = ref *(VLCTextSegment*)segment2Memory;
            ref VLCTextSegment segment3 = ref *(VLCTextSegment*)segment3Memory;

            segment1.Text = unchecked((nint)0x1001);
            segment1.Next = segment2Memory;

            segment2.Text = unchecked((nint)0x1002);
            segment2.Next = segment3Memory;

            segment3.Text = unchecked((nint)0x1003);
            segment3.Next = nint.Zero;

            // Traverse the list
            int count = 0;
            nint current = segment1Memory;
            while (current != nint.Zero)
            {
                ref VLCTextSegment seg = ref *(VLCTextSegment*)current;
                count++;
                Assert.Equal(unchecked((nint)(0x1000 + count)), seg.Text);
                current = seg.Next;
            }

            Assert.Equal(3, count);
        }
        finally
        {
            Marshal.FreeHGlobal(segment1Memory);
            Marshal.FreeHGlobal(segment2Memory);
            Marshal.FreeHGlobal(segment3Memory);
        }
    }

    [Fact]
    public unsafe void VLCTextSegmentRuby_CanMarshalToNativeMemory()
    {
        VLCTextSegmentRuby ruby = new()
        {
            BaseText = unchecked((nint)0x5000),
            RubyText = unchecked((nint)0x6000),
            Next = unchecked((nint)0x7000)
        };

        nint ptr = Marshal.AllocHGlobal(Marshal.SizeOf<VLCTextSegmentRuby>());
        try
        {
            Marshal.StructureToPtr(ruby, ptr, false);
            VLCTextSegmentRuby readBack = Marshal.PtrToStructure<VLCTextSegmentRuby>(ptr);

            Assert.Equal(unchecked((nint)0x5000), readBack.BaseText);
            Assert.Equal(unchecked((nint)0x6000), readBack.RubyText);
            Assert.Equal(unchecked((nint)0x7000), readBack.Next);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [Fact]
    public unsafe void VLCTextSegment_FieldsCanBeWrittenAndReadFromNativeMemory()
    {
        nint segmentMemory = Marshal.AllocHGlobal(32);
        try
        {
            // Zero the memory
            for (int i = 0; i < 32; i++)
            {
                Marshal.WriteByte(segmentMemory, i, 0);
            }

            byte* segBase = (byte*)segmentMemory;

            // Write text pointer at offset 0
            *(nint*)(segBase + 0) = unchecked((nint)0xABCD1234);

            // Write style pointer at offset 8
            *(nint*)(segBase + 8) = unchecked((nint)0xDEADBEEF);

            // Write next pointer at offset 16
            *(nint*)(segBase + 16) = unchecked((nint)0x12345678);

            // Write ruby pointer at offset 24
            *(nint*)(segBase + 24) = unchecked((nint)0x87654321);

            // Read back using struct access
            ref VLCTextSegment segment = ref *(VLCTextSegment*)segmentMemory;

            Assert.Equal(unchecked((nint)0xABCD1234), segment.Text);
            Assert.Equal(unchecked((nint)0xDEADBEEF), segment.Style);
            Assert.Equal(unchecked((nint)0x12345678), segment.Next);
            Assert.Equal(unchecked((nint)0x87654321), segment.Ruby);
        }
        finally
        {
            Marshal.FreeHGlobal(segmentMemory);
        }
    }

    #endregion
}
