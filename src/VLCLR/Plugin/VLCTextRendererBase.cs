// VLC Text Renderer Base Class
// Provides a base class for text renderer plugins with lifecycle management
// VLC Version: 3.0.24

using System.Runtime.InteropServices;
using VLCLR.Native;
using VLCLR.Text;

namespace VLCLR.Plugin;

/// <summary>
/// Base class for text renderer plugins. Handles lifecycle, state management,
/// and error handling. Subclass and override RenderText().
/// </summary>
public abstract class VLCTextRendererBase : IDisposable
{
    private nint _filterPtr;
    private bool _initialized;
    private bool _disposed;
    private nint _currentChromaListPtr;

    private VLCRendererContext _context;

    protected bool IsInitialized => _initialized;

    protected VLCRendererContext Context => _context;

    protected nint ChromaListPtr => _currentChromaListPtr;

    protected virtual bool OnOpen(VLCRendererContext context) => true;

    protected virtual void OnClose() { }

    protected abstract void RenderText(VLCTextRequest request, nint destinationRegionPtr, nint sourceRegionPtr);

    /// <summary>
    /// Internal: Called by the Open callback to initialize the renderer.
    /// </summary>
    public unsafe int InternalOpen(nint filterPtr)
    {
        try
        {
            _filterPtr = filterPtr;
            _context = new VLCRendererContext(filterPtr);

            _context.Logger.Info($"[VLCLR] Opening text renderer: {GetType().Name}");

            if (!OnOpen(_context))
            {
                _context.Logger.Error($"[VLCLR] Renderer {GetType().Name} OnOpen() returned false");
                return -1;
            }

            _initialized = true;
            _context.Logger.Info($"[VLCLR] Renderer {GetType().Name} initialized successfully");

            return 0;
        }
        catch (Exception ex)
        {
            try { _context.Logger.Error($"[VLCLR] Exception in renderer Open: {ex.Message}"); }
            catch { }
            return -1;
        }
    }

    public int InternalRender(nint filterPtr, nint destinationPtr, nint sourcePtr, nint chromaListPtr)
    {
        try
        {
            if (!_initialized || destinationPtr == 0 || sourcePtr == 0)
                return -1;

            _currentChromaListPtr = chromaListPtr;

            var request = ParseRegion(sourcePtr);
            if (!request.HasText)
                return -1;

            RenderText(request, destinationPtr, sourcePtr);
            return 0;
        }
        catch (Exception ex)
        {
            try { _context.Logger.Error($"[VLCLR] Exception in Render: {ex.Message}"); }
            catch { }
            return -1;
        }
    }

    public void InternalClose(nint filterPtr)
    {
        try
        {
            _context.Logger.Info($"[VLCLR] Closing text renderer: {GetType().Name}");
            OnClose();
            _initialized = false;
        }
        catch (Exception ex)
        {
            try { _context.Logger.Error($"[VLCLR] Exception in renderer Close: {ex.Message}"); }
            catch { }
        }
    }

    private unsafe VLCTextRequest ParseRegion(nint regionPtr)
    {
        var region = *(VLCSubpictureRegion*)regionPtr;

        string text = TextSegmentParser.ParseText(region.Text);

        VLCTextStyle style = default;
        if (region.Text != 0)
        {
            var segment = *(VLCTextSegment*)region.Text;
            if (segment.Style != 0)
            {
                style = *(VLCTextStyle*)segment.Style;
            }
        }

        return new VLCTextRequest(
            text,
            style,
            region.MaxWidth,
            region.MaxHeight,
            region.Align
        );
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;
    }

    ~VLCTextRendererBase()
    {
        Dispose(false);
    }
}
