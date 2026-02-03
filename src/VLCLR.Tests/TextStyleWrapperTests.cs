using System.Runtime.InteropServices;
using VLCLR.Native;
using VLCLR.Text;
using Xunit;

namespace VLCLR.Tests;

/// <summary>
/// Tests for TextStyleWrapper class.
/// Verifies parsing of VLC text_style_t structures and visibility optimizations.
/// </summary>
public class TextStyleWrapperTests
{
    #region FromNative Tests

    [Fact]
    public void FromNative_WithZeroPointer_ReturnsDefaults()
    {
        var wrapper = TextStyleWrapper.FromNative(nint.Zero);

        Assert.Equal("Arial", wrapper.FontName);
        Assert.Equal(24, wrapper.FontSize);
        Assert.Equal(0xFFFFFFu, wrapper.ForegroundColor);
        Assert.Equal(255, wrapper.ForegroundAlpha);
        Assert.False(wrapper.IsBold);
        Assert.False(wrapper.IsItalic);
        Assert.False(wrapper.HasOutline);
        Assert.False(wrapper.HasShadow);
    }

    [Fact]
    public unsafe void FromNative_WithPopulatedStyle_ExtractsValues()
    {
        // Arrange - allocate and populate a VLCTextStyle struct
        nint styleMemory = Marshal.AllocHGlobal(Marshal.SizeOf<VLCTextStyle>());
        try
        {
            // Zero the memory first
            for (int i = 0; i < Marshal.SizeOf<VLCTextStyle>(); i++)
            {
                Marshal.WriteByte(styleMemory, i, 0);
            }

            ref VLCTextStyle style = ref *(VLCTextStyle*)styleMemory;

            // Set values
            style.FontSize = 48;
            style.FontColor = 0xFF0000; // Red
            style.FontAlpha = 200;
            style.StyleFlags = VLCTextStyleFlags.Bold | VLCTextStyleFlags.Italic;
            style.OutlineColor = 0x00FF00; // Green outline
            style.OutlineWidth = 5;
            style.ShadowColor = 0x0000FF; // Blue shadow
            style.ShadowWidth = 3;

            // Act
            var wrapper = TextStyleWrapper.FromNative(styleMemory);

            // Assert
            Assert.Equal(48, wrapper.FontSize);
            Assert.Equal(0xFF0000u, wrapper.ForegroundColor);
            Assert.Equal(200, wrapper.ForegroundAlpha);
            Assert.True(wrapper.IsBold);
            Assert.True(wrapper.IsItalic);
            Assert.Equal(0x00FF00u, wrapper.OutlineColor);
            Assert.Equal(5, wrapper.OutlineWidth);
            Assert.Equal(0x0000FFu, wrapper.ShadowColor);
            Assert.Equal(3, wrapper.ShadowOffset);
        }
        finally
        {
            Marshal.FreeHGlobal(styleMemory);
        }
    }

    [Fact]
    public unsafe void FromNative_WithZeroFontSize_UsesDefault()
    {
        nint styleMemory = Marshal.AllocHGlobal(Marshal.SizeOf<VLCTextStyle>());
        try
        {
            for (int i = 0; i < Marshal.SizeOf<VLCTextStyle>(); i++)
            {
                Marshal.WriteByte(styleMemory, i, 0);
            }

            ref VLCTextStyle style = ref *(VLCTextStyle*)styleMemory;
            style.FontSize = 0; // Zero font size

            var wrapper = TextStyleWrapper.FromNative(styleMemory);

            Assert.Equal(24, wrapper.FontSize); // Default
        }
        finally
        {
            Marshal.FreeHGlobal(styleMemory);
        }
    }

    [Fact]
    public unsafe void FromNative_WithZeroAlpha_UsesDefault()
    {
        nint styleMemory = Marshal.AllocHGlobal(Marshal.SizeOf<VLCTextStyle>());
        try
        {
            for (int i = 0; i < Marshal.SizeOf<VLCTextStyle>(); i++)
            {
                Marshal.WriteByte(styleMemory, i, 0);
            }

            ref VLCTextStyle style = ref *(VLCTextStyle*)styleMemory;
            style.FontSize = 24;
            style.FontAlpha = 0; // Zero alpha

            var wrapper = TextStyleWrapper.FromNative(styleMemory);

            Assert.Equal(255, wrapper.ForegroundAlpha); // Default opaque
        }
        finally
        {
            Marshal.FreeHGlobal(styleMemory);
        }
    }

    [Fact]
    public unsafe void FromNative_WithAllStyleFlags_ExtractsAll()
    {
        nint styleMemory = Marshal.AllocHGlobal(Marshal.SizeOf<VLCTextStyle>());
        try
        {
            for (int i = 0; i < Marshal.SizeOf<VLCTextStyle>(); i++)
            {
                Marshal.WriteByte(styleMemory, i, 0);
            }

            ref VLCTextStyle style = ref *(VLCTextStyle*)styleMemory;
            style.FontSize = 24;
            style.StyleFlags = VLCTextStyleFlags.Bold |
                              VLCTextStyleFlags.Italic |
                              VLCTextStyleFlags.Underline |
                              VLCTextStyleFlags.Strikeout |
                              VLCTextStyleFlags.Outline |
                              VLCTextStyleFlags.Shadow |
                              VLCTextStyleFlags.Background;

            var wrapper = TextStyleWrapper.FromNative(styleMemory);

            Assert.True(wrapper.IsBold);
            Assert.True(wrapper.IsItalic);
            Assert.True(wrapper.IsUnderline);
            Assert.True(wrapper.IsStrikeout);
            Assert.True(wrapper.HasOutline);
            Assert.True(wrapper.HasShadow);
            Assert.True(wrapper.HasBackground);
        }
        finally
        {
            Marshal.FreeHGlobal(styleMemory);
        }
    }

    #endregion

    #region FromNativeWithVisibility Tests

    [Fact]
    public void FromNativeWithVisibility_WithZeroPointer_ForcesOutline()
    {
        var wrapper = TextStyleWrapper.FromNativeWithVisibility(nint.Zero, forceOutline: true, outlineWidth: 3);

        Assert.True(wrapper.HasOutline);
        Assert.Equal(3, wrapper.OutlineWidth);
    }

    [Fact]
    public unsafe void FromNativeWithVisibility_WithBlackText_ForcesWhite()
    {
        nint styleMemory = Marshal.AllocHGlobal(Marshal.SizeOf<VLCTextStyle>());
        try
        {
            for (int i = 0; i < Marshal.SizeOf<VLCTextStyle>(); i++)
            {
                Marshal.WriteByte(styleMemory, i, 0);
            }

            ref VLCTextStyle style = ref *(VLCTextStyle*)styleMemory;
            style.FontSize = 24;
            style.FontColor = 0x000000; // Black text
            style.FontAlpha = 255;

            var wrapper = TextStyleWrapper.FromNativeWithVisibility(styleMemory, forceWhiteText: true);

            Assert.Equal(0xFFFFFFu, wrapper.ForegroundColor); // Forced to white
        }
        finally
        {
            Marshal.FreeHGlobal(styleMemory);
        }
    }

    [Fact]
    public unsafe void FromNativeWithVisibility_WithWhiteText_KeepsWhite()
    {
        nint styleMemory = Marshal.AllocHGlobal(Marshal.SizeOf<VLCTextStyle>());
        try
        {
            for (int i = 0; i < Marshal.SizeOf<VLCTextStyle>(); i++)
            {
                Marshal.WriteByte(styleMemory, i, 0);
            }

            ref VLCTextStyle style = ref *(VLCTextStyle*)styleMemory;
            style.FontSize = 24;
            style.FontColor = 0xFFFFFF; // White text
            style.FontAlpha = 255;

            var wrapper = TextStyleWrapper.FromNativeWithVisibility(styleMemory, forceWhiteText: true);

            Assert.Equal(0xFFFFFFu, wrapper.ForegroundColor); // Still white
        }
        finally
        {
            Marshal.FreeHGlobal(styleMemory);
        }
    }

    [Fact]
    public unsafe void FromNativeWithVisibility_WithYellowText_KeepsYellow()
    {
        nint styleMemory = Marshal.AllocHGlobal(Marshal.SizeOf<VLCTextStyle>());
        try
        {
            for (int i = 0; i < Marshal.SizeOf<VLCTextStyle>(); i++)
            {
                Marshal.WriteByte(styleMemory, i, 0);
            }

            ref VLCTextStyle style = ref *(VLCTextStyle*)styleMemory;
            style.FontSize = 24;
            style.FontColor = 0xFFFF00; // Yellow text
            style.FontAlpha = 255;

            var wrapper = TextStyleWrapper.FromNativeWithVisibility(styleMemory, forceWhiteText: true);

            Assert.Equal(0xFFFF00u, wrapper.ForegroundColor); // Yellow preserved (not black)
        }
        finally
        {
            Marshal.FreeHGlobal(styleMemory);
        }
    }

    [Fact]
    public unsafe void FromNativeWithVisibility_ForceOutline_OverridesNativeOutlineWidth()
    {
        nint styleMemory = Marshal.AllocHGlobal(Marshal.SizeOf<VLCTextStyle>());
        try
        {
            for (int i = 0; i < Marshal.SizeOf<VLCTextStyle>(); i++)
            {
                Marshal.WriteByte(styleMemory, i, 0);
            }

            ref VLCTextStyle style = ref *(VLCTextStyle*)styleMemory;
            style.FontSize = 24;
            style.OutlineWidth = 1; // Native has small outline

            var wrapper = TextStyleWrapper.FromNativeWithVisibility(styleMemory, forceOutline: true, outlineWidth: 5);

            Assert.True(wrapper.HasOutline);
            Assert.Equal(5, wrapper.OutlineWidth); // Forced to 5
        }
        finally
        {
            Marshal.FreeHGlobal(styleMemory);
        }
    }

    [Fact]
    public unsafe void FromNativeWithVisibility_NoForceOutline_KeepsNativeOutline()
    {
        nint styleMemory = Marshal.AllocHGlobal(Marshal.SizeOf<VLCTextStyle>());
        try
        {
            for (int i = 0; i < Marshal.SizeOf<VLCTextStyle>(); i++)
            {
                Marshal.WriteByte(styleMemory, i, 0);
            }

            ref VLCTextStyle style = ref *(VLCTextStyle*)styleMemory;
            style.FontSize = 24;
            style.StyleFlags = VLCTextStyleFlags.Outline;
            style.OutlineWidth = 2;

            var wrapper = TextStyleWrapper.FromNativeWithVisibility(styleMemory, forceOutline: false);

            Assert.True(wrapper.HasOutline);
            Assert.Equal(2, wrapper.OutlineWidth); // Native value preserved
        }
        finally
        {
            Marshal.FreeHGlobal(styleMemory);
        }
    }

    #endregion

    #region Color Helper Tests

    [Fact]
    public void GetRed_ExtractsRedComponent()
    {
        Assert.Equal(0xFF, TextStyleWrapper.GetRed(0xFF0000));
        Assert.Equal(0x12, TextStyleWrapper.GetRed(0x123456));
        Assert.Equal(0x00, TextStyleWrapper.GetRed(0x00FF00));
    }

    [Fact]
    public void GetGreen_ExtractsGreenComponent()
    {
        Assert.Equal(0xFF, TextStyleWrapper.GetGreen(0x00FF00));
        Assert.Equal(0x34, TextStyleWrapper.GetGreen(0x123456));
        Assert.Equal(0x00, TextStyleWrapper.GetGreen(0xFF0000));
    }

    [Fact]
    public void GetBlue_ExtractsBlueComponent()
    {
        Assert.Equal(0xFF, TextStyleWrapper.GetBlue(0x0000FF));
        Assert.Equal(0x56, TextStyleWrapper.GetBlue(0x123456));
        Assert.Equal(0x00, TextStyleWrapper.GetBlue(0xFF0000));
    }

    [Fact]
    public void MakeColor_CreatesRRGGBBValue()
    {
        Assert.Equal(0xFF0000u, TextStyleWrapper.MakeColor(0xFF, 0x00, 0x00)); // Red
        Assert.Equal(0x00FF00u, TextStyleWrapper.MakeColor(0x00, 0xFF, 0x00)); // Green
        Assert.Equal(0x0000FFu, TextStyleWrapper.MakeColor(0x00, 0x00, 0xFF)); // Blue
        Assert.Equal(0x123456u, TextStyleWrapper.MakeColor(0x12, 0x34, 0x56));
    }

    [Fact]
    public void ColorHelpers_RoundTrip()
    {
        uint original = 0xAABBCC;
        byte r = TextStyleWrapper.GetRed(original);
        byte g = TextStyleWrapper.GetGreen(original);
        byte b = TextStyleWrapper.GetBlue(original);

        uint reconstructed = TextStyleWrapper.MakeColor(r, g, b);

        Assert.Equal(original, reconstructed);
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ToString_ReturnsFormattedString()
    {
        var wrapper = new TextStyleWrapper
        {
            FontName = "Arial",
            FontSize = 24,
            ForegroundColor = 0xFFFFFF,
            IsBold = true,
            HasOutline = true
        };

        string result = wrapper.ToString();

        Assert.Contains("Arial", result);
        Assert.Contains("24", result);
        Assert.Contains("FFFFFF", result);
        Assert.Contains("B", result); // Bold
        Assert.Contains("O", result); // Outline
    }

    [Fact]
    public void ToString_NoAttributes_ShowsDash()
    {
        var wrapper = new TextStyleWrapper();

        string result = wrapper.ToString();

        Assert.Contains("-", result);
    }

    #endregion
}
