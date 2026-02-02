using System.Runtime.InteropServices;
using VLCLR.Native;
using Xunit;

namespace VLCLR.Tests;

/// <summary>
/// Tests for VLCTextStyle struct layout.
/// Verifies that C# struct definition matches VLC 4.x text_style_t layout.
/// </summary>
public class VLCTextStyleTests
{
    #region Struct Size Tests

    [Fact]
    public void VLCTextStyle_Size_Is80Bytes()
    {
        // text_style_t is 80 bytes on 64-bit
        // 2 pointers (16) + 2 uint16 (4) + float (4) + int (4) + uint (4) + byte (1) + padding (3)
        // + int (4) + uint (4) + byte (1) + padding (3) + int (4) + uint (4) + byte (1) + padding (3)
        // + int (4) + uint (4) + byte (1) + padding (3) + enum/int (4) + padding (4) = 80 bytes
        Assert.Equal(80, Marshal.SizeOf<VLCTextStyle>());
    }

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

    #region VLCTextStyle Field Offset Tests

    [Fact]
    public unsafe void VLCTextStyle_FontName_IsAtOffset0()
    {
        VLCTextStyle style = default;
        byte* basePtr = (byte*)&style;
        byte* fieldPtr = (byte*)&style.FontName;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCTextStyle_MonoFontName_IsAtOffset8()
    {
        VLCTextStyle style = default;
        byte* basePtr = (byte*)&style;
        byte* fieldPtr = (byte*)&style.MonoFontName;
        Assert.Equal(8, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCTextStyle_Features_IsAtOffset16()
    {
        VLCTextStyle style = default;
        byte* basePtr = (byte*)&style;
        byte* fieldPtr = (byte*)&style.Features;
        Assert.Equal(16, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCTextStyle_StyleFlags_IsAtOffset18()
    {
        VLCTextStyle style = default;
        byte* basePtr = (byte*)&style;
        byte* fieldPtr = (byte*)&style.StyleFlags;
        Assert.Equal(18, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCTextStyle_FontRelativeSize_IsAtOffset20()
    {
        VLCTextStyle style = default;
        byte* basePtr = (byte*)&style;
        byte* fieldPtr = (byte*)&style.FontRelativeSize;
        Assert.Equal(20, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCTextStyle_FontSize_IsAtOffset24()
    {
        VLCTextStyle style = default;
        byte* basePtr = (byte*)&style;
        byte* fieldPtr = (byte*)&style.FontSize;
        Assert.Equal(24, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCTextStyle_FontColor_IsAtOffset28()
    {
        VLCTextStyle style = default;
        byte* basePtr = (byte*)&style;
        byte* fieldPtr = (byte*)&style.FontColor;
        Assert.Equal(28, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCTextStyle_FontAlpha_IsAtOffset32()
    {
        VLCTextStyle style = default;
        byte* basePtr = (byte*)&style;
        byte* fieldPtr = (byte*)&style.FontAlpha;
        Assert.Equal(32, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCTextStyle_Spacing_IsAtOffset36()
    {
        VLCTextStyle style = default;
        byte* basePtr = (byte*)&style;
        byte* fieldPtr = (byte*)&style.Spacing;
        Assert.Equal(36, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCTextStyle_OutlineColor_IsAtOffset40()
    {
        VLCTextStyle style = default;
        byte* basePtr = (byte*)&style;
        byte* fieldPtr = (byte*)&style.OutlineColor;
        Assert.Equal(40, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCTextStyle_OutlineAlpha_IsAtOffset44()
    {
        VLCTextStyle style = default;
        byte* basePtr = (byte*)&style;
        byte* fieldPtr = (byte*)&style.OutlineAlpha;
        Assert.Equal(44, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCTextStyle_OutlineWidth_IsAtOffset48()
    {
        VLCTextStyle style = default;
        byte* basePtr = (byte*)&style;
        byte* fieldPtr = (byte*)&style.OutlineWidth;
        Assert.Equal(48, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCTextStyle_ShadowColor_IsAtOffset52()
    {
        VLCTextStyle style = default;
        byte* basePtr = (byte*)&style;
        byte* fieldPtr = (byte*)&style.ShadowColor;
        Assert.Equal(52, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCTextStyle_ShadowAlpha_IsAtOffset56()
    {
        VLCTextStyle style = default;
        byte* basePtr = (byte*)&style;
        byte* fieldPtr = (byte*)&style.ShadowAlpha;
        Assert.Equal(56, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCTextStyle_ShadowWidth_IsAtOffset60()
    {
        VLCTextStyle style = default;
        byte* basePtr = (byte*)&style;
        byte* fieldPtr = (byte*)&style.ShadowWidth;
        Assert.Equal(60, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCTextStyle_BackgroundColor_IsAtOffset64()
    {
        VLCTextStyle style = default;
        byte* basePtr = (byte*)&style;
        byte* fieldPtr = (byte*)&style.BackgroundColor;
        Assert.Equal(64, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCTextStyle_BackgroundAlpha_IsAtOffset68()
    {
        VLCTextStyle style = default;
        byte* basePtr = (byte*)&style;
        byte* fieldPtr = (byte*)&style.BackgroundAlpha;
        Assert.Equal(68, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCTextStyle_WrapMode_IsAtOffset72()
    {
        VLCTextStyle style = default;
        byte* basePtr = (byte*)&style;
        byte* fieldPtr = (byte*)&style.WrapMode;
        Assert.Equal(72, (int)(fieldPtr - basePtr));
    }

    #endregion

    #region Style Flags Tests

    [Fact]
    public void VLCTextStyleFlags_Bold_Is1()
    {
        Assert.Equal(0x0001, VLCTextStyleFlags.Bold);
    }

    [Fact]
    public void VLCTextStyleFlags_Italic_Is2()
    {
        Assert.Equal(0x0002, VLCTextStyleFlags.Italic);
    }

    [Fact]
    public void VLCTextStyleFlags_Outline_Is4()
    {
        Assert.Equal(0x0004, VLCTextStyleFlags.Outline);
    }

    [Fact]
    public void VLCTextStyleFlags_Shadow_Is8()
    {
        Assert.Equal(0x0008, VLCTextStyleFlags.Shadow);
    }

    [Fact]
    public void VLCTextStyleFlags_Background_Is16()
    {
        Assert.Equal(0x0010, VLCTextStyleFlags.Background);
    }

    [Fact]
    public void VLCTextStyleFlags_Underline_Is32()
    {
        Assert.Equal(0x0020, VLCTextStyleFlags.Underline);
    }

    [Fact]
    public void VLCTextStyleFlags_Strikeout_Is64()
    {
        Assert.Equal(0x0040, VLCTextStyleFlags.Strikeout);
    }

    #endregion

    #region Feature Flags Tests

    [Fact]
    public void VLCTextStyleFeatures_NoDefaults_Is0()
    {
        Assert.Equal(0, VLCTextStyleFeatures.NoDefaults);
    }

    [Fact]
    public void VLCTextStyleFeatures_FullySet_IsFFFF()
    {
        Assert.Equal(0xFFFF, VLCTextStyleFeatures.FullySet);
    }

    [Fact]
    public void VLCTextStyleFeatures_HasFontColor_Is1()
    {
        Assert.Equal(0x0001, VLCTextStyleFeatures.HasFontColor);
    }

    [Fact]
    public void VLCTextStyleFeatures_HasFontAlpha_Is2()
    {
        Assert.Equal(0x0002, VLCTextStyleFeatures.HasFontAlpha);
    }

    [Fact]
    public void VLCTextStyleFeatures_HasFlags_Is4()
    {
        Assert.Equal(0x0004, VLCTextStyleFeatures.HasFlags);
    }

    #endregion

    #region Alpha Constants Tests

    [Fact]
    public void VLCTextStyleAlpha_Opaque_Is255()
    {
        Assert.Equal(0xFF, VLCTextStyleAlpha.Opaque);
    }

    [Fact]
    public void VLCTextStyleAlpha_Transparent_Is0()
    {
        Assert.Equal(0x00, VLCTextStyleAlpha.Transparent);
    }

    #endregion

    #region Default Values Tests

    [Fact]
    public void VLCTextStyleDefaults_FontSize_Is20()
    {
        Assert.Equal(20, VLCTextStyleDefaults.FontSize);
    }

    [Fact]
    public void VLCTextStyleDefaults_RelativeFontSize_Is6_25()
    {
        Assert.Equal(6.25f, VLCTextStyleDefaults.RelativeFontSize);
    }

    #endregion

    #region WrapMode Enum Tests

    [Fact]
    public void VLCTextWrapMode_Default_Is0()
    {
        Assert.Equal(0, (int)VLCTextWrapMode.Default);
    }

    [Fact]
    public void VLCTextWrapMode_Character_Is1()
    {
        Assert.Equal(1, (int)VLCTextWrapMode.Character);
    }

    [Fact]
    public void VLCTextWrapMode_None_Is2()
    {
        Assert.Equal(2, (int)VLCTextWrapMode.None);
    }

    #endregion

    #region Memory Marshaling Tests

    [Fact]
    public unsafe void VLCTextStyle_CanMarshalToNativeMemory()
    {
        VLCTextStyle style = new()
        {
            FontSize = 24,
            FontColor = 0x00FFFFFF,  // White
            FontAlpha = 0xFF,  // Opaque
            StyleFlags = VLCTextStyleFlags.Bold | VLCTextStyleFlags.Shadow,
            Features = VLCTextStyleFeatures.HasFontColor | VLCTextStyleFeatures.HasFontAlpha | VLCTextStyleFeatures.HasFlags,
            OutlineColor = 0x00000000,  // Black
            OutlineWidth = 2,
            ShadowColor = 0x00000000,  // Black
            ShadowWidth = 2,
            WrapMode = VLCTextWrapMode.Default
        };

        nint ptr = Marshal.AllocHGlobal(Marshal.SizeOf<VLCTextStyle>());
        try
        {
            Marshal.StructureToPtr(style, ptr, false);
            VLCTextStyle readBack = Marshal.PtrToStructure<VLCTextStyle>(ptr);

            Assert.Equal(24, readBack.FontSize);
            Assert.Equal(0x00FFFFFFu, readBack.FontColor);
            Assert.Equal(0xFF, readBack.FontAlpha);
            Assert.Equal(VLCTextStyleFlags.Bold | VLCTextStyleFlags.Shadow, readBack.StyleFlags);
            Assert.Equal(0x00000000u, readBack.OutlineColor);
            Assert.Equal(2, readBack.OutlineWidth);
            Assert.Equal(VLCTextWrapMode.Default, readBack.WrapMode);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [Fact]
    public unsafe void VLCTextStyle_FieldsCanBeWrittenAndReadFromNativeMemory()
    {
        nint styleMemory = Marshal.AllocHGlobal(80);
        try
        {
            // Zero the memory
            for (int i = 0; i < 80; i++)
            {
                Marshal.WriteByte(styleMemory, i, 0);
            }

            byte* styleBase = (byte*)styleMemory;

            // Write font size at offset 24
            *(int*)(styleBase + 24) = 32;

            // Write font color at offset 28
            *(uint*)(styleBase + 28) = 0x00FF0000;  // Red

            // Write font alpha at offset 32
            *(byte*)(styleBase + 32) = 0xFF;

            // Write style flags at offset 18
            *(ushort*)(styleBase + 18) = VLCTextStyleFlags.Bold | VLCTextStyleFlags.Italic;

            // Read back using struct access
            ref VLCTextStyle style = ref *(VLCTextStyle*)styleMemory;

            Assert.Equal(32, style.FontSize);
            Assert.Equal(0x00FF0000u, style.FontColor);
            Assert.Equal(0xFF, style.FontAlpha);
            Assert.Equal(VLCTextStyleFlags.Bold | VLCTextStyleFlags.Italic, style.StyleFlags);
        }
        finally
        {
            Marshal.FreeHGlobal(styleMemory);
        }
    }

    #endregion
}
