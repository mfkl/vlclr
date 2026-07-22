// Subtitle Text Renderer using VLCLR Framework
// Demonstrates using VLCTextRendererBase and source generator

using System.Reflection;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using VLCLR.Imaging;
using VLCLR.Native;
using VLCLR.Plugin;
using VLCLR.Rendering;
using VLCLR.Text;

namespace SubtitleRenderer;

/// <summary>
/// Text renderer that renders styled subtitles using ImageSharp.
/// Uses the VLCLR framework base class and source generator for entry points.
/// </summary>
[VLCModule("dotnet_subtitle")]
[VLCCapability("text renderer", Score = 100)]
[VLCDescription(".NET Native AOT Text Renderer for Subtitles")]
[VLCConfig("dotnet-subtitle-force-outline", VLCConfigType.Bool, Default = true,
    Description = "Force text outline", LongDescription = "Always render text with an outline for better visibility")]
[VLCConfig("dotnet-subtitle-outline-width", VLCConfigType.Integer, Default = 3, Min = 1, Max = 10,
    Description = "Outline width", LongDescription = "Width of the text outline in pixels")]
[VLCConfig("dotnet-subtitle-force-white", VLCConfigType.Bool, Default = true,
    Description = "Force white text", LongDescription = "Force white text color when VLC sends black")]
public partial class SubtitleTextRenderer : VLCTextRendererBase
{
    // Reusable canvas instance for rendering (using framework TextCanvas)
    private TextCanvas? _canvas;
    private bool _forceOutline = true;
    private int _outlineWidth = 3;
    private bool _forceWhite = true;

    // Default canvas dimensions (used if region doesn't specify)
    private const int DefaultWidth = 1920;
    private const int DefaultHeight = 1080;

    // Debug output control
#if DEBUG
    private bool _debugOutputEnabled;
    private string? _debugOutputPath;
    private bool _firstRenderSaved;
#endif
    private long _renderCount;

    /// <summary>
    /// Called when the renderer opens. Initializes fonts and canvas.
    /// </summary>
    protected override bool OnOpen(VLCRendererContext context)
    {
        context.Logger.Info("[SubtitleTextRenderer] Opening text renderer");

        try
        {
            var config = Config;
            _forceOutline = config.ForceOutline;
            _outlineWidth = (int)Math.Clamp(config.OutlineWidth, 1, 10);
            _forceWhite = config.ForceWhite;

            // Initialize font manager with embedded JetBrains Mono font
            var assembly = Assembly.GetExecutingAssembly();
            FontManager.LoadEmbeddedFont(
                assembly,
                "SubtitleRenderer.Resources.JetBrainsMono-Regular.ttf",
                setAsDefault: true);

#if DEBUG
            // Check for debug output environment variable
            _debugOutputPath = Environment.GetEnvironmentVariable("DOTNET_SUBTITLE_DEBUG_PATH");
            _debugOutputEnabled = !string.IsNullOrEmpty(_debugOutputPath);

            if (_debugOutputEnabled)
            {
                context.Logger.Info($"[SubtitleTextRenderer] Debug output enabled: {_debugOutputPath}");
            }
#endif

            context.Logger.Info($"[SubtitleTextRenderer] Text renderer initialized (forceOutline={_forceOutline}, outlineWidth={_outlineWidth}, forceWhite={_forceWhite})");
            return true;
        }
        catch (Exception ex)
        {
            context.Logger.Error($"[SubtitleTextRenderer] Failed to initialize: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Called when the renderer closes. Cleans up resources.
    /// </summary>
    protected override void OnClose()
    {
        Context.Logger.Info($"[SubtitleTextRenderer] Closing renderer, rendered {_renderCount} subtitles");

        _canvas?.Dispose();
        _canvas = null;
    }

    /// <summary>
    /// Renders text to a subpicture region.
    /// </summary>
    protected override unsafe nint RenderText(VLCTextRequest request)
    {
        _renderCount++;

        // Parse text segments from the region with visibility optimization
        // Use RegionPtr to access the original region for styled segment parsing
        var segments = TextSegmentParser.ParseWithVisibility(
            RegionPtr,
            forceWhiteText: _forceWhite,
            forceOutline: _forceOutline,
            outlineWidth: _outlineWidth);

        // Log first few render calls for debugging
        if (_renderCount <= 5)
        {
            string description = TextSegmentParser.ParseAndDescribe(RegionPtr);
            Context.Logger.Info($"[SubtitleTextRenderer] Render #{_renderCount}: {description}");

            foreach (var segment in segments)
            {
                var style = segment.Style;
                Context.Logger.Info($"[SubtitleTextRenderer]   Segment: \"{segment.Text}\"");
                Context.Logger.Info($"[SubtitleTextRenderer]   Style: FG=#{style.ForegroundColor:X6}, Outline={style.HasOutline}, Width={style.OutlineWidth}px");
            }
        }

        // Skip empty text
        if (segments.Count == 0 || segments.TrueForAll(s => s.IsEmpty))
        {
            return 0;
        }

        // Get video dimensions from filter's format
        ref VLCFilter filter = ref Unsafe.AsRef<VLCFilter>((void*)Context.NativePtr);
        uint videoWidth = filter.FormatOut.Video.Width > 0 ? filter.FormatOut.Video.Width : (uint)DefaultWidth;
        uint videoHeight = filter.FormatOut.Video.Height > 0 ? filter.FormatOut.Video.Height : (uint)DefaultHeight;

        // Use video dimensions for canvas
        int canvasWidth = (int)videoWidth;
        int canvasHeight = (int)videoHeight;

        // Ensure minimum dimensions
        canvasWidth = Math.Max(canvasWidth, 320);
        canvasHeight = Math.Max(canvasHeight, 240);

        // Create or resize canvas
        if (_canvas == null)
        {
            _canvas = new TextCanvas(canvasWidth, canvasHeight);
        }
        else
        {
            _canvas.EnsureSize(canvasWidth, canvasHeight);
        }

        // Determine text alignment based on request
        TextAlignment alignment = request.HorizontalAlignment;

        // Render text segments to canvas using framework's TextCanvas
        _canvas.Render(segments, alignment);

#if DEBUG
        // Save debug image on first successful render
        if (_debugOutputEnabled && !_firstRenderSaved)
        {
            try
            {
                _canvas.SaveDebugImage(_debugOutputPath!);
                Context.Logger.Info($"[SubtitleTextRenderer] Saved debug image to: {_debugOutputPath}");
                _firstRenderSaved = true;
            }
            catch (Exception ex)
            {
                Context.Logger.Warning($"[SubtitleTextRenderer] Failed to save debug image: {ex.Message}");
                _firstRenderSaved = true; // Don't try again
            }
        }
#endif

        // Get the rendered image
        Image<Rgba32>? image = _canvas.GetImage();
        if (image == null)
        {
            return 0;
        }

        // Convert to VLC subpicture region using the chroma list from VLC
        nint outputRegionPtr = PictureConverter.ToSubpictureRegion(image, ChromaListPtr);

        if (outputRegionPtr != 0 && _renderCount <= 5)
        {
            Context.Logger.Info($"[SubtitleTextRenderer] Created region {canvasWidth}x{canvasHeight}, alignment={alignment}");
        }

        return outputRegionPtr;
    }
}
