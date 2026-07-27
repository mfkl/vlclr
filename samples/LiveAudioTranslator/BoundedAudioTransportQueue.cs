using VLCLR.LiveTranslation.Protocol;

namespace LiveAudioTranslator;

internal readonly record struct QueuedAudioFrame(
    LiveAudioMessage Audio,
    int Generation,
    long Sequence);

/// <summary>
/// A non-blocking producer queue bounded by source-audio duration. The budget
/// is deliberately expressed in media ticks so initial VLC cache bursts do not
/// turn a change in PCM block size into unbounded memory growth.
/// </summary>
internal sealed class BoundedAudioTransportQueue
{
    private readonly object _sync = new();
    private readonly Queue<QueuedAudioFrame> _frames = new();
    private readonly long _durationBudgetTicks;
    private long _queuedDurationTicks;

    public BoundedAudioTransportQueue(long durationBudgetTicks)
    {
        if (durationBudgetTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationBudgetTicks));
        _durationBudgetTicks = durationBudgetTicks;
    }

    public long DurationBudgetTicks => _durationBudgetTicks;

    public int Count
    {
        get { lock (_sync) return _frames.Count; }
    }

    public long QueuedDurationTicks
    {
        get { lock (_sync) return _queuedDurationTicks; }
    }

    public bool TryEnqueue(QueuedAudioFrame frame, out int droppedFrames)
    {
        droppedFrames = 0;
        if (frame.Audio.DurationTicks <= 0 || frame.Audio.DurationTicks > _durationBudgetTicks)
            return false;

        lock (_sync)
        {
            while (_frames.Count > 0 &&
                   _queuedDurationTicks > _durationBudgetTicks - frame.Audio.DurationTicks)
            {
                QueuedAudioFrame dropped = _frames.Dequeue();
                _queuedDurationTicks -= dropped.Audio.DurationTicks;
                droppedFrames++;
            }
            _frames.Enqueue(frame);
            _queuedDurationTicks += frame.Audio.DurationTicks;
            return true;
        }
    }

    public bool TryDequeue(out QueuedAudioFrame frame)
    {
        lock (_sync)
        {
            if (!_frames.TryDequeue(out frame))
                return false;
            _queuedDurationTicks -= frame.Audio.DurationTicks;
            return true;
        }
    }

    public int Clear()
    {
        lock (_sync)
        {
            int count = _frames.Count;
            _frames.Clear();
            _queuedDurationTicks = 0;
            return count;
        }
    }
}
