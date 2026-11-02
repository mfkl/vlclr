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
    private readonly ushort _proposedOutputChannels;

    /// <summary>Creates a context over a native <c>filter_t</c>.</summary>
    public VLCAudioFilterContext(nint filterPtr)
        : this(filterPtr, 0)
    {
    }

    /// <summary>
    /// Creates a context over a native <c>filter_t</c>, with the output
    /// channels VLC proposed when it created the filter.
    /// </summary>
    public VLCAudioFilterContext(nint filterPtr, ushort proposedOutputChannels)
    {
        _filterPtr = filterPtr;
        _proposedOutputChannels = proposedOutputChannels;
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
    /// The output channels VLC proposed when it created the filter, before
    /// the output was set to the input's layout: in playback the audio
    /// device's layout, when transcoding the encoder's. 0 when VLC proposed
    /// no channel map. A filter that mixes down itself (for example 5.1 to
    /// the device's stereo) passes it to <see cref="SetOutputChannels"/>.
    /// </summary>
    public ushort ProposedOutputChannels => _proposedOutputChannels;

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
