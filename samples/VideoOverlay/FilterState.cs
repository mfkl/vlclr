using VLCLR.Imaging;
using VLCLR.Native;

namespace VideoOverlay;

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

        Console.Error.WriteLine($"[.NET Video Overlay] FilterState initialized: {width}x{height}");
    }

    /// <summary>
    /// Cleanup the filter state.
    /// </summary>
    public static void Cleanup()
    {
        Console.Error.WriteLine($"[.NET Video Overlay] FilterState cleanup, processed {_frameCount} frames");

        _renderer?.Dispose();
        _renderer = null;
        _initialized = false;
        _filterPtr = nint.Zero;
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

        // Use framework's FrameCompositor to blend overlay onto frame
        // Position overlay at top-left with 10px padding
        bool success = FrameCompositor.Composite(
            pixels,
            pitch,
            visiblePitch,
            visibleLines,
            chroma,
            overlay,
            overlayWidth,
            overlayHeight,
            offsetX: 10,
            offsetY: 10);

        // Log format issues on first frame only
        if (!success && _frameCount == 1)
        {
            Console.Error.WriteLine($"[.NET Video Overlay] Compositing failed for chroma: {VLCFourCC.ToString(chroma)} (0x{chroma:X8})");
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
                Console.Error.WriteLine($"[.NET Video Overlay] Saved debug overlay to: {DebugFramePath}");
                _savedDebugFrame = true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[.NET Video Overlay] Failed to save debug frame: {ex.Message}");
                _savedDebugFrame = true; // Don't try again
            }
        }
    }

}
