namespace LiveAudioTranslator;

internal enum PlaybackClockFailure
{
    None,
    NoAnchor,
    UnstableDecodeLead,
    DecodeLeadMismatch,
    StaleAnchor,
    CallbackBeforeAnchor
}

/// <summary>
/// Maps VLC's sub-source system dates to presented media PTS. In live-sync
/// mode decoded audio is intentionally ahead, so an anchor is accepted only
/// after the post-buffer burst settles and the configured input delay can be
/// subtracted from the decoded source PTS.
/// </summary>
internal sealed class PlaybackClockMapper
{
    public const long DefaultStaleAnchorTicks = 2_000_000;
    public const long DefaultJumpThresholdTicks = 2_000_000;
    public const long DefaultLeadToleranceTicks = 1_000_000;
    private const long BackwardToleranceTicks = 100_000;
    private const long CallbackLeadToleranceTicks = 250_000;
    private const int RequiredStableObservations = 3;

    private readonly object _sync = new();
    private readonly long _staleAnchorTicks;
    private readonly long _jumpThresholdTicks;
    private readonly long _configuredInputDelayTicks;
    private readonly long _leadToleranceTicks;
    private bool _hasAnchor;
    private bool _hasObservation;
    private long _lastDecodedPts;
    private long _lastObservationSystemTick;
    private long _presentationMediaPts;
    private long _blockDuration;
    private long _anchorSystemTick;
    private double _playbackRate = 1d;
    private double _candidateRate = 1d;
    private int _stableObservations;
    private int _generation;
    private long _measuredDecodeLeadTicks;
    private PlaybackClockFailure _uncertainReason = PlaybackClockFailure.NoAnchor;

    public PlaybackClockMapper(
        long staleAnchorTicks = DefaultStaleAnchorTicks,
        long jumpThresholdTicks = DefaultJumpThresholdTicks,
        long configuredInputDelayTicks = 0,
        long leadToleranceTicks = DefaultLeadToleranceTicks)
    {
        if (staleAnchorTicks <= 0 || jumpThresholdTicks <= 0 ||
            configuredInputDelayTicks < 0 || leadToleranceTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(staleAnchorTicks));
        }
        _staleAnchorTicks = staleAnchorTicks;
        _jumpThresholdTicks = jumpThresholdTicks;
        _configuredInputDelayTicks = configuredInputDelayTicks;
        _leadToleranceTicks = leadToleranceTicks;
    }

    public int Generation
    {
        get { lock (_sync) return _generation; }
    }

    public bool IsAnchored
    {
        get { lock (_sync) return _hasAnchor; }
    }

    public long MeasuredDecodeLeadTicks
    {
        get { lock (_sync) return _measuredDecodeLeadTicks; }
    }

    public double EstimatedPlaybackRate
    {
        get { lock (_sync) return _playbackRate; }
    }

    public bool Observe(long mediaPts, long blockDuration, long systemTick, bool discontinuity = false)
    {
        if (mediaPts == long.MinValue || systemTick == long.MinValue || blockDuration < 0)
            return false;

        lock (_sync)
        {
            bool reset = discontinuity;
            if (_hasObservation && !reset)
            {
                long mediaDelta = SaturatingSubtract(mediaPts, _lastDecodedPts);
                long systemDelta = SaturatingSubtract(systemTick, _lastObservationSystemTick);
                if (mediaDelta < -BackwardToleranceTicks)
                {
                    reset = true;
                }
                else if (_hasAnchor)
                {
                    long expected = MapFromAnchor(systemTick);
                    long lead = SaturatingSubtract(mediaPts, expected);
                    _measuredDecodeLeadTicks = lead;
                    if (Math.Abs((double)(lead - _configuredInputDelayTicks)) >
                        Math.Max(_leadToleranceTicks, _jumpThresholdTicks))
                    {
                        reset = true;
                    }
                }

                if (!reset)
                    ObserveRate(mediaDelta, systemDelta);
            }

            if (reset)
            {
                _generation++;
                ClearAnchor(discontinuity
                    ? PlaybackClockFailure.NoAnchor
                    : PlaybackClockFailure.DecodeLeadMismatch);
            }

            _hasObservation = true;
            _lastDecodedPts = mediaPts;
            _lastObservationSystemTick = systemTick;
            _blockDuration = blockDuration;

            if (_configuredInputDelayTicks == 0)
            {
                SetAnchor(mediaPts, systemTick);
                return reset;
            }

            if (mediaPts < _configuredInputDelayTicks)
            {
                _hasAnchor = false;
                _uncertainReason = PlaybackClockFailure.UnstableDecodeLead;
                return reset;
            }

            if (_stableObservations >= RequiredStableObservations)
            {
                SetAnchor(Math.Max(0, mediaPts - _configuredInputDelayTicks), systemTick);
                _measuredDecodeLeadTicks = _configuredInputDelayTicks;
            }
            else
            {
                _hasAnchor = false;
                _uncertainReason = PlaybackClockFailure.UnstableDecodeLead;
            }
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
                failure = _uncertainReason;
                return false;
            }

            long elapsed = SaturatingSubtract(sourceSystemDate, _anchorSystemTick);
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

            mediaPts = Math.Max(0, MapFromAnchor(sourceSystemDate));
            failure = PlaybackClockFailure.None;
            return true;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _generation++;
            ClearAnchor(PlaybackClockFailure.NoAnchor);
        }
    }

    private void ObserveRate(long mediaDelta, long systemDelta)
    {
        if (mediaDelta < 0 || systemDelta <= 0)
        {
            _stableObservations = 0;
            return;
        }

        double rate = mediaDelta / (double)systemDelta;
        if (!double.IsFinite(rate) || rate is < 0.25 or > 4.0)
        {
            _stableObservations = 0;
            _candidateRate = 1d;
            return;
        }

        if (_stableObservations == 0 || Math.Abs(rate - _candidateRate) <= 0.15)
        {
            _candidateRate = _stableObservations == 0
                ? rate
                : _candidateRate * 0.75 + rate * 0.25;
            _stableObservations++;
        }
        else
        {
            _candidateRate = rate;
            _stableObservations = 1;
        }
        if (_stableObservations >= RequiredStableObservations)
            _playbackRate = Math.Clamp(_candidateRate, 0.25, 4.0);
    }

    private void SetAnchor(long presentationMediaPts, long systemTick)
    {
        _presentationMediaPts = presentationMediaPts;
        _anchorSystemTick = systemTick;
        _hasAnchor = true;
        _uncertainReason = PlaybackClockFailure.None;
    }

    private void ClearAnchor(PlaybackClockFailure reason)
    {
        _hasAnchor = false;
        _hasObservation = false;
        _stableObservations = 0;
        _candidateRate = 1d;
        _playbackRate = 1d;
        _uncertainReason = reason;
    }

    private long MapFromAnchor(long systemTick)
    {
        long elapsed = SaturatingSubtract(systemTick, _anchorSystemTick);
        double scaled = elapsed * _playbackRate;
        long mediaElapsed = scaled >= long.MaxValue
            ? long.MaxValue
            : scaled <= long.MinValue
                ? long.MinValue
                : (long)Math.Round(scaled);
        return SaturatingAdd(_presentationMediaPts, mediaElapsed);
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
