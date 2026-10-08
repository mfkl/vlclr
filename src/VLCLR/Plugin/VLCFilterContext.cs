// VLC Filter Context wrapper
// Provides safe access to filter information
// VLC Version: 3.0.24

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

    public VLCFilterContext(nint filterPtr)
    {
        _filterPtr = filterPtr;
    }

    public VLCLogger Logger => new(_filterPtr);

    public VLCVideoFormat InputFormat
    {
        get
        {
            if (_filterPtr == 0) return default;
            return GetFilter().FormatIn.Video;
        }
    }

    public VLCVideoFormat OutputFormat
    {
        get
        {
            if (_filterPtr == 0) return default;
            return GetFilter().FormatOut.Video;
        }
    }

    public int Width => (int)InputFormat.VisibleWidth;

    public int Height => (int)InputFormat.VisibleHeight;

    public uint Chroma => InputFormat.Chroma;

    public string ChromaString => VLCFourCC.ToString(Chroma);

    public nint NativePtr => _filterPtr;

    public bool IsValid => _filterPtr != 0;

    public nint Sys
    {
        get
        {
            if (_filterPtr == 0) return 0;
            return GetFilter().Sys;
        }
    }

    public unsafe void SetSys(nint value)
    {
        if (_filterPtr == 0) return;
        var filterPtr = (VLCFilter*)_filterPtr;
        filterPtr->Sys = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe VLCFilter GetFilter()
    {
        return *(VLCFilter*)_filterPtr;
    }
}
