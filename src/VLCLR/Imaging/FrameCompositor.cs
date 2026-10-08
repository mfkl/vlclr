// Frame compositor for RGBA overlay blending onto VLC video frames
// Supports various pixel formats (RGBA, BGRA, RGB, YUV planar)

using VLCLR.Native;

namespace VLCLR.Imaging;

/// <summary>
/// Composites RGBA overlay images onto video frame buffers.
/// Supports alpha blending and various pixel formats.
/// </summary>
/// <remarks>
/// This class handles the low-level pixel manipulation required to blend
/// overlay images onto video frames. It automatically handles format
/// conversion and alpha blending for different VLC chroma formats.
/// </remarks>
public static class FrameCompositor
{
    /// <summary>
    /// Composites an RGBA overlay onto a video frame buffer.
    /// </summary>
    /// <param name="framePixels">Pointer to the frame's pixel buffer.</param>
    /// <param name="framePitch">Bytes per row in the frame buffer (includes padding).</param>
    /// <param name="frameVisiblePitch">Visible bytes per row (actual pixel data).</param>
    /// <param name="frameVisibleLines">Number of visible lines (height).</param>
    /// <param name="chroma">VLC chroma format code (e.g., VLCFourCC.RGBA).</param>
    /// <param name="overlayPixels">RGBA overlay pixel data (4 bytes per pixel: R, G, B, A).</param>
    /// <param name="overlayWidth">Width of the overlay in pixels.</param>
    /// <param name="overlayHeight">Height of the overlay in pixels.</param>
    /// <param name="offsetX">X position to place the overlay (pixels from left).</param>
    /// <param name="offsetY">Y position to place the overlay (pixels from top).</param>
    /// <param name="opacity">Global opacity multiplier in the range 0.0 to 1.0.</param>
    /// <returns>True if compositing was performed, false if format is unsupported.</returns>
    public static unsafe bool Composite(
        nint framePixels,
        int framePitch,
        int frameVisiblePitch,
        int frameVisibleLines,
        uint chroma,
        ReadOnlySpan<byte> overlayPixels,
        int overlayWidth,
        int overlayHeight,
        int offsetX = 0,
        int offsetY = 0,
        float opacity = 1.0f)
    {
        if (framePixels == nint.Zero || overlayPixels.IsEmpty)
            return false;

        // Determine bytes per pixel based on chroma
        int bytesPerPixel = VLCFourCC.GetBytesPerPixel(chroma);
        if (bytesPerPixel == 0)
        {
            // Unknown format
            return false;
        }

        byte* framePtr = (byte*)framePixels;

        // Calculate frame width from visible pitch
        int frameWidth = frameVisiblePitch / bytesPerPixel;

        // Ensure overlay fits within frame
        int maxOverlayWidth = Math.Min(overlayWidth, frameWidth - offsetX);
        int maxOverlayHeight = Math.Min(overlayHeight, frameVisibleLines - offsetY);

        if (maxOverlayWidth <= 0 || maxOverlayHeight <= 0)
            return false;

        // Composite based on format
        bool isBgra = VLCFourCC.IsBgraFormat(chroma);
        bool hasAlpha = VLCFourCC.HasAlphaChannel(chroma);
        int opacityScale = (int)(Math.Clamp(opacity, 0.0f, 1.0f) * 255.0f + 0.5f);

        fixed (byte* overlayPtr = overlayPixels)
        {
            for (int y = 0; y < maxOverlayHeight; y++)
            {
                int frameY = offsetY + y;
                byte* rowPtr = framePtr + (frameY * framePitch) + (offsetX * bytesPerPixel);

                for (int x = 0; x < maxOverlayWidth; x++)
                {
                    int overlayIdx = (y * overlayWidth + x) * 4; // RGBA
                    byte r = overlayPtr[overlayIdx];
                    byte g = overlayPtr[overlayIdx + 1];
                    byte b = overlayPtr[overlayIdx + 2];
                    byte a = overlayPtr[overlayIdx + 3];
                    if (opacityScale != 255)
                        a = (byte)((a * opacityScale + 127) / 255);

                    if (a == 0)
                    {
                        // Fully transparent - skip
                        rowPtr += bytesPerPixel;
                        continue;
                    }

                    BlendPixel(rowPtr, r, g, b, a, bytesPerPixel, isBgra, hasAlpha);
                    rowPtr += bytesPerPixel;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Composites an RGBA overlay onto a video frame buffer using array input.
    /// </summary>
    /// <param name="framePixels">Pointer to the frame's pixel buffer.</param>
    /// <param name="framePitch">Bytes per row in the frame buffer.</param>
    /// <param name="frameVisiblePitch">Visible bytes per row.</param>
    /// <param name="frameVisibleLines">Number of visible lines.</param>
    /// <param name="chroma">VLC chroma format code.</param>
    /// <param name="overlayPixels">RGBA overlay pixel array.</param>
    /// <param name="overlayWidth">Width of the overlay.</param>
    /// <param name="overlayHeight">Height of the overlay.</param>
    /// <param name="offsetX">X position for the overlay.</param>
    /// <param name="offsetY">Y position for the overlay.</param>
    /// <param name="opacity">Global opacity multiplier in the range 0.0 to 1.0.</param>
    /// <returns>True if compositing was performed.</returns>
    public static bool Composite(
        nint framePixels,
        int framePitch,
        int frameVisiblePitch,
        int frameVisibleLines,
        uint chroma,
        byte[] overlayPixels,
        int overlayWidth,
        int overlayHeight,
        int offsetX = 0,
        int offsetY = 0,
        float opacity = 1.0f)
    {
        return Composite(
            framePixels, framePitch, frameVisiblePitch, frameVisibleLines,
            chroma, overlayPixels.AsSpan(), overlayWidth, overlayHeight,
            offsetX, offsetY, opacity);
    }

    /// <summary>
    /// Blends a single pixel using alpha compositing.
    /// </summary>
    /// <param name="dst">Destination pixel pointer.</param>
    /// <param name="r">Source red component.</param>
    /// <param name="g">Source green component.</param>
    /// <param name="b">Source blue component.</param>
    /// <param name="a">Source alpha (0=transparent, 255=opaque).</param>
    /// <param name="bytesPerPixel">Bytes per pixel in destination.</param>
    /// <param name="isBgra">True if destination is BGRA format.</param>
    /// <param name="hasAlpha">True if destination has alpha channel.</param>
    private static unsafe void BlendPixel(
        byte* dst, 
        byte r, byte g, byte b, byte a,
        int bytesPerPixel, bool isBgra, bool hasAlpha)
    {
        if (bytesPerPixel == 4)
        {
            // 32-bit format (RGBA or BGRA)
            if (a == 255)
            {
                // Fully opaque - direct copy
                if (isBgra)
                {
                    dst[0] = b;
                    dst[1] = g;
                    dst[2] = r;
                    if (hasAlpha) dst[3] = a;
                }
                else
                {
                    dst[0] = r;
                    dst[1] = g;
                    dst[2] = b;
                    if (hasAlpha) dst[3] = a;
                }
            }
            else
            {
                // Alpha blend
                int invAlpha = 255 - a;
                if (isBgra)
                {
                    dst[0] = (byte)((b * a + dst[0] * invAlpha) / 255);
                    dst[1] = (byte)((g * a + dst[1] * invAlpha) / 255);
                    dst[2] = (byte)((r * a + dst[2] * invAlpha) / 255);
                }
                else
                {
                    dst[0] = (byte)((r * a + dst[0] * invAlpha) / 255);
                    dst[1] = (byte)((g * a + dst[1] * invAlpha) / 255);
                    dst[2] = (byte)((b * a + dst[2] * invAlpha) / 255);
                }
            }
        }
        else if (bytesPerPixel == 3)
        {
            // 24-bit RGB
            if (a == 255)
            {
                if (isBgra)
                {
                    dst[0] = b;
                    dst[1] = g;
                    dst[2] = r;
                }
                else
                {
                    dst[0] = r;
                    dst[1] = g;
                    dst[2] = b;
                }
            }
            else
            {
                int invAlpha = 255 - a;
                if (isBgra)
                {
                    dst[0] = (byte)((b * a + dst[0] * invAlpha) / 255);
                    dst[1] = (byte)((g * a + dst[1] * invAlpha) / 255);
                    dst[2] = (byte)((r * a + dst[2] * invAlpha) / 255);
                }
                else
                {
                    dst[0] = (byte)((r * a + dst[0] * invAlpha) / 255);
                    dst[1] = (byte)((g * a + dst[1] * invAlpha) / 255);
                    dst[2] = (byte)((b * a + dst[2] * invAlpha) / 255);
                }
            }
        }
        else if (bytesPerPixel == 1)
        {
            // YUV Y plane - convert to grayscale (luminance)
            byte gray = RgbToLuminance(r, g, b);
            if (a == 255)
            {
                dst[0] = gray;
            }
            else
            {
                int invAlpha = 255 - a;
                dst[0] = (byte)((gray * a + dst[0] * invAlpha) / 255);
            }
        }
    }

    /// <summary>
    /// Converts RGB to luminance (Y component for YUV).
    /// Uses ITU-R BT.601 coefficients: Y = 0.299R + 0.587G + 0.114B
    /// </summary>
    /// <param name="r">Red component.</param>
    /// <param name="g">Green component.</param>
    /// <param name="b">Blue component.</param>
    /// <returns>Luminance value (0-255).</returns>
    public static byte RgbToLuminance(byte r, byte g, byte b)
    {
        // Using integer approximation: Y = (77*R + 150*G + 29*B) >> 8
        return (byte)((r * 77 + g * 150 + b * 29) >> 8);
    }

    /// <summary>
    /// Fills a rectangular region with a solid color.
    /// Useful for drawing backgrounds or clearing regions.
    /// </summary>
    /// <param name="framePixels">Pointer to the frame's pixel buffer.</param>
    /// <param name="framePitch">Bytes per row in the frame buffer.</param>
    /// <param name="chroma">VLC chroma format code.</param>
    /// <param name="x">X position of rectangle.</param>
    /// <param name="y">Y position of rectangle.</param>
    /// <param name="width">Width of rectangle.</param>
    /// <param name="height">Height of rectangle.</param>
    /// <param name="r">Red component.</param>
    /// <param name="g">Green component.</param>
    /// <param name="b">Blue component.</param>
    /// <param name="a">Alpha component (255=opaque).</param>
    public static unsafe void FillRect(
        nint framePixels,
        int framePitch,
        uint chroma,
        int x, int y, int width, int height,
        byte r, byte g, byte b, byte a = 255)
    {
        if (framePixels == nint.Zero || width <= 0 || height <= 0)
            return;

        int bytesPerPixel = VLCFourCC.GetBytesPerPixel(chroma);
        if (bytesPerPixel == 0)
            return;

        bool isBgra = VLCFourCC.IsBgraFormat(chroma);
        bool hasAlpha = VLCFourCC.HasAlphaChannel(chroma);

        byte* framePtr = (byte*)framePixels;

        for (int row = 0; row < height; row++)
        {
            byte* rowPtr = framePtr + ((y + row) * framePitch) + (x * bytesPerPixel);

            for (int col = 0; col < width; col++)
            {
                BlendPixel(rowPtr, r, g, b, a, bytesPerPixel, isBgra, hasAlpha);
                rowPtr += bytesPerPixel;
            }
        }
    }
}
