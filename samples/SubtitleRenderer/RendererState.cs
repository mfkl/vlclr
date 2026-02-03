using System.Runtime.CompilerServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using VLCLR.Native;

namespace SubtitleRenderer;

/// <summary>
/// Manages state for the subtitle text renderer.
/// Tracks render count, initialization state, and renderer instance.
/// </summary>
public static class RendererState
{
    private static nint _filterPtr;
    private static long _renderCount;
    private static bool _initialized;

    // Reusable canvas instance for rendering
    private static SubtitleCanvas? _canvas;

    // Default canvas dimensions (used if region doesn't specify)
    private const int DefaultWidth = 1920;
    private const int DefaultHeight = 1080;

    // Debug output control
    private static bool _debugOutputEnabled;
    private static string? _debugOutputPath;
    private static bool _firstRenderSaved;

    /// <summary>
    /// Current render count (increments each time Render is called).
    /// </summary>
    public static long RenderCount => _renderCount;

    /// <summary>
    /// Initialize the renderer state.
    /// </summary>
    public static void Initialize(nint filterPtr)
    {
        _filterPtr = filterPtr;
        _renderCount = 0;
        _initialized = true;
        _firstRenderSaved = false;

        // Check for debug output environment variable
        _debugOutputPath = Environment.GetEnvironmentVariable("DOTNET_SUBTITLE_DEBUG_PATH");
        _debugOutputEnabled = !string.IsNullOrEmpty(_debugOutputPath);

        Console.Error.WriteLine("[.NET Subtitle] RendererState initialized");
        if (_debugOutputEnabled)
        {
            Console.Error.WriteLine($"[.NET Subtitle] Debug output enabled: {_debugOutputPath}");
        }
    }

    /// <summary>
    /// Cleanup the renderer state.
    /// </summary>
    public static void Cleanup()
    {
        Console.Error.WriteLine($"[.NET Subtitle] RendererState cleanup, rendered {_renderCount} times");

        _canvas?.Dispose();
        _canvas = null;

        _initialized = false;
        _filterPtr = nint.Zero;
    }

    /// <summary>
    /// Render a subtitle region to a picture.
    /// </summary>
    /// <param name="filterPtr">Pointer to the filter instance.</param>
    /// <param name="regionPtr">Pointer to the input subpicture_region_t containing text segments.</param>
    /// <param name="chromaListPtr">Pointer to null-terminated array of supported output chromas.</param>
    /// <returns>Pointer to rendered subpicture_region_t, or nint.Zero on failure.</returns>
    public static unsafe nint Render(nint filterPtr, nint regionPtr, nint chromaListPtr)
    {
        Console.Error.WriteLine($"[.NET Subtitle] Render method entered, initialized={_initialized}, debugEnabled={_debugOutputEnabled}");
        
        if (!_initialized)
        {
            return nint.Zero;
        }

        _renderCount++;

        // Parse text segments from the region
        var segments = TextSegmentParser.Parse(regionPtr);

        // Log first few render calls for debugging
        if (_renderCount <= 5)
        {
            string description = TextSegmentParser.ParseAndDescribe(regionPtr);
            Console.Error.WriteLine($"[.NET Subtitle] Render #{_renderCount}: {description}");

            // Log individual segment details with ACTUAL rendered style (post-conversion)
            foreach (var segment in segments)
            {
                var style = segment.Style;
                Console.Error.WriteLine($"[.NET Subtitle]   Segment: \"{segment.Text}\"");
                Console.Error.WriteLine($"[.NET Subtitle]   Style: FG=#{style.ForegroundColor:X6}, Outline={style.HasOutline}, Width={style.OutlineWidth}px");
            }
        }

        // Skip empty text (handles Phase 8.2)
        if (segments.Count == 0 || segments.TrueForAll(s => s.IsEmpty))
        {
            return nint.Zero;
        }

        // Extract region information for positioning and sizing
        ref VLCSubpictureRegion region = ref Unsafe.AsRef<VLCSubpictureRegion>((void*)regionPtr);

        // Get actual video dimensions from filter's format - this is crucial!
        // The region's MaxWidth/MaxHeight may be larger than the actual video
        ref VLCFilter filter = ref Unsafe.AsRef<VLCFilter>((void*)filterPtr);
        uint videoWidth = filter.FormatOut.Video.Width > 0 ? filter.FormatOut.Video.Width : (uint)DefaultWidth;
        uint videoHeight = filter.FormatOut.Video.Height > 0 ? filter.FormatOut.Video.Height : (uint)DefaultHeight;
        
        // Use video dimensions for canvas (not region MaxWidth/MaxHeight)
        int canvasWidth = (int)videoWidth;
        int canvasHeight = (int)videoHeight;

        // Ensure minimum dimensions
        canvasWidth = Math.Max(canvasWidth, 320);
        canvasHeight = Math.Max(canvasHeight, 240);

        // Create or resize canvas
        if (_canvas == null)
        {
            _canvas = new SubtitleCanvas(canvasWidth, canvasHeight);
        }
        else if (_canvas.Width != canvasWidth || _canvas.Height != canvasHeight)
        {
            _canvas.Dispose();
            _canvas = new SubtitleCanvas(canvasWidth, canvasHeight);
        }

        // Determine text alignment based on region's alignment flags
        SubtitleAlignment alignment = GetAlignment(region.Align);

        // Render text segments to canvas
        _canvas.Render(segments, alignment);

        // Save debug image on first successful render
        if (_debugOutputEnabled && !_firstRenderSaved)
        {
            try
            {
                Console.Error.WriteLine("[.NET Subtitle] Attempting debug save...");
                _canvas.SaveDebugImage(_debugOutputPath!);
                Console.Error.WriteLine($"[.NET Subtitle] Saved debug image to: {_debugOutputPath}");
                _firstRenderSaved = true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[.NET Subtitle] Failed to save debug image: {ex.Message}");
            }
        }

        // Get the rendered image
        Image<Rgba32>? image = _canvas.GetImage();
        if (image == null)
        {
            return nint.Zero;
        }

        // Convert to VLC subpicture region
        nint outputRegionPtr = PictureConverter.ToSubpictureRegion(image, chromaListPtr);

        if (outputRegionPtr != nint.Zero && _renderCount <= 5)
        {
            Console.Error.WriteLine($"[.NET Subtitle] Created region {canvasWidth}x{canvasHeight}, alignment={alignment}");
        }

        return outputRegionPtr;
    }

    /// <summary>
    /// Converts VLC alignment flags to SubtitleAlignment enum.
    /// </summary>
    private static SubtitleAlignment GetAlignment(int vlcAlign)
    {
        // VLC uses bitmask for alignment
        // Left = 0x1, Right = 0x2, Top = 0x4, Bottom = 0x8
        if ((vlcAlign & VLCSubpictureAlign.Left) != 0)
        {
            return SubtitleAlignment.Left;
        }
        if ((vlcAlign & VLCSubpictureAlign.Right) != 0)
        {
            return SubtitleAlignment.Right;
        }
        // Default to center for horizontal alignment
        return SubtitleAlignment.Center;
    }
}
