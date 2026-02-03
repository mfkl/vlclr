using System.Runtime.InteropServices;
using VLCLR.Imaging;
using VLCLR.Native;
using Xunit;

namespace VLCLR.Tests;

/// <summary>
/// Tests for FrameCompositor class.
/// Verifies alpha blending and pixel format conversions.
/// </summary>
public class FrameCompositorTests
{
    #region RgbToLuminance Tests

    [Fact]
    public void RgbToLuminance_White_Returns255()
    {
        byte y = FrameCompositor.RgbToLuminance(255, 255, 255);
        Assert.Equal(255, y);
    }

    [Fact]
    public void RgbToLuminance_Black_Returns0()
    {
        byte y = FrameCompositor.RgbToLuminance(0, 0, 0);
        Assert.Equal(0, y);
    }

    [Fact]
    public void RgbToLuminance_PureRed_Returns77()
    {
        // Using BT.601: Y = 0.299*R + 0.587*G + 0.114*B
        // For (255, 0, 0): Y = 0.299 * 255 ≈ 76.245
        // Integer approximation: (255*77) >> 8 = 76
        byte y = FrameCompositor.RgbToLuminance(255, 0, 0);
        Assert.InRange(y, 75, 77); // Allow for rounding
    }

    [Fact]
    public void RgbToLuminance_PureGreen_Returns150()
    {
        // For (0, 255, 0): Y = 0.587 * 255 ≈ 149.685
        // Integer approximation: (255*150) >> 8 = 149
        byte y = FrameCompositor.RgbToLuminance(0, 255, 0);
        Assert.InRange(y, 148, 150); // Allow for rounding
    }

    [Fact]
    public void RgbToLuminance_PureBlue_Returns29()
    {
        // For (0, 0, 255): Y = 0.114 * 255 ≈ 29.07
        // Integer approximation: (255*29) >> 8 = 28
        byte y = FrameCompositor.RgbToLuminance(0, 0, 255);
        Assert.InRange(y, 28, 30); // Allow for rounding
    }

    #endregion

    #region Composite Tests - Null/Empty Input

    [Fact]
    public void Composite_NullFramePointer_ReturnsFalse()
    {
        byte[] overlay = new byte[16]; // 2x2 RGBA

        bool result = FrameCompositor.Composite(
            nint.Zero, // null frame pointer
            framePitch: 16,
            frameVisiblePitch: 16,
            frameVisibleLines: 4,
            chroma: VLCFourCC.RGBA,
            overlayPixels: overlay,
            overlayWidth: 2,
            overlayHeight: 2);

        Assert.False(result);
    }

    [Fact]
    public void Composite_EmptyOverlay_ReturnsFalse()
    {
        nint framePtr = Marshal.AllocHGlobal(64);
        try
        {
            bool result = FrameCompositor.Composite(
                framePtr,
                framePitch: 16,
                frameVisiblePitch: 16,
                frameVisibleLines: 4,
                chroma: VLCFourCC.RGBA,
                overlayPixels: [], // Empty overlay
                overlayWidth: 0,
                overlayHeight: 0);

            Assert.False(result);
        }
        finally
        {
            Marshal.FreeHGlobal(framePtr);
        }
    }

    [Fact]
    public void Composite_UnsupportedChroma_ReturnsFalse()
    {
        nint framePtr = Marshal.AllocHGlobal(64);
        byte[] overlay = new byte[16];
        try
        {
            bool result = FrameCompositor.Composite(
                framePtr,
                framePitch: 16,
                frameVisiblePitch: 16,
                frameVisibleLines: 4,
                chroma: 0xDEADBEEF, // Invalid chroma
                overlayPixels: overlay,
                overlayWidth: 2,
                overlayHeight: 2);

            Assert.False(result);
        }
        finally
        {
            Marshal.FreeHGlobal(framePtr);
        }
    }

    [Fact]
    public void Composite_OverlayOutsideFrame_ReturnsFalse()
    {
        nint framePtr = Marshal.AllocHGlobal(64);
        byte[] overlay = new byte[16];
        try
        {
            // Frame is 4x4, overlay at offset 10,10 is completely outside
            bool result = FrameCompositor.Composite(
                framePtr,
                framePitch: 16,
                frameVisiblePitch: 16,
                frameVisibleLines: 4,
                chroma: VLCFourCC.RGBA,
                overlayPixels: overlay,
                overlayWidth: 2,
                overlayHeight: 2,
                offsetX: 10,
                offsetY: 10);

            Assert.False(result);
        }
        finally
        {
            Marshal.FreeHGlobal(framePtr);
        }
    }

    #endregion

    #region Composite Tests - RGBA Format

    [Fact]
    public unsafe void Composite_RGBA_OpaquePixel_DirectCopy()
    {
        // Setup a 4x4 RGBA frame filled with black
        int width = 4;
        int height = 4;
        int pitch = width * 4;
        int size = pitch * height;

        nint framePtr = Marshal.AllocHGlobal(size);
        try
        {
            // Zero the frame (black)
            for (int i = 0; i < size; i++)
            {
                Marshal.WriteByte(framePtr, i, 0);
            }

            // Create a 1x1 opaque red overlay
            byte[] overlay = [255, 0, 0, 255]; // RGBA: Red, fully opaque

            // Composite at position (1,1)
            bool result = FrameCompositor.Composite(
                framePtr,
                framePitch: pitch,
                frameVisiblePitch: pitch,
                frameVisibleLines: height,
                chroma: VLCFourCC.RGBA,
                overlayPixels: overlay,
                overlayWidth: 1,
                overlayHeight: 1,
                offsetX: 1,
                offsetY: 1);

            Assert.True(result);

            // Verify the pixel at (1,1) is now red
            byte* pixels = (byte*)framePtr;
            int pixelOffset = (1 * pitch) + (1 * 4);

            Assert.Equal(255, pixels[pixelOffset + 0]); // R
            Assert.Equal(0, pixels[pixelOffset + 1]);   // G
            Assert.Equal(0, pixels[pixelOffset + 2]);   // B
            // Alpha in frame may or may not be set depending on hasAlpha flag
        }
        finally
        {
            Marshal.FreeHGlobal(framePtr);
        }
    }

    [Fact]
    public unsafe void Composite_RGBA_TransparentPixel_NoChange()
    {
        int width = 4;
        int height = 4;
        int pitch = width * 4;
        int size = pitch * height;

        nint framePtr = Marshal.AllocHGlobal(size);
        try
        {
            // Fill frame with white
            for (int i = 0; i < size; i += 4)
            {
                Marshal.WriteByte(framePtr, i + 0, 255); // R
                Marshal.WriteByte(framePtr, i + 1, 255); // G
                Marshal.WriteByte(framePtr, i + 2, 255); // B
                Marshal.WriteByte(framePtr, i + 3, 255); // A
            }

            // Create a 1x1 fully transparent overlay
            byte[] overlay = [255, 0, 0, 0]; // RGBA: Red, fully transparent

            // Composite at position (1,1)
            bool result = FrameCompositor.Composite(
                framePtr,
                framePitch: pitch,
                frameVisiblePitch: pitch,
                frameVisibleLines: height,
                chroma: VLCFourCC.RGBA,
                overlayPixels: overlay,
                overlayWidth: 1,
                overlayHeight: 1,
                offsetX: 1,
                offsetY: 1);

            Assert.True(result);

            // Verify the pixel at (1,1) is still white (transparent overlay = no change)
            byte* pixels = (byte*)framePtr;
            int pixelOffset = (1 * pitch) + (1 * 4);

            Assert.Equal(255, pixels[pixelOffset + 0]); // R
            Assert.Equal(255, pixels[pixelOffset + 1]); // G
            Assert.Equal(255, pixels[pixelOffset + 2]); // B
        }
        finally
        {
            Marshal.FreeHGlobal(framePtr);
        }
    }

    [Fact]
    public unsafe void Composite_RGBA_SemiTransparent_Blends()
    {
        int width = 4;
        int height = 4;
        int pitch = width * 4;
        int size = pitch * height;

        nint framePtr = Marshal.AllocHGlobal(size);
        try
        {
            // Fill frame with black
            for (int i = 0; i < size; i++)
            {
                Marshal.WriteByte(framePtr, i, 0);
            }

            // Create a 1x1 semi-transparent white overlay (50% alpha)
            byte[] overlay = [255, 255, 255, 128]; // RGBA: White, ~50% opaque

            // Composite at position (0,0)
            bool result = FrameCompositor.Composite(
                framePtr,
                framePitch: pitch,
                frameVisiblePitch: pitch,
                frameVisibleLines: height,
                chroma: VLCFourCC.RGBA,
                overlayPixels: overlay,
                overlayWidth: 1,
                overlayHeight: 1,
                offsetX: 0,
                offsetY: 0);

            Assert.True(result);

            // Verify blending: should be approximately 50% gray
            // (255*128 + 0*127) / 255 ≈ 128
            byte* pixels = (byte*)framePtr;

            Assert.InRange(pixels[0], 126, 130); // R ≈ 128
            Assert.InRange(pixels[1], 126, 130); // G ≈ 128
            Assert.InRange(pixels[2], 126, 130); // B ≈ 128
        }
        finally
        {
            Marshal.FreeHGlobal(framePtr);
        }
    }

    #endregion

    #region Composite Tests - BGRA Format

    [Fact]
    public unsafe void Composite_BGRA_OpaquePixel_SwizzlesChannels()
    {
        int width = 4;
        int height = 4;
        int pitch = width * 4;
        int size = pitch * height;

        nint framePtr = Marshal.AllocHGlobal(size);
        try
        {
            for (int i = 0; i < size; i++)
            {
                Marshal.WriteByte(framePtr, i, 0);
            }

            // Create a 1x1 opaque red overlay (RGBA format)
            byte[] overlay = [255, 0, 0, 255]; // RGBA: Red, fully opaque

            // Composite to BGRA frame
            bool result = FrameCompositor.Composite(
                framePtr,
                framePitch: pitch,
                frameVisiblePitch: pitch,
                frameVisibleLines: height,
                chroma: VLCFourCC.BGRA, // Target is BGRA
                overlayPixels: overlay,
                overlayWidth: 1,
                overlayHeight: 1,
                offsetX: 0,
                offsetY: 0);

            Assert.True(result);

            // Verify the pixel is stored as BGRA (B=0, G=0, R=255)
            byte* pixels = (byte*)framePtr;

            Assert.Equal(0, pixels[0]);   // B
            Assert.Equal(0, pixels[1]);   // G
            Assert.Equal(255, pixels[2]); // R
        }
        finally
        {
            Marshal.FreeHGlobal(framePtr);
        }
    }

    #endregion

    #region FillRect Tests

    [Fact]
    public void FillRect_NullPointer_NoOp()
    {
        // Should not throw
        FrameCompositor.FillRect(
            nint.Zero,
            framePitch: 16,
            chroma: VLCFourCC.RGBA,
            x: 0, y: 0, width: 4, height: 4,
            r: 255, g: 0, b: 0);
    }

    [Fact]
    public void FillRect_ZeroSize_NoOp()
    {
        nint framePtr = Marshal.AllocHGlobal(64);
        try
        {
            // Should not throw with zero dimensions
            FrameCompositor.FillRect(
                framePtr,
                framePitch: 16,
                chroma: VLCFourCC.RGBA,
                x: 0, y: 0, width: 0, height: 0,
                r: 255, g: 0, b: 0);
        }
        finally
        {
            Marshal.FreeHGlobal(framePtr);
        }
    }

    [Fact]
    public unsafe void FillRect_RGBA_FillsRegion()
    {
        int width = 4;
        int height = 4;
        int pitch = width * 4;
        int size = pitch * height;

        nint framePtr = Marshal.AllocHGlobal(size);
        try
        {
            // Zero the frame
            for (int i = 0; i < size; i++)
            {
                Marshal.WriteByte(framePtr, i, 0);
            }

            // Fill a 2x2 region at (1,1) with green
            FrameCompositor.FillRect(
                framePtr,
                framePitch: pitch,
                chroma: VLCFourCC.RGBA,
                x: 1, y: 1, width: 2, height: 2,
                r: 0, g: 255, b: 0, a: 255);

            byte* pixels = (byte*)framePtr;

            // Check pixels inside the filled region
            for (int y = 1; y <= 2; y++)
            {
                for (int x = 1; x <= 2; x++)
                {
                    int offset = (y * pitch) + (x * 4);
                    Assert.Equal(0, pixels[offset + 0]);   // R
                    Assert.Equal(255, pixels[offset + 1]); // G
                    Assert.Equal(0, pixels[offset + 2]);   // B
                }
            }

            // Check a pixel outside the region is still black
            Assert.Equal(0, pixels[0]); // (0,0) should be black
        }
        finally
        {
            Marshal.FreeHGlobal(framePtr);
        }
    }

    #endregion
}
