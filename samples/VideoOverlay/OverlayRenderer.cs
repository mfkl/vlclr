using System.Reflection;
using System.Runtime.InteropServices;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace VideoOverlay;

/// <summary>
/// Renders the debug overlay using ImageSharp.
/// </summary>
public class OverlayRenderer : IDisposable
{
    private readonly Font _font;
    private readonly Image<Rgba32> _overlay;
    private readonly byte[] _pixelBuffer;
    private readonly string _runtimeInfo;

    /// <summary>
    /// Width of the overlay in pixels.
    /// </summary>
    public int OverlayWidth { get; }

    /// <summary>
    /// Height of the overlay in pixels.
    /// </summary>
    public int OverlayHeight { get; }

    public OverlayRenderer()
    {
        // Load font from embedded resource
        _font = LoadEmbeddedFont();

        // Cache runtime info (doesn't change)
        _runtimeInfo = RuntimeInformation.FrameworkDescription;

        // Overlay dimensions
        OverlayWidth = 300;
        OverlayHeight = 90;

        // Pre-allocate overlay image
        _overlay = new Image<Rgba32>(OverlayWidth, OverlayHeight);

        // Pre-allocate pixel buffer
        _pixelBuffer = new byte[OverlayWidth * OverlayHeight * 4];

        Console.Error.WriteLine($"[VideoOverlay] OverlayRenderer created: {OverlayWidth}x{OverlayHeight}, font loaded");
    }

    private Font LoadEmbeddedFont()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "VideoOverlay.Resources.JetBrainsMono-Regular.ttf";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            // List available resources for debugging
            var resources = assembly.GetManifestResourceNames();
            Console.Error.WriteLine($"[VideoOverlay] Available resources: {string.Join(", ", resources)}");
            throw new InvalidOperationException($"Font resource not found: {resourceName}");
        }

        var collection = new FontCollection();
        var family = collection.Add(stream);
        return family.CreateFont(14, FontStyle.Regular);
    }

    /// <summary>
    /// Render the overlay with current frame count and GC stats.
    /// </summary>
    public void RenderOverlay(long frameCount)
    {
        // Clear to transparent
        _overlay.Mutate(ctx => ctx.Clear(Color.Transparent));

        // Draw semi-transparent background
        var bgColor = Color.FromRgba(0, 0, 0, 180);
        _overlay.Mutate(ctx => ctx.Fill(bgColor, new RectangleF(0, 0, OverlayWidth, OverlayHeight)));

        // Draw border
        var borderColor = Color.FromRgba(100, 100, 100, 200);
        _overlay.Mutate(ctx =>
        {
            // Top border
            ctx.Fill(borderColor, new RectangleF(0, 0, OverlayWidth, 2));
            // Bottom border
            ctx.Fill(borderColor, new RectangleF(0, OverlayHeight - 2, OverlayWidth, 2));
            // Left border
            ctx.Fill(borderColor, new RectangleF(0, 0, 2, OverlayHeight));
            // Right border
            ctx.Fill(borderColor, new RectangleF(OverlayWidth - 2, 0, 2, OverlayHeight));
        });

        // Get GC stats
        int gc0 = GC.CollectionCount(0);
        double heapMB = GC.GetTotalMemory(false) / 1_000_000.0;

        // Draw text lines
        var textColor = Color.White;
        float x = 10;
        float y = 10;
        float lineHeight = 22;

        // Line 1: Runtime info
        var options1 = new RichTextOptions(_font) { Origin = new PointF(x, y) };
        _overlay.Mutate(ctx => ctx.DrawText(options1, $"Hello from {_runtimeInfo}", textColor));

        // Line 2: Frame counter
        var options2 = new RichTextOptions(_font) { Origin = new PointF(x, y + lineHeight) };
        _overlay.Mutate(ctx => ctx.DrawText(options2, $"Frame: {frameCount}", textColor));

        // Line 3: GC stats
        var options3 = new RichTextOptions(_font) { Origin = new PointF(x, y + lineHeight * 2) };
        _overlay.Mutate(ctx => ctx.DrawText(options3, $"GC0: {gc0}  Heap: {heapMB:F1} MB", textColor));
    }

    /// <summary>
    /// Get the overlay pixels as RGBA byte array.
    /// </summary>
    public byte[] GetOverlayPixels()
    {
        // Copy pixels to buffer
        _overlay.CopyPixelDataTo(_pixelBuffer);
        return _pixelBuffer;
    }

    /// <summary>
    /// Save the current overlay to a file for debugging.
    /// </summary>
    public void SaveOverlayToFile(string path)
    {
        _overlay.SaveAsPng(path);
    }

    public void Dispose()
    {
        _overlay.Dispose();
    }
}
