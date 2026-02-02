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

        Console.Error.WriteLine("[.NET Subtitle] RendererState initialized");
    }

    /// <summary>
    /// Cleanup the renderer state.
    /// </summary>
    public static void Cleanup()
    {
        Console.Error.WriteLine($"[.NET Subtitle] RendererState cleanup, rendered {_renderCount} times");

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
    public static nint Render(nint filterPtr, nint regionPtr, nint chromaListPtr)
    {
        if (!_initialized)
        {
            return nint.Zero;
        }

        _renderCount++;

        // Log first few render calls for debugging
        if (_renderCount <= 5)
        {
            Console.Error.WriteLine($"[.NET Subtitle] Render #{_renderCount}: regionPtr=0x{regionPtr:X}, chromaListPtr=0x{chromaListPtr:X}");
        }

        // TODO: Phase 6 will implement text segment parsing
        // TODO: Phase 7 will implement ImageSharp rendering
        // TODO: Phase 8 will connect parsing + rendering + VLC picture creation

        // For now, return null to indicate no rendered output
        // This allows VLC to continue without crashing
        return nint.Zero;
    }
}
