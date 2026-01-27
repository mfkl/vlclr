using System.Runtime.InteropServices;

namespace VlcPlugin;

/// <summary>
/// Manages state for the video filter.
/// Tracks frame count, renderer instance, and filter configuration.
/// </summary>
public static class FilterState
{
    private static nint _filterPtr;
    private static int _width;
    private static int _height;
    private static uint _chroma;
    private static OverlayRenderer? _renderer;
    private static long _frameCount;
    private static bool _initialized;

    // For debug frame save
    private static bool _savedDebugFrame;
    private const string DebugFramePath = "overlay_test.png";

    /// <summary>
    /// Current frame count (increments each frame).
    /// </summary>
    public static long FrameCount => _frameCount;

    /// <summary>
    /// Initialize the filter state.
    /// </summary>
    public static void Initialize(nint filterPtr, int width, int height, uint chroma)
    {
        _filterPtr = filterPtr;
        _width = width;
        _height = height;
        _chroma = chroma;
        _frameCount = 0;
        _savedDebugFrame = false;

        // Create the overlay renderer
        _renderer = new OverlayRenderer();

        _initialized = true;

        Console.Error.WriteLine($"[VlcPlugin] FilterState initialized: {width}x{height}");
    }

    /// <summary>
    /// Cleanup the filter state.
    /// </summary>
    public static void Cleanup()
    {
        _renderer?.Dispose();
        _renderer = null;
        _initialized = false;
        _filterPtr = nint.Zero;

        Console.Error.WriteLine($"[VlcPlugin] FilterState cleanup, processed {_frameCount} frames");
    }

    /// <summary>
    /// Process a video frame - render and composite the overlay.
    /// </summary>
    public static unsafe void ProcessFrame(nint pixels, int pitch, int visiblePitch, int visibleLines, uint chroma)
    {
        if (!_initialized || _renderer == null)
            return;

        _frameCount++;

        // Render the overlay text
        _renderer.RenderOverlay(_frameCount);

        // Get overlay pixels
        var overlay = _renderer.GetOverlayPixels();
        int overlayWidth = _renderer.OverlayWidth;
        int overlayHeight = _renderer.OverlayHeight;

        // Composite overlay onto frame
        byte* framePtr = (byte*)pixels;

        // Determine bytes per pixel based on chroma
        int bytesPerPixel = GetBytesPerPixel(chroma);
        if (bytesPerPixel == 0)
        {
            // Unknown format - log once and skip
            if (_frameCount == 1)
            {
                Console.Error.WriteLine($"[VlcPlugin] Unknown chroma format: 0x{chroma:X8}");
            }
            return;
        }

        // Position for overlay (top-left with padding)
        int offsetX = 10;
        int offsetY = 10;

        // Calculate frame width from visible pitch
        int frameWidth = visiblePitch / bytesPerPixel;

        // Ensure overlay fits within frame
        int maxOverlayWidth = Math.Min(overlayWidth, frameWidth - offsetX);
        int maxOverlayHeight = Math.Min(overlayHeight, visibleLines - offsetY);

        if (maxOverlayWidth <= 0 || maxOverlayHeight <= 0)
            return;

        // Composite based on format
        bool isBgra = IsBgraFormat(chroma);
        bool hasAlpha = HasAlphaChannel(chroma);

        for (int y = 0; y < maxOverlayHeight; y++)
        {
            int frameY = offsetY + y;
            byte* rowPtr = framePtr + (frameY * pitch) + (offsetX * bytesPerPixel);

            for (int x = 0; x < maxOverlayWidth; x++)
            {
                int overlayIdx = (y * overlayWidth + x) * 4; // RGBA
                byte r = overlay[overlayIdx];
                byte g = overlay[overlayIdx + 1];
                byte b = overlay[overlayIdx + 2];
                byte a = overlay[overlayIdx + 3];

                if (a == 0)
                {
                    // Fully transparent - skip
                    rowPtr += bytesPerPixel;
                    continue;
                }

                if (bytesPerPixel == 4)
                {
                    // 32-bit format (RGBA or BGRA)
                    if (a == 255)
                    {
                        // Fully opaque - direct copy
                        if (isBgra)
                        {
                            rowPtr[0] = b;
                            rowPtr[1] = g;
                            rowPtr[2] = r;
                            if (hasAlpha) rowPtr[3] = a;
                        }
                        else
                        {
                            rowPtr[0] = r;
                            rowPtr[1] = g;
                            rowPtr[2] = b;
                            if (hasAlpha) rowPtr[3] = a;
                        }
                    }
                    else
                    {
                        // Alpha blend
                        int invAlpha = 255 - a;
                        if (isBgra)
                        {
                            rowPtr[0] = (byte)((b * a + rowPtr[0] * invAlpha) / 255);
                            rowPtr[1] = (byte)((g * a + rowPtr[1] * invAlpha) / 255);
                            rowPtr[2] = (byte)((r * a + rowPtr[2] * invAlpha) / 255);
                        }
                        else
                        {
                            rowPtr[0] = (byte)((r * a + rowPtr[0] * invAlpha) / 255);
                            rowPtr[1] = (byte)((g * a + rowPtr[1] * invAlpha) / 255);
                            rowPtr[2] = (byte)((b * a + rowPtr[2] * invAlpha) / 255);
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
                            rowPtr[0] = b;
                            rowPtr[1] = g;
                            rowPtr[2] = r;
                        }
                        else
                        {
                            rowPtr[0] = r;
                            rowPtr[1] = g;
                            rowPtr[2] = b;
                        }
                    }
                    else
                    {
                        int invAlpha = 255 - a;
                        if (isBgra)
                        {
                            rowPtr[0] = (byte)((b * a + rowPtr[0] * invAlpha) / 255);
                            rowPtr[1] = (byte)((g * a + rowPtr[1] * invAlpha) / 255);
                            rowPtr[2] = (byte)((r * a + rowPtr[2] * invAlpha) / 255);
                        }
                        else
                        {
                            rowPtr[0] = (byte)((r * a + rowPtr[0] * invAlpha) / 255);
                            rowPtr[1] = (byte)((g * a + rowPtr[1] * invAlpha) / 255);
                            rowPtr[2] = (byte)((b * a + rowPtr[2] * invAlpha) / 255);
                        }
                    }
                }
                else if (bytesPerPixel == 1)
                {
                    // YUV Y plane - overlay as grayscale
                    byte gray = (byte)((r * 77 + g * 150 + b * 29) >> 8);
                    if (a == 255)
                    {
                        rowPtr[0] = gray;
                    }
                    else
                    {
                        int invAlpha = 255 - a;
                        rowPtr[0] = (byte)((gray * a + rowPtr[0] * invAlpha) / 255);
                    }
                }

                rowPtr += bytesPerPixel;
            }
        }

        // Save first frame to disk for verification
        if (!_savedDebugFrame && _frameCount == 1)
        {
            try
            {
                // Ensure directory exists
                var dir = Path.GetDirectoryName(DebugFramePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // Save just the overlay for now (simpler verification)
                _renderer.SaveOverlayToFile(DebugFramePath);
                Console.Error.WriteLine($"[VlcPlugin] Saved debug overlay to: {DebugFramePath}");
                _savedDebugFrame = true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[VlcPlugin] Failed to save debug frame: {ex.Message}");
                _savedDebugFrame = true; // Don't try again
            }
        }
    }

    /// <summary>
    /// Get bytes per pixel for a VLC chroma format.
    /// </summary>
    private static int GetBytesPerPixel(uint chroma)
    {
        // VLC chroma codes are fourcc values
        // Common formats:
        // RV32 = 0x32335652 = "RV32" (32-bit RGB, actually BGRX)
        // RV24 = 0x34325652 = "RV24" (24-bit RGB, actually BGR)
        // RGBA = 0x41424752 = "RGBA"
        // BGRA = 0x41524742 = "BGRA"
        // I420 = 0x30323449 = "I420" (YUV 4:2:0 planar)

        // For simplicity, map known formats
        return chroma switch
        {
            0x32335652 => 4, // RV32
            0x34325652 => 3, // RV24
            0x41424752 => 4, // RGBA
            0x41524742 => 4, // BGRA
            0x30323449 => 1, // I420 (Y plane only)
            0x32315659 => 1, // YV12 (Y plane only)
            _ => GuessFromFourcc(chroma)
        };
    }

    private static int GuessFromFourcc(uint chroma)
    {
        // Try to guess from fourcc pattern
        char c1 = (char)(chroma & 0xFF);
        char c2 = (char)((chroma >> 8) & 0xFF);
        char c3 = (char)((chroma >> 16) & 0xFF);
        char c4 = (char)((chroma >> 24) & 0xFF);

        string fourcc = $"{c1}{c2}{c3}{c4}";

        // Log for debugging
        if (_frameCount == 1)
        {
            Console.Error.WriteLine($"[VlcPlugin] Chroma fourcc: {fourcc}");
        }

        // Patterns
        if (fourcc.Contains("32") || fourcc.Contains("RGBA") || fourcc.Contains("BGRA") || fourcc.Contains("ARGB"))
            return 4;
        if (fourcc.Contains("24") || fourcc.Contains("RGB"))
            return 3;
        if (fourcc.StartsWith("I4") || fourcc.StartsWith("YV") || fourcc.StartsWith("NV"))
            return 1; // Planar YUV - Y plane is 1 byte per pixel

        return 0; // Unknown
    }

    private static bool IsBgraFormat(uint chroma)
    {
        // RV32 and RV24 are actually BGR(A) in VLC
        // BGRA is obviously BGRA
        return chroma switch
        {
            0x32335652 => true,  // RV32 (BGRX)
            0x34325652 => true,  // RV24 (BGR)
            0x41524742 => true,  // BGRA
            _ => false
        };
    }

    private static bool HasAlphaChannel(uint chroma)
    {
        return chroma switch
        {
            0x41424752 => true, // RGBA
            0x41524742 => true, // BGRA
            _ => false
        };
    }
}
