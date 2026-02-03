// VLC Video Filter Base Class
// Provides a base class for video filter plugins with lifecycle management
// VLC Version: 4.0.6

using System.Runtime.InteropServices;
using VLCLR.Module;
using VLCLR.Native;

namespace VLCLR.Plugin;

/// <summary>
/// Base class for video filter plugins. Handles lifecycle, state management,
/// and error handling. Subclass and override ProcessFrame().
/// </summary>
public abstract class VLCVideoFilterBase : IDisposable
{
    private nint _filterPtr;
    private long _frameCount;
    private bool _initialized;
    private bool _firstFrame = true;
    private bool _disposed;

    private VLCFilterContext _context;
    private GCHandle _filterOpsHandle;
    private VLCFilterOperations _filterOps;

    /// <summary>
    /// Current frame count (increments each frame).
    /// </summary>
    protected long FrameCount => Interlocked.Read(ref _frameCount);

    /// <summary>
    /// Whether the filter is initialized.
    /// </summary>
    protected bool IsInitialized => _initialized;

    /// <summary>
    /// Access to filter context (format, logger, etc.).
    /// </summary>
    protected VLCFilterContext Context => _context;

    /// <summary>
    /// Called when filter opens. Override to perform custom initialization.
    /// Base implementation does nothing. Return false to fail open.
    /// </summary>
    /// <param name="context">The filter context with format and logging access</param>
    protected virtual bool OnOpen(VLCFilterContext context) => true;

    /// <summary>
    /// Called when filter closes. Override to perform custom cleanup.
    /// Base implementation does nothing.
    /// </summary>
    protected virtual void OnClose() { }

    /// <summary>
    /// Called when video format changes or flush is requested.
    /// Override to reset any frame-specific state.
    /// </summary>
    protected virtual void OnFlush() { }

    /// <summary>
    /// Process a video frame. Override to implement filter logic.
    /// </summary>
    /// <param name="frame">The video frame to process (in-place modification).</param>
    protected abstract void ProcessFrame(VLCFrame frame);

    /// <summary>
    /// Called on first frame. Override for one-time setup that needs frame info.
    /// </summary>
    /// <param name="frame">The first video frame</param>
    protected virtual void OnFirstFrame(VLCFrame frame) { }

    /// <summary>
    /// Internal: Called by the Open callback to initialize the filter.
    /// </summary>
    /// <param name="filterPtr">Pointer to filter_t</param>
    /// <param name="filterVideo">Function pointer for FilterVideo callback</param>
    /// <param name="close">Function pointer for Close callback</param>
    /// <param name="flush">Optional function pointer for Flush callback</param>
    /// <returns>0 on success, non-zero on failure</returns>
    internal unsafe int InternalOpen(nint filterPtr, delegate* unmanaged<nint, nint, nint> filterVideo,
        delegate* unmanaged<nint, void> close, delegate* unmanaged<nint, void> flush = null)
    {
        try
        {
            _filterPtr = filterPtr;
            _context = new VLCFilterContext(filterPtr);

            // Log filter opening
            _context.Logger.Info($"[VLCLR] Opening video filter: {GetType().Name}");
            _context.Logger.Info($"[VLCLR] Format: {_context.Width}x{_context.Height} {_context.ChromaString}");

            // Call derived class initialization
            if (!OnOpen(_context))
            {
                _context.Logger.Error($"[VLCLR] Filter {GetType().Name} OnOpen() returned false");
                return -1;
            }

            // Set up filter operations
            _filterOps = new VLCFilterOperations
            {
                FilterVideo = (nint)filterVideo,
                Close = (nint)close,
                Flush = flush != null ? (nint)flush : 0,
            };

            // Pin the operations struct
            _filterOpsHandle = GCHandle.Alloc(_filterOps, GCHandleType.Pinned);
            _context.SetOperations(_filterOpsHandle.AddrOfPinnedObject());

            _initialized = true;
            _context.Logger.Info($"[VLCLR] Filter {GetType().Name} initialized successfully");

            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                _context.Logger.Error($"[VLCLR] Exception in filter Open: {ex.Message}");
            }
            catch
            {
                // Ignore logging failures
            }
            return -1;
        }
    }

    /// <summary>
    /// Internal: Called by the FilterVideo callback to process a frame.
    /// Handles exception safety - on error, returns the input picture unchanged.
    /// </summary>
    /// <param name="filterPtr">Pointer to filter_t</param>
    /// <param name="picturePtr">Pointer to picture_t</param>
    /// <returns>The picture pointer (same as input for in-place filters)</returns>
    internal nint InternalFilterVideo(nint filterPtr, nint picturePtr)
    {
        try
        {
            if (!_initialized || picturePtr == 0)
                return picturePtr;

            var frame = new VLCFrame(picturePtr, _context);

            // Handle first frame
            if (_firstFrame)
            {
                _firstFrame = false;
                OnFirstFrame(frame);
            }

            // Process the frame
            ProcessFrame(frame);

            // Increment frame count
            Interlocked.Increment(ref _frameCount);

            return picturePtr;
        }
        catch (Exception ex)
        {
            // Log error but don't crash - return picture unchanged
            try
            {
                _context.Logger.Error($"[VLCLR] Exception in FilterVideo: {ex.Message}");
            }
            catch
            {
                // Ignore logging failures
            }
            return picturePtr;
        }
    }

    /// <summary>
    /// Internal: Called by the Close callback to clean up.
    /// </summary>
    /// <param name="filterPtr">Pointer to filter_t</param>
    internal void InternalClose(nint filterPtr)
    {
        try
        {
            _context.Logger.Info($"[VLCLR] Closing video filter: {GetType().Name} (processed {FrameCount} frames)");

            OnClose();

            // Free pinned operations
            if (_filterOpsHandle.IsAllocated)
            {
                _filterOpsHandle.Free();
            }

            _initialized = false;
        }
        catch (Exception ex)
        {
            try
            {
                _context.Logger.Error($"[VLCLR] Exception in filter Close: {ex.Message}");
            }
            catch
            {
                // Ignore logging failures
            }
        }
    }

    /// <summary>
    /// Internal: Called by the Flush callback.
    /// </summary>
    /// <param name="filterPtr">Pointer to filter_t</param>
    internal void InternalFlush(nint filterPtr)
    {
        try
        {
            OnFlush();
            _firstFrame = true; // Reset first frame flag on flush
        }
        catch (Exception ex)
        {
            try
            {
                _context.Logger.Error($"[VLCLR] Exception in filter Flush: {ex.Message}");
            }
            catch
            {
                // Ignore logging failures
            }
        }
    }

    /// <summary>
    /// Disposes the filter instance.
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
        if (_filterOpsHandle.IsAllocated)
        {
            _filterOpsHandle.Free();
        }

        _disposed = true;
    }

    /// <summary>
    /// Finalizer.
    /// </summary>
    ~VLCVideoFilterBase()
    {
        Dispose(false);
    }
}
