// Filter operations builder for VLC video filters
// In VLC 3, callbacks are set directly on filter_t — no operations struct.
// This builder pins function pointers for the plugin's Open callback.

using System.Runtime.InteropServices;
using VLCLR.Native;

namespace VLCLR.Module;

/// <summary>
/// Delegate type for video filter callbacks.
/// </summary>
public unsafe delegate nint FilterVideoDelegate(nint filterPtr, nint picturePtr);

/// <summary>
/// Delegate type for filter close callbacks.
/// </summary>
public unsafe delegate void FilterCloseDelegate(nint filterPtr);

/// <summary>
/// Delegate type for filter flush callbacks.
/// </summary>
public unsafe delegate void FilterFlushDelegate(nint filterPtr);

/// <summary>
/// Delegate type for filter drain callbacks.
/// </summary>
public unsafe delegate nint FilterDrainDelegate(nint filterPtr);

/// <summary>
/// Pins delegate instances so their function pointers remain stable for VLC.
/// In VLC 3, filter_t has inline callback fields (pf_video, pf_close, etc.)
/// rather than a separate operations struct.
/// </summary>
public sealed class FilterOpsBuilder : IDisposable
{
    private bool _disposed;

    public nint VideoFnPtr { get; private set; }
    public nint CloseFnPtr { get; private set; }
    public nint FlushFnPtr { get; private set; }
    public nint DrainFnPtr { get; private set; }

    public unsafe FilterOpsBuilder WithFilterVideo(delegate* unmanaged[Cdecl]<nint, nint, nint> callback)
    {
        VideoFnPtr = (nint)callback;
        return this;
    }

    public unsafe FilterOpsBuilder WithClose(delegate* unmanaged[Cdecl]<nint, void> callback)
    {
        CloseFnPtr = (nint)callback;
        return this;
    }

    public unsafe FilterOpsBuilder WithFlush(delegate* unmanaged[Cdecl]<nint, void> callback)
    {
        FlushFnPtr = (nint)callback;
        return this;
    }

    public unsafe FilterOpsBuilder WithDrain(delegate* unmanaged[Cdecl]<nint, nint> callback)
    {
        DrainFnPtr = (nint)callback;
        return this;
    }

    public FilterOpsBuilder Build()
    {
        return this;
    }

    public static FilterOpsBuilder Create() => new();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}

/// <summary>
/// Static helper for creating filter operations without managing lifetime.
/// </summary>
public static class FilterOps
{
    private static readonly object _lock = new();
    private static readonly List<FilterOpsBuilder> _builders = new();

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

        return builder.VideoFnPtr;
    }

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

        return builder.VideoFnPtr;
    }
}
