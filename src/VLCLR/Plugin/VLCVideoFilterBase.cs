// VLC Video Filter Base Class
// Provides a base class for video filter plugins with lifecycle management
// VLC Version: 3.0.24

using System.Runtime.InteropServices;
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

    protected long FrameCount => Interlocked.Read(ref _frameCount);

    protected bool IsInitialized => _initialized;

    protected VLCFilterContext Context => _context;

    protected virtual bool OnOpen(VLCFilterContext context) => true;

    protected virtual void OnClose() { }

    protected virtual void OnFlush() { }

    protected virtual void ProcessFrame(VLCFrame frame) { }

    protected virtual nint ProcessFrameToOutput(VLCFrame frame)
    {
        ProcessFrame(frame);
        return frame.NativePtr;
    }

    protected virtual void OnFirstFrame(VLCFrame frame) { }

    /// <summary>
    /// Internal: Called by the Open callback to initialize the filter.
    /// In VLC 3, callbacks are set directly on filter_t by the generated entry code.
    /// </summary>
    public unsafe int InternalOpen(nint filterPtr)
    {
        try
        {
            _filterPtr = filterPtr;
            _context = new VLCFilterContext(filterPtr);

            _context.Logger.Info($"[VLCLR] Opening video filter: {GetType().Name}");
            _context.Logger.Info($"[VLCLR] Format: {_context.Width}x{_context.Height} {_context.ChromaString}");

            if (!OnOpen(_context))
            {
                _context.Logger.Error($"[VLCLR] Filter {GetType().Name} OnOpen() returned false");
                return -1;
            }

            _initialized = true;
            _context.Logger.Info($"[VLCLR] Filter {GetType().Name} initialized successfully");

            return 0;
        }
        catch (Exception ex)
        {
            try { _context.Logger.Error($"[VLCLR] Exception in filter Open: {ex.Message}"); }
            catch { }
            return -1;
        }
    }

    public nint InternalFilterVideo(nint filterPtr, nint picturePtr)
    {
        try
        {
            if (!_initialized || picturePtr == 0)
                return picturePtr;

            var frame = new VLCFrame(picturePtr, _context);

            if (_firstFrame)
            {
                _firstFrame = false;
                OnFirstFrame(frame);
            }

            nint outputPicture = ProcessFrameToOutput(frame);

            Interlocked.Increment(ref _frameCount);

            if (outputPicture != picturePtr)
            {
                if (outputPicture != 0)
                {
                    VLCCore.PictureCopyProperties(outputPicture, picturePtr);
                }
                VLCCore.PictureRelease(picturePtr);
            }

            return outputPicture;
        }
        catch (Exception ex)
        {
            try { _context.Logger.Error($"[VLCLR] Exception in FilterVideo: {ex.Message}"); }
            catch { }
            return picturePtr;
        }
    }

    public void InternalClose(nint filterPtr)
    {
        try
        {
            _context.Logger.Info($"[VLCLR] Closing video filter: {GetType().Name} (processed {FrameCount} frames)");
            OnClose();
            _initialized = false;
        }
        catch (Exception ex)
        {
            try { _context.Logger.Error($"[VLCLR] Exception in filter Close: {ex.Message}"); }
            catch { }
        }
    }

    public void InternalFlush(nint filterPtr)
    {
        try
        {
            OnFlush();
            _firstFrame = true;
        }
        catch (Exception ex)
        {
            try { _context.Logger.Error($"[VLCLR] Exception in filter Flush: {ex.Message}"); }
            catch { }
        }
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

    ~VLCVideoFilterBase()
    {
        Dispose(false);
    }
}
