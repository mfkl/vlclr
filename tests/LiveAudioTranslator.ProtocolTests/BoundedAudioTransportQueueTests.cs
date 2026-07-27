using VLCLR.LiveTranslation.Protocol;

namespace LiveAudioTranslator.ProtocolTests;

public sealed class BoundedAudioTransportQueueTests
{
    [Fact]
    public void InitialBurstRemainsBoundedByMediaDuration()
    {
        var queue = new LiveAudioTranslator.BoundedAudioTransportQueue(
            100_000,
            frameCapacity: 8,
            maximumAudioBlockBytes: 64);
        byte[] audio = [0, 0];

        for (int sequence = 0; sequence < 10; sequence++)
        {
            Assert.True(queue.TryEnqueue(Frame(sequence, 20_000), audio, out _));
        }

        Assert.Equal(5, queue.Count);
        Assert.Equal(100_000, queue.QueuedDurationTicks);
        Assert.True(queue.TryDequeue(out var first));
        Assert.Equal(5, first.Metadata.Sequence);
        first.Dispose();
    }

    [Fact]
    public void FlushClearsQueuedPcmAndDuration()
    {
        var queue = new LiveAudioTranslator.BoundedAudioTransportQueue(
            100_000,
            frameCapacity: 8,
            maximumAudioBlockBytes: 64);
        queue.TryEnqueue(Frame(0, 20_000), new byte[] { 0, 0 }, out _);

        Assert.Equal(1, queue.Clear());
        Assert.Equal(0, queue.Count);
        Assert.Equal(0, queue.QueuedDurationTicks);
    }

    [Fact]
    public void ProducerPathDoesNotAllocateAfterConstruction()
    {
        var queue = new LiveAudioTranslator.BoundedAudioTransportQueue(
            100_000,
            frameCapacity: 8,
            maximumAudioBlockBytes: 64);
        byte[] audio = new byte[32];

        Assert.True(queue.TryEnqueue(Frame(0, 20_000), audio, out _));
        Assert.True(queue.TryDequeue(out var warmup));
        warmup.Dispose();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int sequence = 1; sequence <= 1_000; sequence++)
        {
            Assert.True(queue.TryEnqueue(Frame(sequence, 20_000), audio, out _));
            Assert.True(queue.TryDequeue(out var frame));
            frame.Dispose();
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void OversizedBlockIsRejectedWithoutAllocating()
    {
        var queue = new LiveAudioTranslator.BoundedAudioTransportQueue(
            100_000,
            frameCapacity: 8,
            maximumAudioBlockBytes: 16);
        byte[] audio = new byte[32];

        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.False(queue.TryEnqueue(Frame(0, 20_000), audio, out int dropped));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, dropped);
        Assert.Equal(0, allocated);
    }

    private static LiveAudioTranslator.QueuedAudioMetadata Frame(
        long sequence,
        long duration) =>
        new(
            LiveAudioSampleFormat.Pcm16LittleEndian,
            16_000,
            1,
            sequence * duration,
            duration,
            1,
            sequence);
}
