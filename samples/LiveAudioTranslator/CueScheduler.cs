namespace LiveAudioTranslator;

internal readonly record struct ScheduledCue(
    TimedCue Cue,
    long RemainingTicks,
    long SchedulingErrorTicks,
    int PlaybackGeneration);

internal sealed class CueScheduler
{
    private readonly long _earlyToleranceTicks;
    private readonly HashSet<long> _emitted = [];
    private int _playbackGeneration = int.MinValue;
    private string? _timelineGeneration;

    public CueScheduler(long earlyToleranceTicks = 80_000)
    {
        if (earlyToleranceTicks < 0 || earlyToleranceTicks > 500_000)
            throw new ArgumentOutOfRangeException(nameof(earlyToleranceTicks));
        _earlyToleranceTicks = earlyToleranceTicks;
    }

    public bool TrySchedule(
        IReadOnlyList<TimedCue> cues,
        long currentMediaTicks,
        int playbackGeneration,
        string timelineGeneration,
        out ScheduledCue scheduled)
    {
        scheduled = default;
        if (currentMediaTicks < 0 || string.IsNullOrWhiteSpace(timelineGeneration))
            return false;
        if (_playbackGeneration != playbackGeneration ||
            !string.Equals(_timelineGeneration, timelineGeneration, StringComparison.Ordinal))
        {
            _emitted.Clear();
            _playbackGeneration = playbackGeneration;
            _timelineGeneration = timelineGeneration;
        }

        for (int index = 0; index < cues.Count; index++)
        {
            TimedCue cue = cues[index];
            if (cue.EndMediaTicks <= currentMediaTicks)
                continue;
            if (cue.StartMediaTicks > currentMediaTicks + _earlyToleranceTicks)
                return false;

            // Do not skip ahead to another cue while the cue covering this
            // media position is already owned by a live subpicture.
            if (_emitted.Contains(cue.Sequence))
                return false;

            long remaining = cue.EndMediaTicks - currentMediaTicks;
            if (remaining <= 0)
                return false;
            _emitted.Add(cue.Sequence);
            scheduled = new ScheduledCue(
                cue,
                remaining,
                currentMediaTicks - cue.StartMediaTicks,
                playbackGeneration);
            return true;
        }

        return false;
    }

    public void Reset()
    {
        _emitted.Clear();
        _playbackGeneration = int.MinValue;
        _timelineGeneration = null;
    }
}
