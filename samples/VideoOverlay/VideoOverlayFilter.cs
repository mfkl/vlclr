// Video Overlay Filter using VLCLR Framework
// Demonstrates using VLCVideoFilterBase and source generator

using VLCLR.Imaging;
using VLCLR.Native;
using VLCLR.Plugin;

namespace VideoOverlay;

/// <summary>
/// Video filter that renders a debug overlay showing frame count and GC stats.
/// Uses the VLCLR framework base class and source generator for entry points.
/// </summary>
[VLCModule("dotnet_overlay")]
[VLCCapability("video filter", Score = 0)]
[VLCDescription(".NET Native AOT Video Filter Overlay")]
[VLCConfig("dotnet-overlay-opacity", VLCConfigType.Float, Default = 1.0f, Min = 0.0f, Max = 1.0f,
    Description = "Overlay opacity", LongDescription = "Sets the opacity of the debug overlay (0.0 = transparent, 1.0 = opaque)")]
[VLCConfig("dotnet-overlay-x", VLCConfigType.Integer, Default = 10, Min = 0, Max = 4096,
    Description = "Overlay X position", LongDescription = "Horizontal position of the overlay in pixels")]
[VLCConfig("dotnet-overlay-y", VLCConfigType.Integer, Default = 10, Min = 0, Max = 2160,
    Description = "Overlay Y position", LongDescription = "Vertical position of the overlay in pixels")]
[VLCConfig("dotnet-overlay-enabled", VLCConfigType.Bool, Default = true,
    Description = "Enable overlay", LongDescription = "Enable or disable the debug overlay")]
public partial class VideoOverlayFilter : VLCVideoFilterBase
{
    private OverlayRenderer? _renderer;
#if DEBUG
    private bool _savedDebugFrame;
    private const string DebugFramePath = "overlay_test.png";
#endif

    /// <summary>
    /// Called when the filter opens. Initializes the overlay renderer.
    /// </summary>
    protected override bool OnOpen(VLCFilterContext context)
    {
        context.Logger.Info($"[VideoOverlay] Opening filter: {context.Width}x{context.Height} {context.ChromaString}");

        try
        {
            _renderer = new OverlayRenderer();
            context.Logger.Info("[VideoOverlay] Overlay renderer initialized");
            return true;
        }
        catch (Exception ex)
        {
            context.Logger.Error($"[VideoOverlay] Failed to initialize renderer: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Called when the filter closes. Cleans up the overlay renderer.
    /// </summary>
    protected override void OnClose()
    {
        Context.Logger.Info($"[VideoOverlay] Closing filter, processed {FrameCount} frames");
        _renderer?.Dispose();
        _renderer = null;
    }

    /// <summary>
    /// Called for the first frame. Logs format information.
    /// </summary>
    protected override void OnFirstFrame(VLCFrame frame)
    {
        Context.Logger.Info($"[VideoOverlay] First frame: {frame.Width}x{frame.Height} {VLCFourCC.ToString(frame.Chroma)}");
    }

    /// <summary>
    /// Processes each video frame by rendering and compositing the overlay.
    /// </summary>
    protected override void ProcessFrame(VLCFrame frame)
    {
        if (_renderer == null)
            return;

        // Render the overlay text
        _renderer.RenderOverlay(FrameCount);

        // Get overlay pixels
        var overlay = _renderer.GetOverlayPixels();
        int overlayWidth = _renderer.OverlayWidth;
        int overlayHeight = _renderer.OverlayHeight;

        // Use framework's FrameCompositor to blend overlay onto frame
        // Position overlay at top-left with 10px padding
        bool success = FrameCompositor.Composite(
            frame.Pixels,
            frame.Pitch,
            frame.VisiblePitch,
            frame.VisibleLines,
            frame.Chroma,
            overlay,
            overlayWidth,
            overlayHeight,
            offsetX: 10,
            offsetY: 10);

        // Log format issues on first frame only
        if (!success && FrameCount == 1)
        {
            Context.Logger.Warning($"[VideoOverlay] Compositing failed for chroma: {VLCFourCC.ToString(frame.Chroma)}");
        }

#if DEBUG
        // Save first frame to disk for verification
        if (!_savedDebugFrame && FrameCount == 1)
        {
            try
            {
                var dir = Path.GetDirectoryName(DebugFramePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                _renderer.SaveOverlayToFile(DebugFramePath);
                Context.Logger.Info($"[VideoOverlay] Saved debug overlay to: {DebugFramePath}");
                _savedDebugFrame = true;
            }
            catch (Exception ex)
            {
                Context.Logger.Warning($"[VideoOverlay] Failed to save debug frame: {ex.Message}");
                _savedDebugFrame = true; // Don't try again
            }
        }
#endif
    }
}
