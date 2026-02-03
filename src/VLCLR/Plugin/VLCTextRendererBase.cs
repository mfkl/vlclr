// VLC Text Renderer Base Class
// Provides a base class for text renderer plugins with lifecycle management
// VLC Version: 4.0.6

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
    private nint _currentRegionPtr;

    private VLCRendererContext _context;
    private GCHandle _opsHandle;
    private VLCTextRendererOperations _ops;

    /// <summary>
    /// Whether the renderer is initialized.
    /// </summary>
    protected bool IsInitialized => _initialized;

    /// <summary>
    /// Access to renderer context (logger, etc.).
    /// </summary>
    protected VLCRendererContext Context => _context;

    /// <summary>
    /// Pointer to the current chroma list (null-terminated array of supported chromas).
    /// Only valid during RenderText call.
    /// </summary>
    protected nint ChromaListPtr => _currentChromaListPtr;

    /// <summary>
    /// Pointer to the current subpicture region being rendered.
    /// Only valid during RenderText call. Use this to access text segments directly.
    /// </summary>
    protected nint RegionPtr => _currentRegionPtr;

    /// <summary>
    /// Called when renderer opens. Override to perform custom initialization.
    /// Return false to fail open.
    /// </summary>
    /// <param name="context">The renderer context</param>
    protected virtual bool OnOpen(VLCRendererContext context) => true;

    /// <summary>
    /// Called when renderer closes. Override to perform custom cleanup.
    /// </summary>
    protected virtual void OnClose() { }

    /// <summary>
    /// Render text to a subpicture region. Override to implement rendering logic.
    /// </summary>
    /// <param name="request">The text rendering request (text, style, bounds).</param>
    /// <returns>A pointer to the rendered subpicture region, or 0 on failure.</returns>
    /// <remarks>
    /// The returned region must be allocated via VLC's subpicture_region_New.
    /// Ownership is transferred to VLC - do not free it yourself.
    /// </remarks>
    protected abstract nint RenderText(VLCTextRequest request);

    /// <summary>
    /// Internal: Called by the Open callback to initialize the renderer.
    /// </summary>
    /// <param name="filterPtr">Pointer to filter_t</param>
    /// <param name="render">Function pointer for Render callback</param>
    /// <param name="close">Function pointer for Close callback</param>
    /// <returns>0 on success, non-zero on failure</returns>
    public unsafe int InternalOpen(nint filterPtr, delegate* unmanaged<nint, nint, nint, nint> render,
        delegate* unmanaged<nint, void> close)
    {
        try
        {
            _filterPtr = filterPtr;
            _context = new VLCRendererContext(filterPtr);

            // Log renderer opening
            _context.Logger.Info($"[VLCLR] Opening text renderer: {GetType().Name}");

            // Call derived class initialization
            if (!OnOpen(_context))
            {
                _context.Logger.Error($"[VLCLR] Renderer {GetType().Name} OnOpen() returned false");
                return -1;
            }

            // Set up renderer operations
            _ops = new VLCTextRendererOperations
            {
                Render = (nint)render,
                Close = (nint)close,
            };

            // Pin the operations struct and set it on the filter
            _opsHandle = GCHandle.Alloc(_ops, GCHandleType.Pinned);
            SetOperations(_opsHandle.AddrOfPinnedObject());

            _initialized = true;
            _context.Logger.Info($"[VLCLR] Renderer {GetType().Name} initialized successfully");

            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                _context.Logger.Error($"[VLCLR] Exception in renderer Open: {ex.Message}");
            }
            catch
            {
                // Ignore logging failures
            }
            return -1;
        }
    }

    /// <summary>
    /// Internal: Called by the Render callback to render text.
    /// </summary>
    /// <param name="filterPtr">Pointer to filter_t</param>
    /// <param name="regionPtr">Pointer to input subpicture_region_t with text</param>
    /// <param name="chromaListPtr">Pointer to NULL-terminated chroma array</param>
    /// <returns>Pointer to output subpicture_region_t, or 0 on failure</returns>
    public nint InternalRender(nint filterPtr, nint regionPtr, nint chromaListPtr)
    {
        try
        {
            if (!_initialized || regionPtr == 0)
                return 0;

            // Store pointers for derived class access
            _currentChromaListPtr = chromaListPtr;
            _currentRegionPtr = regionPtr;

            // Parse the input region to extract text and style
            var request = ParseRegion(regionPtr);
            if (!request.HasText)
                return 0;

            // Call the derived class to render
            return RenderText(request);
        }
        catch (Exception ex)
        {
            // Log error but don't crash - return null region
            try
            {
                _context.Logger.Error($"[VLCLR] Exception in Render: {ex.Message}");
            }
            catch
            {
                // Ignore logging failures
            }
            return 0;
        }
    }

    /// <summary>
    /// Internal: Called by the Close callback to clean up.
    /// </summary>
    /// <param name="filterPtr">Pointer to filter_t</param>
    public void InternalClose(nint filterPtr)
    {
        try
        {
            _context.Logger.Info($"[VLCLR] Closing text renderer: {GetType().Name}");

            OnClose();

            // Free pinned operations
            if (_opsHandle.IsAllocated)
            {
                _opsHandle.Free();
            }

            _initialized = false;
        }
        catch (Exception ex)
        {
            try
            {
                _context.Logger.Error($"[VLCLR] Exception in renderer Close: {ex.Message}");
            }
            catch
            {
                // Ignore logging failures
            }
        }
    }

    /// <summary>
    /// Parses a VLC subpicture region to extract text rendering request data.
    /// </summary>
    private unsafe VLCTextRequest ParseRegion(nint regionPtr)
    {
        var region = *(VLCSubpictureRegion*)regionPtr;

        // Extract text from text segments
        string text = TextSegmentParser.ParseText(region.Text);

        // Extract style from first segment (if available)
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

    /// <summary>
    /// Sets the filter operations pointer on the native filter.
    /// </summary>
    private unsafe void SetOperations(nint opsPtr)
    {
        if (_filterPtr == 0) return;
        var filter = (VLCFilter*)_filterPtr;
        filter->Operations = opsPtr;
    }

    /// <summary>
    /// Disposes the renderer instance.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes managed and unmanaged resources.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false from finalizer</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            // Clean up managed resources
        }

        // Clean up unmanaged resources
        if (_opsHandle.IsAllocated)
        {
            _opsHandle.Free();
        }

        _disposed = true;
    }

    /// <summary>
    /// Finalizer.
    /// </summary>
    ~VLCTextRendererBase()
    {
        Dispose(false);
    }
}
