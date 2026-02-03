// Filter operations builder for VLC video filters
// Creates and pins filter_operations structures for VLC

using System.Runtime.InteropServices;
using VLCLR.Native;

namespace VLCLR.Module;

/// <summary>
/// Delegate type for video filter callbacks.
/// </summary>
/// <param name="filterPtr">Pointer to the filter_t structure.</param>
/// <param name="picturePtr">Pointer to the input picture_t.</param>
/// <returns>Pointer to the output picture_t (may be same as input for in-place filters).</returns>
public unsafe delegate nint FilterVideoDelegate(nint filterPtr, nint picturePtr);

/// <summary>
/// Delegate type for filter close callbacks.
/// </summary>
/// <param name="filterPtr">Pointer to the filter_t structure.</param>
public unsafe delegate void FilterCloseDelegate(nint filterPtr);

/// <summary>
/// Delegate type for filter flush callbacks.
/// </summary>
/// <param name="filterPtr">Pointer to the filter_t structure.</param>
public unsafe delegate void FilterFlushDelegate(nint filterPtr);

/// <summary>
/// Delegate type for filter drain callbacks.
/// </summary>
/// <param name="filterPtr">Pointer to the filter_t structure.</param>
/// <returns>Pointer to the drained picture_t, or nint.Zero if none.</returns>
public unsafe delegate nint FilterDrainDelegate(nint filterPtr);

/// <summary>
/// Builds and manages VLC filter_operations structures.
/// Handles pinning of the structure and callback function pointers.
/// </summary>
/// <remarks>
/// The built operations structure is pinned in memory and must be kept alive
/// for as long as VLC uses the filter. Call Dispose() when the filter is closed.
/// </remarks>
public sealed class FilterOpsBuilder : IDisposable
{
    private VLCFilterOperations _ops;
    private GCHandle _handle;
    private bool _pinned;
    private bool _disposed;

    /// <summary>
    /// Gets the pointer to the pinned operations structure.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if Build() hasn't been called.</exception>
    public nint Pointer
    {
        get
        {
            if (!_pinned)
                throw new InvalidOperationException("Build() must be called before accessing Pointer.");
            return _handle.AddrOfPinnedObject();
        }
    }

    /// <summary>
    /// Sets the video filter callback.
    /// </summary>
    /// <param name="callback">Function pointer to the filter callback.</param>
    /// <returns>This builder for chaining.</returns>
    public unsafe FilterOpsBuilder WithFilterVideo(delegate* unmanaged[Cdecl]<nint, nint, nint> callback)
    {
        _ops.FilterVideo = (nint)callback;
        return this;
    }

    /// <summary>
    /// Sets the close callback.
    /// </summary>
    /// <param name="callback">Function pointer to the close callback.</param>
    /// <returns>This builder for chaining.</returns>
    public unsafe FilterOpsBuilder WithClose(delegate* unmanaged[Cdecl]<nint, void> callback)
    {
        _ops.Close = (nint)callback;
        return this;
    }

    /// <summary>
    /// Sets the flush callback.
    /// </summary>
    /// <param name="callback">Function pointer to the flush callback.</param>
    /// <returns>This builder for chaining.</returns>
    public unsafe FilterOpsBuilder WithFlush(delegate* unmanaged[Cdecl]<nint, void> callback)
    {
        _ops.Flush = (nint)callback;
        return this;
    }

    /// <summary>
    /// Sets the drain callback.
    /// </summary>
    /// <param name="callback">Function pointer to the drain callback.</param>
    /// <returns>This builder for chaining.</returns>
    public unsafe FilterOpsBuilder WithDrain(delegate* unmanaged[Cdecl]<nint, nint> callback)
    {
        _ops.Drain = (nint)callback;
        return this;
    }

    /// <summary>
    /// Sets the change viewpoint callback.
    /// </summary>
    /// <param name="callback">Function pointer to the callback.</param>
    /// <returns>This builder for chaining.</returns>
    public unsafe FilterOpsBuilder WithChangeViewpoint(nint callback)
    {
        _ops.ChangeViewpoint = callback;
        return this;
    }

    /// <summary>
    /// Sets the video mouse callback.
    /// </summary>
    /// <param name="callback">Function pointer to the callback.</param>
    /// <returns>This builder for chaining.</returns>
    public unsafe FilterOpsBuilder WithVideoMouse(nint callback)
    {
        _ops.VideoMouse = callback;
        return this;
    }

    /// <summary>
    /// Builds and pins the operations structure.
    /// After calling this, use Pointer to get the address for VLC.
    /// </summary>
    /// <returns>This builder (for accessing Pointer).</returns>
    /// <exception cref="InvalidOperationException">Thrown if Build() was already called.</exception>
    public FilterOpsBuilder Build()
    {
        if (_pinned)
            throw new InvalidOperationException("Build() can only be called once.");

        _handle = GCHandle.Alloc(_ops, GCHandleType.Pinned);
        _pinned = true;
        return this;
    }

    /// <summary>
    /// Releases the pinned memory.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_pinned && _handle.IsAllocated)
        {
            _handle.Free();
            _pinned = false;
        }
    }

    /// <summary>
    /// Creates a new filter operations builder.
    /// </summary>
    /// <returns>A new builder instance.</returns>
    public static FilterOpsBuilder Create() => new();
}

/// <summary>
/// Static helper for creating filter operations without managing lifetime.
/// Use when the operations should live for the entire plugin lifetime.
/// </summary>
public static class FilterOps
{
    private static readonly object _lock = new();
    private static readonly List<FilterOpsBuilder> _builders = new();

    /// <summary>
    /// Creates and pins a video filter operations structure.
    /// The structure remains pinned until the application exits.
    /// </summary>
    /// <param name="filterVideo">Video filter callback (required).</param>
    /// <param name="close">Close callback (optional but recommended).</param>
    /// <returns>Pointer to the pinned operations structure.</returns>
    public static unsafe nint CreateVideoFilter(
        delegate* unmanaged[Cdecl]<nint, nint, nint> filterVideo,
        delegate* unmanaged[Cdecl]<nint, void> close = null)
    {
        var builder = FilterOpsBuilder.Create()
            .WithFilterVideo(filterVideo);

        if (close != null)
        {
            builder.WithClose(close);
        }

        builder.Build();

        lock (_lock)
        {
            _builders.Add(builder);
        }

        return builder.Pointer;
    }

    /// <summary>
    /// Creates and pins a video filter operations structure with all callbacks.
    /// </summary>
    /// <param name="filterVideo">Video filter callback.</param>
    /// <param name="close">Close callback.</param>
    /// <param name="flush">Flush callback.</param>
    /// <param name="drain">Drain callback.</param>
    /// <returns>Pointer to the pinned operations structure.</returns>
    public static unsafe nint CreateVideoFilterFull(
        delegate* unmanaged[Cdecl]<nint, nint, nint> filterVideo,
        delegate* unmanaged[Cdecl]<nint, void> close,
        delegate* unmanaged[Cdecl]<nint, void> flush,
        delegate* unmanaged[Cdecl]<nint, nint> drain)
    {
        var builder = FilterOpsBuilder.Create()
            .WithFilterVideo(filterVideo)
            .WithClose(close)
            .WithFlush(flush)
            .WithDrain(drain)
            .Build();

        lock (_lock)
        {
            _builders.Add(builder);
        }

        return builder.Pointer;
    }
}
