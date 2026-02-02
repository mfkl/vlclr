using System.Runtime.InteropServices;
using VLCLR.Native;
using Xunit;

namespace VLCLR.Tests;

/// <summary>
/// Tests for VLCSubpictureRegion struct layout.
/// Verifies that C# struct definition matches VLC 4.x subpicture_region_t layout.
/// </summary>
public class VLCSubpictureRegionTests
{
    #region Struct Size Tests

    [Fact]
    public void VLCSubpictureRegion_Size_Is224Bytes()
    {
        // subpicture_region_t is 224 bytes on 64-bit
        // video_format_t (152) + pointer (8) + 2 bytes + 2 padding + 4 ints (16) + 4 padding
        // + pointer (8) + 3 ints (12) + 4 padding + 2 pointers (16) = 224 bytes
        Assert.Equal(224, Marshal.SizeOf<VLCSubpictureRegion>());
    }

    [Fact]
    public void VLCListNode_Size_Is16Bytes()
    {
        // vlc_list is 16 bytes on 64-bit (2 pointers)
        Assert.Equal(16, Marshal.SizeOf<VLCListNode>());
    }

    #endregion

    #region VLCSubpictureRegion Field Offset Tests

    [Fact]
    public unsafe void VLCSubpictureRegion_Format_IsAtOffset0()
    {
        VLCSubpictureRegion region = default;
        byte* basePtr = (byte*)&region;
        byte* fieldPtr = (byte*)&region.Format;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpictureRegion_Picture_IsAtOffset152()
    {
        // After VLCVideoFormat (152 bytes)
        VLCSubpictureRegion region = default;
        byte* basePtr = (byte*)&region;
        byte* fieldPtr = (byte*)&region.Picture;
        Assert.Equal(152, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpictureRegion_IsAbsolute_IsAtOffset160()
    {
        VLCSubpictureRegion region = default;
        byte* basePtr = (byte*)&region;
        byte* fieldPtr = (byte*)&region.IsAbsolute;
        Assert.Equal(160, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpictureRegion_IsInWindow_IsAtOffset161()
    {
        VLCSubpictureRegion region = default;
        byte* basePtr = (byte*)&region;
        byte* fieldPtr = (byte*)&region.IsInWindow;
        Assert.Equal(161, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpictureRegion_X_IsAtOffset164()
    {
        VLCSubpictureRegion region = default;
        byte* basePtr = (byte*)&region;
        byte* fieldPtr = (byte*)&region.X;
        Assert.Equal(164, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpictureRegion_Y_IsAtOffset168()
    {
        VLCSubpictureRegion region = default;
        byte* basePtr = (byte*)&region;
        byte* fieldPtr = (byte*)&region.Y;
        Assert.Equal(168, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpictureRegion_Align_IsAtOffset172()
    {
        VLCSubpictureRegion region = default;
        byte* basePtr = (byte*)&region;
        byte* fieldPtr = (byte*)&region.Align;
        Assert.Equal(172, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpictureRegion_Alpha_IsAtOffset176()
    {
        VLCSubpictureRegion region = default;
        byte* basePtr = (byte*)&region;
        byte* fieldPtr = (byte*)&region.Alpha;
        Assert.Equal(176, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpictureRegion_Text_IsAtOffset184()
    {
        // After Alpha (176 + 4 = 180), padded to 8-byte alignment = 184
        VLCSubpictureRegion region = default;
        byte* basePtr = (byte*)&region;
        byte* fieldPtr = (byte*)&region.Text;
        Assert.Equal(184, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpictureRegion_TextFlags_IsAtOffset192()
    {
        VLCSubpictureRegion region = default;
        byte* basePtr = (byte*)&region;
        byte* fieldPtr = (byte*)&region.TextFlags;
        Assert.Equal(192, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpictureRegion_MaxWidth_IsAtOffset196()
    {
        VLCSubpictureRegion region = default;
        byte* basePtr = (byte*)&region;
        byte* fieldPtr = (byte*)&region.MaxWidth;
        Assert.Equal(196, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpictureRegion_MaxHeight_IsAtOffset200()
    {
        VLCSubpictureRegion region = default;
        byte* basePtr = (byte*)&region;
        byte* fieldPtr = (byte*)&region.MaxHeight;
        Assert.Equal(200, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpictureRegion_NodePrev_IsAtOffset208()
    {
        // After MaxHeight (200 + 4 = 204), padded to 8-byte alignment = 208
        VLCSubpictureRegion region = default;
        byte* basePtr = (byte*)&region;
        byte* fieldPtr = (byte*)&region.NodePrev;
        Assert.Equal(208, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpictureRegion_NodeNext_IsAtOffset216()
    {
        VLCSubpictureRegion region = default;
        byte* basePtr = (byte*)&region;
        byte* fieldPtr = (byte*)&region.NodeNext;
        Assert.Equal(216, (int)(fieldPtr - basePtr));
    }

    #endregion

    #region Alignment Flags Tests

    [Fact]
    public void VLCSubpictureAlign_Left_Is1()
    {
        Assert.Equal(0x1, VLCSubpictureAlign.Left);
    }

    [Fact]
    public void VLCSubpictureAlign_Right_Is2()
    {
        Assert.Equal(0x2, VLCSubpictureAlign.Right);
    }

    [Fact]
    public void VLCSubpictureAlign_Top_Is4()
    {
        Assert.Equal(0x4, VLCSubpictureAlign.Top);
    }

    [Fact]
    public void VLCSubpictureAlign_Bottom_Is8()
    {
        Assert.Equal(0x8, VLCSubpictureAlign.Bottom);
    }

    [Fact]
    public void VLCSubpictureAlign_Mask_IsCombinationOfAll()
    {
        Assert.Equal(0xF, VLCSubpictureAlign.Mask);
        Assert.Equal(
            VLCSubpictureAlign.Left | VLCSubpictureAlign.Right | VLCSubpictureAlign.Top | VLCSubpictureAlign.Bottom,
            VLCSubpictureAlign.Mask);
    }

    #endregion

    #region Text Flags Tests

    [Fact]
    public void VLCSubpictureTextFlags_NoRegionBackground_Is16()
    {
        Assert.Equal(1 << 4, VLCSubpictureTextFlags.NoRegionBackground);
    }

    [Fact]
    public void VLCSubpictureTextFlags_GridMode_Is32()
    {
        Assert.Equal(1 << 5, VLCSubpictureTextFlags.GridMode);
    }

    [Fact]
    public void VLCSubpictureTextFlags_TextNotBalanced_Is64()
    {
        Assert.Equal(1 << 6, VLCSubpictureTextFlags.TextNotBalanced);
    }

    [Fact]
    public void VLCSubpictureTextFlags_IsText_Is128()
    {
        Assert.Equal(1 << 7, VLCSubpictureTextFlags.IsText);
    }

    #endregion

    #region Memory Marshaling Tests

    [Fact]
    public unsafe void VLCSubpictureRegion_CanMarshalToNativeMemory()
    {
        VLCSubpictureRegion region = new()
        {
            Picture = unchecked((nint)0x1000),
            IsAbsolute = 1,
            IsInWindow = 0,
            X = 100,
            Y = 200,
            Align = VLCSubpictureAlign.Bottom,
            Alpha = 255,
            Text = unchecked((nint)0x2000),
            TextFlags = VLCSubpictureTextFlags.IsText,
            MaxWidth = 1920,
            MaxHeight = 1080
        };

        nint ptr = Marshal.AllocHGlobal(Marshal.SizeOf<VLCSubpictureRegion>());
        try
        {
            Marshal.StructureToPtr(region, ptr, false);
            VLCSubpictureRegion readBack = Marshal.PtrToStructure<VLCSubpictureRegion>(ptr);

            Assert.Equal(unchecked((nint)0x1000), readBack.Picture);
            Assert.Equal(1, readBack.IsAbsolute);
            Assert.Equal(0, readBack.IsInWindow);
            Assert.Equal(100, readBack.X);
            Assert.Equal(200, readBack.Y);
            Assert.Equal(VLCSubpictureAlign.Bottom, readBack.Align);
            Assert.Equal(255, readBack.Alpha);
            Assert.Equal(unchecked((nint)0x2000), readBack.Text);
            Assert.Equal(VLCSubpictureTextFlags.IsText, readBack.TextFlags);
            Assert.Equal(1920, readBack.MaxWidth);
            Assert.Equal(1080, readBack.MaxHeight);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [Fact]
    public unsafe void VLCSubpictureRegion_FieldsCanBeWrittenAndReadFromNativeMemory()
    {
        nint regionMemory = Marshal.AllocHGlobal(224);
        try
        {
            // Zero the memory
            for (int i = 0; i < 224; i++)
            {
                Marshal.WriteByte(regionMemory, i, 0);
            }

            byte* regionBase = (byte*)regionMemory;

            // Write Picture at offset 152
            *(nint*)(regionBase + 152) = unchecked((nint)0xDEADBEEF);

            // Write IsAbsolute at offset 160
            *(byte*)(regionBase + 160) = 1;

            // Write X at offset 164
            *(int*)(regionBase + 164) = 50;

            // Write Y at offset 168
            *(int*)(regionBase + 168) = 100;

            // Write Align at offset 172
            *(int*)(regionBase + 172) = VLCSubpictureAlign.Bottom;

            // Write Text at offset 184
            *(nint*)(regionBase + 184) = unchecked((nint)0x12345678);

            // Read back using struct access
            ref VLCSubpictureRegion region = ref *(VLCSubpictureRegion*)regionMemory;

            Assert.Equal(unchecked((nint)0xDEADBEEF), region.Picture);
            Assert.Equal(1, region.IsAbsolute);
            Assert.Equal(50, region.X);
            Assert.Equal(100, region.Y);
            Assert.Equal(VLCSubpictureAlign.Bottom, region.Align);
            Assert.Equal(unchecked((nint)0x12345678), region.Text);
        }
        finally
        {
            Marshal.FreeHGlobal(regionMemory);
        }
    }

    [Fact]
    public unsafe void VLCSubpictureRegion_FormatChroma_CanBeAccessed()
    {
        // Verify we can access nested Format.Chroma field
        nint regionMemory = Marshal.AllocHGlobal(224);
        try
        {
            // Zero the memory
            for (int i = 0; i < 224; i++)
            {
                Marshal.WriteByte(regionMemory, i, 0);
            }

            byte* regionBase = (byte*)regionMemory;

            // Write Chroma at offset 0 (start of Format)
            uint rgba = (uint)('R' | ('G' << 8) | ('B' << 16) | ('A' << 24));
            *(uint*)(regionBase + 0) = rgba;

            // Write Width at offset 4
            *(uint*)(regionBase + 4) = 1920;

            // Write Height at offset 8
            *(uint*)(regionBase + 8) = 1080;

            // Read back using struct access
            ref VLCSubpictureRegion region = ref *(VLCSubpictureRegion*)regionMemory;

            Assert.Equal(rgba, region.Format.Chroma);
            Assert.Equal(1920u, region.Format.Width);
            Assert.Equal(1080u, region.Format.Height);
        }
        finally
        {
            Marshal.FreeHGlobal(regionMemory);
        }
    }

    #endregion

    #region VLCListNode Tests

    [Fact]
    public unsafe void VLCListNode_Prev_IsAtOffset0()
    {
        VLCListNode node = default;
        byte* basePtr = (byte*)&node;
        byte* fieldPtr = (byte*)&node.Prev;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCListNode_Next_IsAtOffset8()
    {
        VLCListNode node = default;
        byte* basePtr = (byte*)&node;
        byte* fieldPtr = (byte*)&node.Next;
        Assert.Equal(8, (int)(fieldPtr - basePtr));
    }

    #endregion
}
