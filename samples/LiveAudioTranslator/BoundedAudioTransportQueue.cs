using VLCLR.LiveTranslation.Protocol;

namespace LiveAudioTranslator;

internal readonly record struct QueuedAudioMetadata(
    LiveAudioSampleFormat Format,
    int SampleRate,
    ushort Channels,
    long SourcePts,
    long DurationTicks,
    int Generation,
    long Sequence);

/// <summary>
/// Preallocated non-blocking PCM queue. The VLC audio callback copies into
/// fixed slots and never allocates a managed payload or waits for a monitor.
/// The transport consumer encodes a reserved slot before releasing it.
/// </summary>
internal sealed class BoundedAudioTransportQueue
{
    internal const int DefaultFrameCapacity = 256;
    internal const int DefaultMaximumAudioBlockBytes = 64 * 1024;

    internal sealed class QueueSlot
    {
        public QueueSlot(long sequence, int maximumAudioBlockBytes)
        {
            Sequence = sequence;
            Buffer = GC.AllocateUninitializedArray<byte>(maximumAudioBlockBytes);
        }

        public long Sequence;
        public readonly byte[] Buffer;
        public int AudioLength;
        public QueuedAudioMetadata Metadata;
    }

    internal struct DequeuedAudioFrame : IDisposable
    {
        private BoundedAudioTransportQueue? _owner;
        private readonly QueueSlot? _slot;
        private readonly long _position;

        private DequeuedAudioFrame(
            BoundedAudioTransportQueue owner,
            QueueSlot slot,
            long position)
        {
            _owner = owner;
            _slot = slot;
            _position = position;
        }

        public readonly QueuedAudioMetadata Metadata =>
            _slot?.Metadata ?? throw new ObjectDisposedException(nameof(DequeuedAudioFrame));

        public readonly ReadOnlySpan<byte> AudioBytes =>
            _slot == null
                ? throw new ObjectDisposedException(nameof(DequeuedAudioFrame))
                : _slot.Buffer.AsSpan(0, _slot.AudioLength);

        public void Dispose()
        {
            BoundedAudioTransportQueue? owner =
                Interlocked.Exchange(ref _owner, null);
            if (owner != null)
                owner.Release(_slot!, _position);
        }

        internal static DequeuedAudioFrame Create(
            BoundedAudioTransportQueue owner,
            QueueSlot slot,
            long position) =>
            new(owner, slot, position);
    }

    private readonly QueueSlot[] _slots;
    private readonly int _indexMask;
    private readonly long _durationBudgetTicks;
    private readonly int _maximumAudioBlockBytes;
    private long _enqueuePosition;
    private long _dequeuePosition;
    private long _queuedDurationTicks;
    private int _count;

    public BoundedAudioTransportQueue(
        long durationBudgetTicks,
        int frameCapacity = DefaultFrameCapacity,
        int maximumAudioBlockBytes = DefaultMaximumAudioBlockBytes)
    {
        if (durationBudgetTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationBudgetTicks));
        if (frameCapacity < 2 || !System.Numerics.BitOperations.IsPow2((uint)frameCapacity))
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameCapacity),
                "Frame capacity must be a power of two greater than one.");
        }
        if (maximumAudioBlockBytes <= 0 ||
            maximumAudioBlockBytes > LiveProtocol.MaximumPayloadBytes - 28)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAudioBlockBytes));
        }

        _durationBudgetTicks = durationBudgetTicks;
        _maximumAudioBlockBytes = maximumAudioBlockBytes;
        _slots = new QueueSlot[frameCapacity];
        for (int index = 0; index < _slots.Length; index++)
            _slots[index] = new QueueSlot(index, maximumAudioBlockBytes);
        _indexMask = frameCapacity - 1;
    }

    public long DurationBudgetTicks => _durationBudgetTicks;
    public int MaximumAudioBlockBytes => _maximumAudioBlockBytes;
    public int Count => Math.Max(0, Volatile.Read(ref _count));
    public long QueuedDurationTicks => Math.Max(0, Volatile.Read(ref _queuedDurationTicks));

    public bool TryEnqueue(
        QueuedAudioMetadata metadata,
        ReadOnlySpan<byte> audioBytes,
        out int droppedFrames)
    {
        droppedFrames = 0;
        if (metadata.DurationTicks <= 0 ||
            metadata.DurationTicks > _durationBudgetTicks ||
            audioBytes.IsEmpty ||
            audioBytes.Length > _maximumAudioBlockBytes)
        {
            return false;
        }

        while (Volatile.Read(ref _queuedDurationTicks) >
               _durationBudgetTicks - metadata.DurationTicks)
        {
            if (!TryDequeue(out DequeuedAudioFrame dropped))
                break;
            dropped.Dispose();
            droppedFrames++;
        }

        while (true)
        {
            long position = Volatile.Read(ref _enqueuePosition);
            QueueSlot slot = _slots[(int)position & _indexMask];
            long sequence = Volatile.Read(ref slot.Sequence);
            long difference = sequence - position;
            if (difference == 0)
            {
                if (Interlocked.CompareExchange(
                        ref _enqueuePosition,
                        position + 1,
                        position) != position)
                {
                    continue;
                }

                audioBytes.CopyTo(slot.Buffer);
                slot.AudioLength = audioBytes.Length;
                slot.Metadata = metadata;
                Interlocked.Add(ref _queuedDurationTicks, metadata.DurationTicks);
                Interlocked.Increment(ref _count);
                Volatile.Write(ref slot.Sequence, position + 1);
                return true;
            }

            if (difference < 0)
            {
                if (!TryDequeue(out DequeuedAudioFrame dropped))
                    return false;
                dropped.Dispose();
                droppedFrames++;
                continue;
            }

            Thread.SpinWait(1);
        }
    }

    public bool TryDequeue(out DequeuedAudioFrame frame)
    {
        while (true)
        {
            long position = Volatile.Read(ref _dequeuePosition);
            QueueSlot slot = _slots[(int)position & _indexMask];
            long sequence = Volatile.Read(ref slot.Sequence);
            long difference = sequence - (position + 1);
            if (difference == 0)
            {
                if (Interlocked.CompareExchange(
                        ref _dequeuePosition,
                        position + 1,
                        position) != position)
                {
                    continue;
                }

                Interlocked.Add(ref _queuedDurationTicks, -slot.Metadata.DurationTicks);
                Interlocked.Decrement(ref _count);
                frame = DequeuedAudioFrame.Create(this, slot, position);
                return true;
            }

            if (difference < 0)
            {
                frame = default;
                return false;
            }

            Thread.SpinWait(1);
        }
    }

    public int Clear()
    {
        int count = 0;
        while (TryDequeue(out DequeuedAudioFrame frame))
        {
            frame.Dispose();
            count++;
        }
        return count;
    }

    private void Release(QueueSlot slot, long position) =>
        Volatile.Write(ref slot.Sequence, position + _slots.Length);
}
