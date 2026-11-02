// A block of decoded audio handed to an audio filter
// VLC Version: 4.0.6

namespace VLCLR.Plugin;

/// <summary>
/// One block of interleaved 32-bit float samples, in VLC's channel order
/// (<see cref="Native.VLCAudioChannels.Order"/>), and where the filter writes
/// its output.
/// </summary>
/// <remarks>
/// When the filter keeps the layout, <see cref="Input"/> and
/// <see cref="Output"/> are the same memory (<see cref="InPlace"/>): read
/// what a frame needs before writing it.
/// </remarks>
public readonly ref struct VLCAudioBlock
{
    internal VLCAudioBlock(ReadOnlySpan<float> input, Span<float> output, int frameCount,
        int inputChannelCount, int outputChannelCount, long timestamp, bool inPlace)
    {
        Input = input;
        Output = output;
        FrameCount = frameCount;
        InputChannelCount = inputChannelCount;
        OutputChannelCount = outputChannelCount;
        Timestamp = timestamp;
        InPlace = inPlace;
    }

    /// <summary>The input samples: <see cref="FrameCount"/> x <see cref="InputChannelCount"/>.</summary>
    public ReadOnlySpan<float> Input { get; }

    /// <summary>The output samples: <see cref="FrameCount"/> x <see cref="OutputChannelCount"/>.</summary>
    public Span<float> Output { get; }

    /// <summary>Number of frames (one sample per channel each).</summary>
    public int FrameCount { get; }

    /// <summary>Channels per input frame.</summary>
    public int InputChannelCount { get; }

    /// <summary>Channels per output frame.</summary>
    public int OutputChannelCount { get; }

    /// <summary>Presentation time of the first frame, in VLC ticks (microseconds).</summary>
    public long Timestamp { get; }

    /// <summary>Whether <see cref="Input"/> and <see cref="Output"/> are the same memory.</summary>
    public bool InPlace { get; }
}
