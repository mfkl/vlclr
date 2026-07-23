namespace LiveAudioTranslator;

internal enum PlaybackClockFailure
{
    None,
    NoAnchor,
    StaleAnchor,
    CallbackBeforeAnchor
}

internal sealed class PlaybackClockMapper
{
    public const long DefaultStaleAnchorTicks = 2_000_000;
    public const long DefaultJumpThresholdTicks = 2_000_000;
    private const long BackwardToleranceTicks = 100_000;
    private const long CallbackLeadToleranceTicks = 250_000;

    private readonly object _sync = new();
    private readonly long _staleAnchorTicks;
    private readonly long _jumpThresholdTicks;
    private bool _hasAnchor;
    private long _mediaPts;
    private long _blockDuration;
    private long _systemTick;
    private int _generation;

    public PlaybackClockMapper(
        long staleAnchorTicks = DefaultStaleAnchorTicks,
        long jumpThresholdTicks = DefaultJumpThresholdTicks)
    {
        if (staleAnchorTicks <= 0 || jumpThresholdTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(staleAnchorTicks));
        _staleAnchorTicks = staleAnchorTicks;
        _jumpThresholdTicks = jumpThresholdTicks;
    }

    public int Generation
    {
        get { lock (_sync) return _generation; }
    }

    public bool Observe(long mediaPts, long blockDuration, long systemTick, bool discontinuity = false)
    {
        if (mediaPts == long.MinValue || systemTick == long.MinValue || blockDuration < 0)
            return false;

        lock (_sync)
        {
            bool reset = discontinuity;
            if (_hasAnchor && !reset)
            {
                long elapsed = systemTick >= _systemTick ? systemTick - _systemTick : 0;
                long expected = SaturatingAdd(_mediaPts, elapsed);
                long difference = SaturatingSubtract(mediaPts, expected);
                reset = mediaPts < _mediaPts - BackwardToleranceTicks ||
                    Math.Abs((double)difference) > _jumpThresholdTicks;
            }

            if (reset)
                _generation++;
            _mediaPts = mediaPts;
            _blockDuration = blockDuration;
            _systemTick = systemTick;
            _hasAnchor = true;
            return reset;
        }
    }

    public bool TryMap(long sourceSystemDate, out long mediaPts, out int generation) =>
        TryMap(sourceSystemDate, out mediaPts, out generation, out _);

    public bool TryMap(
        long sourceSystemDate,
        out long mediaPts,
        out int generation,
        out PlaybackClockFailure failure)
    {
        lock (_sync)
        {
            generation = _generation;
            mediaPts = 0;
            if (!_hasAnchor)
            {
                failure = PlaybackClockFailure.NoAnchor;
                return false;
            }

            long elapsed = SaturatingSubtract(sourceSystemDate, _systemTick);
            if (elapsed < -CallbackLeadToleranceTicks)
            {
                failure = PlaybackClockFailure.CallbackBeforeAnchor;
                return false;
            }
            if (elapsed > _staleAnchorTicks + Math.Max(0, _blockDuration))
            {
                failure = PlaybackClockFailure.StaleAnchor;
                return false;
            }

            mediaPts = Math.Max(0, SaturatingAdd(_mediaPts, elapsed));
            failure = PlaybackClockFailure.None;
            return true;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _hasAnchor = false;
            _generation++;
        }
    }

    private static long SaturatingAdd(long left, long right)
    {
        if (right > 0 && left > long.MaxValue - right)
            return long.MaxValue;
        if (right < 0 && left < long.MinValue - right)
            return long.MinValue;
        return left + right;
    }

    private static long SaturatingSubtract(long left, long right)
    {
        if (right > 0 && left < long.MinValue + right)
            return long.MinValue;
        if (right < 0 && left > long.MaxValue + right)
            return long.MaxValue;
        return left - right;
    }
}
