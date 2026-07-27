using VLCLR.LiveTranslation.Protocol;

namespace LiveAudioTranslator.ProtocolTests;

public sealed class BoundedAudioTransportQueueTests
{
    [Fact]
    public void InitialBurstRemainsBoundedByMediaDuration()
    {
        var queue = new LiveAudioTranslator.BoundedAudioTransportQueue(100_000);

        for (int sequence = 0; sequence < 10; sequence++)
        {
            Assert.True(queue.TryEnqueue(Frame(sequence, 20_000), out _));
        }

        Assert.Equal(5, queue.Count);
        Assert.Equal(100_000, queue.QueuedDurationTicks);
        Assert.True(queue.TryDequeue(out LiveAudioTranslator.QueuedAudioFrame first));
        Assert.Equal(5, first.Sequence);
    }

    [Fact]
    public void FlushClearsQueuedPcmAndDuration()
    {
        var queue = new LiveAudioTranslator.BoundedAudioTransportQueue(100_000);
        queue.TryEnqueue(Frame(0, 20_000), out _);

        Assert.Equal(1, queue.Clear());
        Assert.Equal(0, queue.Count);
        Assert.Equal(0, queue.QueuedDurationTicks);
    }

    private static LiveAudioTranslator.QueuedAudioFrame Frame(long sequence, long duration) =>
        new(
            new LiveAudioMessage
            {
                Format = LiveAudioSampleFormat.Pcm16LittleEndian,
                SampleRate = 16_000,
                Channels = 1,
                SourcePts = sequence * duration,
                DurationTicks = duration,
                AudioBytes = [0, 0]
            },
            1,
            sequence);
}
