// VLC audio filter context
// Safe access to an audio filter's formats
// VLC Version: 4.0.6

using VLCLR.Native;
using Interop = VLCLR.Native.Interop;

namespace VLCLR.Plugin;

/// <summary>
/// An audio filter's <c>filter_t</c>: its input and output formats, and the
/// output layout it may choose while it opens.
/// </summary>
public readonly struct VLCAudioFilterContext
{
    private readonly nint _filterPtr;

    /// <summary>Creates a context over a native <c>filter_t</c>.</summary>
    public VLCAudioFilterContext(nint filterPtr)
    {
        _filterPtr = filterPtr;
    }

    /// <summary>A logger bound to this filter's VLC object.</summary>
    public VLCLogger Logger => new(_filterPtr);

    /// <summary>The native filter pointer, for configuration and variables.</summary>
    public nint NativePtr => _filterPtr;

    /// <summary>The input format (<c>fmt_in.audio</c>).</summary>
    public unsafe VLCAudioFormat InputFormat => _filterPtr == 0 ? default : Filter->FormatIn->Audio;

    /// <summary>The output format (<c>fmt_out.audio</c>).</summary>
    public unsafe VLCAudioFormat OutputFormat => _filterPtr == 0 ? default : Filter->FormatOut->Audio;

    /// <summary>Sample rate, the same in and out.</summary>
    public int Rate => (int)InputFormat.Rate;

    /// <summary>Input channels (<see cref="VLCAudioChannels"/> bits).</summary>
    public ushort InputChannels => InputFormat.PhysicalChannels;

    /// <summary>Output channels (<see cref="VLCAudioChannels"/> bits).</summary>
    public ushort OutputChannels => OutputFormat.PhysicalChannels;

    /// <summary>
    /// Sets the output channels, for a filter that changes the layout (a
    /// downmix, an upmix). Only valid in <c>OnOpen</c>; VLC converts the
    /// output to what follows the filter.
    /// </summary>
    public unsafe void SetOutputChannels(ushort mask)
    {
        if (_filterPtr == 0)
            return;
        if (VLCAudioChannels.Count(mask) == 0)
            throw new ArgumentException("An output needs at least one channel.", nameof(mask));

        var format = (Interop.AudioFormat*)&Filter->FormatOut->Video;
        format->PhysicalChannels = mask;
        format->ChanMode = 0;
        Interop.Aout.FormatPrepare(format);
    }

    private unsafe VLCFilter* Filter => (VLCFilter*)_filterPtr;
}
