// VLC Filter Context wrapper
// Provides safe access to filter information
// VLC Version: 4.0.6

using System.Runtime.CompilerServices;
using VLCLR.Native;

namespace VLCLR.Plugin;

/// <summary>
/// Provides safe access to VLC filter context information.
/// Wraps the native filter_t pointer and exposes commonly needed properties.
/// </summary>
public readonly struct VLCFilterContext
{
    private readonly nint _filterPtr;

    /// <summary>
    /// Creates a filter context from a native filter pointer.
    /// </summary>
    /// <param name="filterPtr">Pointer to filter_t</param>
    public VLCFilterContext(nint filterPtr)
    {
        _filterPtr = filterPtr;
    }

    /// <summary>
    /// Gets a logger bound to this filter's VLC object.
    /// </summary>
    public VLCLogger Logger => new(_filterPtr);

    /// <summary>
    /// Gets the input video format.
    /// </summary>
    public VLCVideoFormat InputFormat
    {
        get
        {
            if (_filterPtr == 0) return default;
            return GetFilter().FormatIn.Video;
        }
    }

    /// <summary>
    /// Gets the output video format.
    /// </summary>
    public VLCVideoFormat OutputFormat
    {
        get
        {
            if (_filterPtr == 0) return default;
            return GetFilter().FormatOut.Video;
        }
    }

    /// <summary>
    /// Gets the video width from the input format.
    /// </summary>
    public int Width => (int)InputFormat.VisibleWidth;

    /// <summary>
    /// Gets the video height from the input format.
    /// </summary>
    public int Height => (int)InputFormat.VisibleHeight;

    /// <summary>
    /// Gets the video chroma (pixel format) as a FourCC value.
    /// </summary>
    public uint Chroma => InputFormat.Chroma;

    /// <summary>
    /// Gets the chroma format as a readable string (e.g., "RV32", "I420").
    /// </summary>
    public string ChromaString => VLCFourCC.ToString(Chroma);

    /// <summary>
    /// Gets the native filter pointer for advanced use.
    /// </summary>
    public nint NativePtr => _filterPtr;

    /// <summary>
    /// Propagates the input hardware video context to this filter's output.
    /// Call ReleaseOutputVideoContext when the filter closes.
    /// </summary>
    public unsafe bool PassThroughVideoContext()
    {
        if (_filterPtr == 0)
        {
            return false;
        }

        VLCFilter* filter = (VLCFilter*)_filterPtr;
        if (filter->VideoContextIn == 0)
        {
            return false;
        }
        if (filter->VideoContextOut != 0)
        {
            return true;
        }

        filter->VideoContextOut = VLCCore.VideoContextHold(
            filter->VideoContextIn);
        return filter->VideoContextOut != 0;
    }

    /// <summary>
    /// Gets the retained hardware video context configured for filter output.
    /// </summary>
    public unsafe nint OutputVideoContext
    {
        get
        {
            if (_filterPtr == 0)
            {
                return 0;
            }

            return ((VLCFilter*)_filterPtr)->VideoContextOut;
        }
    }

    /// <summary>
    /// Releases a hardware video context retained by PassThroughVideoContext.
    /// </summary>
    public unsafe void ReleaseOutputVideoContext()
    {
        if (_filterPtr == 0)
        {
            return;
        }

        VLCFilter* filter = (VLCFilter*)_filterPtr;
        nint videoContext = filter->VideoContextOut;
        filter->VideoContextOut = 0;
        if (videoContext != 0)
        {
            VLCCore.VideoContextRelease(videoContext);
        }
    }

    /// <summary>
    /// Gets whether this context is valid (has a non-null filter pointer).
    /// </summary>
    public bool IsValid => _filterPtr != 0;

    /// <summary>
    /// Gets the private system data pointer (p_sys) from the filter.
    /// This is typically used to store a GCHandle to the managed instance.
    /// </summary>
    public nint Sys
    {
        get
        {
            if (_filterPtr == 0) return 0;
            return GetFilter().Sys;
        }
    }

    /// <summary>
    /// Sets the private system data pointer (p_sys) on the filter.
    /// </summary>
    /// <param name="value">The pointer value to store</param>
    public unsafe void SetSys(nint value)
    {
        if (_filterPtr == 0) return;
        var filterPtr = (VLCFilter*)_filterPtr;
        filterPtr->Sys = value;
    }

    /// <summary>
    /// Sets the filter operations pointer.
    /// </summary>
    /// <param name="opsPtr">Pointer to pinned VLCFilterOperations struct</param>
    public unsafe void SetOperations(nint opsPtr)
    {
        if (_filterPtr == 0) return;
        var filterPtr = (VLCFilter*)_filterPtr;
        filterPtr->Operations = opsPtr;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe VLCFilter GetFilter()
    {
        return *(VLCFilter*)_filterPtr;
    }
}
