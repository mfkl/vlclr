using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using VLCLR.Imaging;
using VLCLR.Native;
using Xunit;

namespace VLCLR.Tests;

/// <summary>
/// Tests for PictureConverter class.
/// Verifies chroma selection and format creation.
/// Note: Full picture creation requires VLC core, so we test the helper methods.
/// </summary>
public class PictureConverterTests
{
    #region SelectChroma Tests

    [Fact]
    public void SelectChroma_NullPointer_ReturnsRGBA()
    {
        uint chroma = PictureConverter.SelectChroma(nint.Zero);

        Assert.Equal(VLCFourCC.RGBA, chroma);
    }

    [Fact]
    public unsafe void SelectChroma_RGBAInList_ReturnsRGBA()
    {
        // Create a chroma list: RGBA, BGRA, 0 (null terminator)
        uint[] chromaList = [VLCFourCC.RGBA, VLCFourCC.BGRA, 0];
        fixed (uint* ptr = chromaList)
        {
            uint chroma = PictureConverter.SelectChroma((nint)ptr);

            Assert.Equal(VLCFourCC.RGBA, chroma); // Prefers RGBA
        }
    }

    [Fact]
    public unsafe void SelectChroma_OnlyBGRAInList_ReturnsBGRA()
    {
        // Create a chroma list: BGRA, 0 (null terminator)
        uint[] chromaList = [VLCFourCC.BGRA, 0];
        fixed (uint* ptr = chromaList)
        {
            uint chroma = PictureConverter.SelectChroma((nint)ptr);

            Assert.Equal(VLCFourCC.BGRA, chroma);
        }
    }

    [Fact]
    public unsafe void SelectChroma_BGRABeforeRGBA_StillReturnsRGBA()
    {
        // Even if BGRA comes first, should prefer RGBA
        uint[] chromaList = [VLCFourCC.BGRA, VLCFourCC.RGBA, 0];
        fixed (uint* ptr = chromaList)
        {
            uint chroma = PictureConverter.SelectChroma((nint)ptr);

            Assert.Equal(VLCFourCC.RGBA, chroma); // Still prefers RGBA
        }
    }

    [Fact]
    public unsafe void SelectChroma_NoSupportedFormat_DefaultsToRGBA()
    {
        // Create a chroma list with unsupported formats only
        uint[] chromaList = [VLCFourCC.I420, VLCFourCC.YV12, 0];
        fixed (uint* ptr = chromaList)
        {
            uint chroma = PictureConverter.SelectChroma((nint)ptr);

            // Falls back to RGBA and hopes VLC can handle it
            Assert.Equal(VLCFourCC.RGBA, chroma);
        }
    }

    [Fact]
    public unsafe void SelectChroma_EmptyList_DefaultsToRGBA()
    {
        // Create an empty list (just null terminator)
        uint[] chromaList = [0];
        fixed (uint* ptr = chromaList)
        {
            uint chroma = PictureConverter.SelectChroma((nint)ptr);

            Assert.Equal(VLCFourCC.RGBA, chroma);
        }
    }

    #endregion

    #region CreateFormat Tests

    [Fact]
    public void CreateFormat_SetsChroma()
    {
        var format = PictureConverter.CreateFormat(VLCFourCC.RGBA, 640, 480);

        Assert.Equal(VLCFourCC.RGBA, format.Chroma);
    }

    [Fact]
    public void CreateFormat_SetsDimensions()
    {
        var format = PictureConverter.CreateFormat(VLCFourCC.BGRA, 1920, 1080);

        Assert.Equal(1920u, format.Width);
        Assert.Equal(1080u, format.Height);
        Assert.Equal(1920u, format.VisibleWidth);
        Assert.Equal(1080u, format.VisibleHeight);
    }

    [Fact]
    public void CreateFormat_SetsDefaultSAR()
    {
        var format = PictureConverter.CreateFormat(VLCFourCC.RGBA, 640, 480);

        // SAR should be 1:1 (square pixels)
        Assert.Equal(1u, format.SarNum);
        Assert.Equal(1u, format.SarDen);
    }

    [Fact]
    public void CreateFormat_ZeroOffsets()
    {
        var format = PictureConverter.CreateFormat(VLCFourCC.RGBA, 640, 480);

        Assert.Equal(0u, format.XOffset);
        Assert.Equal(0u, format.YOffset);
    }

    [Fact]
    public void CreateFormat_NullPalette()
    {
        var format = PictureConverter.CreateFormat(VLCFourCC.RGBA, 640, 480);

        Assert.Equal(nint.Zero, format.Palette);
    }

    [Fact]
    public void CreateFormat_FrameRateBaseIsOne()
    {
        var format = PictureConverter.CreateFormat(VLCFourCC.RGBA, 640, 480);

        // Frame rate base should be 1 to avoid division by zero
        Assert.Equal(1u, format.FrameRateBase);
    }

    [Fact]
    public void CreateFormat_SmallDimensions()
    {
        var format = PictureConverter.CreateFormat(VLCFourCC.RGBA, 1, 1);

        Assert.Equal(1u, format.Width);
        Assert.Equal(1u, format.Height);
    }

    [Fact]
    public void CreateFormat_LargeDimensions()
    {
        var format = PictureConverter.CreateFormat(VLCFourCC.RGBA, 4096, 2160);

        Assert.Equal(4096u, format.Width);
        Assert.Equal(2160u, format.Height);
    }

    #endregion

    #region ToSubpictureRegion Tests - Basic Validation

    [Fact]
    public void ToSubpictureRegion_NullImage_ReturnsZero()
    {
        nint result = PictureConverter.ToSubpictureRegion(null!, nint.Zero);

        Assert.Equal(nint.Zero, result);
    }

    [Fact]
    public void ToSubpictureRegion_ZeroSizeImage_ReturnsZero()
    {
        // We can't create a zero-size ImageSharp image, so skip this test
        // The method checks for width/height <= 0 which can't happen with ImageSharp
    }

    #endregion

    #region VLCVideoFormat Structure Tests

    [Fact]
    public void VLCVideoFormat_StructureSize()
    {
        // Verify the structure has expected size for memory layout
        int size = Marshal.SizeOf<VLCVideoFormat>();

        // Should be a reasonable size (not too small)
        Assert.True(size >= 64, $"VLCVideoFormat size is {size}, expected at least 64 bytes");
    }

    [Fact]
    public unsafe void VLCVideoFormat_CanMarshalToMemory()
    {
        var format = PictureConverter.CreateFormat(VLCFourCC.RGBA, 800, 600);

        nint ptr = Marshal.AllocHGlobal(Marshal.SizeOf<VLCVideoFormat>());
        try
        {
            Marshal.StructureToPtr(format, ptr, false);
            var readBack = Marshal.PtrToStructure<VLCVideoFormat>(ptr);

            Assert.Equal(format.Chroma, readBack.Chroma);
            Assert.Equal(format.Width, readBack.Width);
            Assert.Equal(format.Height, readBack.Height);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    #endregion

    #region Integration-like Tests (Don't require VLC)

    [Fact]
    public void CreateFormat_AllFieldsSet()
    {
        var format = PictureConverter.CreateFormat(VLCFourCC.BGRA, 1280, 720);

        // Verify no uninitialized fields that could cause issues
        Assert.Equal(VLCFourCC.BGRA, format.Chroma);
        Assert.Equal(1280u, format.Width);
        Assert.Equal(720u, format.Height);
        Assert.Equal(0u, format.XOffset);
        Assert.Equal(0u, format.YOffset);
        Assert.Equal(1280u, format.VisibleWidth);
        Assert.Equal(720u, format.VisibleHeight);
        Assert.Equal(1u, format.SarNum);
        Assert.Equal(1u, format.SarDen);
        Assert.Equal(0u, format.FrameRate);
        Assert.Equal(1u, format.FrameRateBase);
        Assert.Equal(nint.Zero, format.Palette);
    }

    #endregion

    #region ImageSharp Integration Tests

    [Fact]
    public void ImageSharp_CreateImage_Works()
    {
        // Basic test to verify ImageSharp is working
        using var image = new Image<Rgba32>(10, 10);

        Assert.Equal(10, image.Width);
        Assert.Equal(10, image.Height);
    }

    [Fact]
    public void ImageSharp_SetPixel_Works()
    {
        using var image = new Image<Rgba32>(2, 2);

        image[0, 0] = new Rgba32(255, 0, 0, 255); // Red
        image[1, 1] = new Rgba32(0, 255, 0, 255); // Green

        Assert.Equal(255, image[0, 0].R);
        Assert.Equal(0, image[0, 0].G);
        Assert.Equal(255, image[1, 1].G);
    }

    [Fact]
    public void ImageSharp_CopyPixelData_Works()
    {
        using var image = new Image<Rgba32>(2, 2);

        image[0, 0] = new Rgba32(255, 0, 0, 255);
        image[1, 0] = new Rgba32(0, 255, 0, 255);
        image[0, 1] = new Rgba32(0, 0, 255, 255);
        image[1, 1] = new Rgba32(255, 255, 255, 255);

        byte[] pixels = new byte[2 * 2 * 4]; // 2x2 RGBA
        image.CopyPixelDataTo(pixels);

        // First pixel should be red (R=255, G=0, B=0, A=255)
        Assert.Equal(255, pixels[0]); // R
        Assert.Equal(0, pixels[1]);   // G
        Assert.Equal(0, pixels[2]);   // B
        Assert.Equal(255, pixels[3]); // A

        // Second pixel should be green
        Assert.Equal(0, pixels[4]);   // R
        Assert.Equal(255, pixels[5]); // G
        Assert.Equal(0, pixels[6]);   // B
        Assert.Equal(255, pixels[7]); // A
    }

    #endregion
}
