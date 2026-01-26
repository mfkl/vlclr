using System.Runtime.InteropServices;

namespace VlcPlugin;

/// <summary>
/// Exports for the video filter plugin.
/// These are called from the C glue layer (video_filter_entry.c).
/// </summary>
public static class FilterExports
{
    /// <summary>
    /// Called when the video filter is opened.
    /// </summary>
    /// <param name="filterPtr">Pointer to filter_t structure</param>
    /// <param name="width">Frame width in pixels</param>
    /// <param name="height">Frame height in pixels</param>
    /// <param name="chroma">VLC chroma fourcc code</param>
    /// <returns>0 on success, non-zero on failure</returns>
    [UnmanagedCallersOnly(EntryPoint = "DotNetFilterOpen")]
    public static int Open(nint filterPtr, int width, int height, uint chroma)
    {
        try
        {
            // Log via stderr since we don't have access to VLC logging here easily
            Console.Error.WriteLine($"[VlcPlugin] Filter opening: {width}x{height} chroma=0x{chroma:X8}");

            // Initialize the overlay renderer
            FilterState.Initialize(filterPtr, width, height, chroma);

            Console.Error.WriteLine("[VlcPlugin] Filter initialized successfully");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[VlcPlugin] Filter open failed: {ex.Message}");
            return -1;
        }
    }

    /// <summary>
    /// Called when the video filter is closed.
    /// </summary>
    /// <param name="filterPtr">Pointer to filter_t structure</param>
    [UnmanagedCallersOnly(EntryPoint = "DotNetFilterClose")]
    public static void Close(nint filterPtr)
    {
        try
        {
            Console.Error.WriteLine("[VlcPlugin] Filter closing");
            FilterState.Cleanup();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[VlcPlugin] Filter close failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Called for each video frame to render the overlay.
    /// </summary>
    /// <param name="filterPtr">Pointer to filter_t structure</param>
    /// <param name="pixels">Pointer to frame pixel data</param>
    /// <param name="pitch">Bytes per row (including padding)</param>
    /// <param name="visiblePitch">Visible bytes per row</param>
    /// <param name="visibleLines">Number of visible lines (height)</param>
    /// <param name="chroma">VLC chroma fourcc code</param>
    [UnmanagedCallersOnly(EntryPoint = "DotNetFilterFrame")]
    public static void ProcessFrame(nint filterPtr,
        nint pixels, int pitch, int visiblePitch, int visibleLines,
        uint chroma)
    {
        try
        {
            FilterState.ProcessFrame(pixels, pitch, visiblePitch, visibleLines, chroma);
        }
        catch (Exception ex)
        {
            // Only log occasionally to avoid spam
            if (FilterState.FrameCount % 300 == 0)
            {
                Console.Error.WriteLine($"[VlcPlugin] Frame processing error: {ex.Message}");
            }
        }
    }
}
