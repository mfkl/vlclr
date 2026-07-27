using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using VLCLR.LiveTranslation.Metrics;
using VLCLR.LiveTranslation.Protocol;
using VLCLR.Native;

namespace LiveAudioTranslator;

internal readonly record struct TranslatedCue(
    string Text,
    int DurationMilliseconds,
    long Sequence,
    long SchedulingErrorTicks,
    long SemanticLatencyTicks,
    int Generation);

internal readonly record struct WorkerCue(
    TimedCue Cue,
    int Generation,
    long CompletedSystemTicks,
    long SemanticLatencyTicks);

/// <summary>
/// State shared by the VLC audio-filter and sub-source modules. Prepared mode
/// reads an append-only timeline. Both live modes only copy PCM and schedule
/// worker results; all resampling, VAD, speech recognition, and translation are
/// performed in the regular .NET worker process.
/// </summary>
internal sealed class LiveTranslationHub
{
    private const int MaximumBufferedCues = 512;
    private readonly LiveAudioTranslationOptions _options;
    private readonly object _cueSync = new();
    private readonly PlaybackClockMapper _clock;
    private readonly ConcurrentQueue<string> _status = new();
    private readonly TimedCueFileReader? _timeline;
    private readonly CueScheduler _scheduler;
    private readonly LatencyStatistics _schedulerErrors = new();
    private readonly LiveWorkerClient? _worker;
    private readonly List<WorkerCue> _workerCues = [];
    private WorkerCue? _latestImmediateCue;
    private long _audioSequence;
    private long _controlSequence;
    private long _staleCompletions;
    private long _lastClockMetricSystemTick;
    private long _lastClockMapMetricSystemTick;
    private long _lastClockWarningSystemTick;
    private long _lastUnderrunSystemTick;
    private string? _reportedTimelineError;
    private bool _acceptingAudio = true;
    private long _leadSampleCount;
    private double _leadMean;
    private double _leadM2;
    private int _schedulerMetricGeneration = int.MinValue;

    public LiveTranslationHub(LiveAudioTranslationOptions options)
    {
        _options = options;
        long configuredDelayTicks = options.Mode == LiveAudioTranslationMode.LiveSync
            ? options.InputDelayMilliseconds * VLCTick.Millisecond
            : 0;
        _clock = new PlaybackClockMapper(
            options.StaleClockMilliseconds * VLCTick.Millisecond,
            configuredInputDelayTicks: configuredDelayTicks,
            leadToleranceTicks: options.ClockLeadToleranceMilliseconds * VLCTick.Millisecond);
        _scheduler = new CueScheduler(options.EarlyCueToleranceMilliseconds * VLCTick.Millisecond);

        if (options.Mode == LiveAudioTranslationMode.Prepared)
        {
            if (string.IsNullOrWhiteSpace(options.CueFilePath))
                throw new InvalidOperationException("Prepared mode requires --live-translator-cue-file.");
            _timeline = new TimedCueFileReader(options.CueFilePath);
            _status.Enqueue(
                $"event=ready mode=prepared generation={_timeline.Manifest!.GenerationId} " +
                $"cues={_timeline.Cues.Count} duration_ticks={_timeline.Manifest.AudioDurationTicks}");
            return;
        }

        if (options.SessionId == Guid.Empty || string.IsNullOrWhiteSpace(options.PipeName))
        {
            throw new InvalidOperationException(
                $"{ModeName(options.Mode)} requires a worker session and pipe.");
        }
        _worker = new LiveWorkerClient(
            options.SessionId,
            options.PipeName,
            options.TransportQueueBudgetTicks,
            HandleWorkerMessage,
            message => _status.Enqueue(message));
        _status.Enqueue(
            $"event=ready mode={ModeName(options.Mode)} session={options.SessionId:N} " +
            $"speech_model={options.SpeechModelId} translation_model={options.TranslationModelId} " +
            $"speech_provider={options.SpeechProviderId} translation_provider={options.TranslationProviderId}");
    }

    public LiveAudioTranslationMode Mode => _options.Mode;

    public void ObserveAudio(long mediaPts, long blockDuration, long systemTick, bool discontinuity)
    {
        bool generationChanged = _clock.Observe(mediaPts, blockDuration, systemTick, discontinuity);
        if (generationChanged && _options.Mode != LiveAudioTranslationMode.Prepared)
        {
            ResetLeadStatistics();
            ClearWorkerPipeline(sendFlush: true);
        }
        if (_clock.IsAnchored)
            AddLeadSample(_clock.MeasuredDecodeLeadTicks);

        long last = Volatile.Read(ref _lastClockMetricSystemTick);
        if (systemTick - last >= 5 * VLCTick.Second &&
            Interlocked.CompareExchange(ref _lastClockMetricSystemTick, systemTick, last) == last)
        {
            _status.Enqueue(
                $"event=clock_anchor decoded_pts={mediaPts} block_duration={blockDuration} " +
                $"decode_lead={_clock.MeasuredDecodeLeadTicks} configured_delay=" +
                $"{_options.InputDelayMilliseconds * VLCTick.Millisecond} anchored={_clock.IsAnchored} " +
                $"lead_stddev={LeadStandardDeviation:F1} rate={_clock.EstimatedPlaybackRate:F3} " +
                $"transport_queue={_worker?.QueueDepth ?? 0} dropped_audio={_worker?.DroppedAudio ?? 0} " +
                $"generation={_clock.Generation}");
        }
    }

    public void PushFloat32(
        ReadOnlySpan<float> samples,
        int sampleRate,
        int channels,
        long mediaPts,
        long blockDuration)
    {
        if (_worker == null || !_acceptingAudio || samples.IsEmpty)
            return;
        if (!_worker.IsReady)
        {
            _worker.ReportNotReadyAudio(blockDuration);
            return;
        }
        QueueAudio(
            LiveAudioSampleFormat.Float32LittleEndian,
            sampleRate,
            checked((ushort)channels),
            mediaPts,
            blockDuration,
            MemoryMarshal.AsBytes(samples));
    }

    public void PushPcm16(
        ReadOnlySpan<short> samples,
        int sampleRate,
        int channels,
        long mediaPts,
        long blockDuration)
    {
        if (_worker == null || !_acceptingAudio || samples.IsEmpty)
            return;
        if (!_worker.IsReady)
        {
            _worker.ReportNotReadyAudio(blockDuration);
            return;
        }
        QueueAudio(
            LiveAudioSampleFormat.Pcm16LittleEndian,
            sampleRate,
            checked((ushort)channels),
            mediaPts,
            blockDuration,
            MemoryMarshal.AsBytes(samples));
    }

    public void ResetAudio()
    {
        _clock.Reset();
        ResetLeadStatistics();
        if (_options.Mode == LiveAudioTranslationMode.Prepared)
        {
            _scheduler.Reset();
        }
        else
        {
            ClearWorkerPipeline(sendFlush: true);
        }
    }

    public void BeginSession() => ResetAudio();
    public void EndSession() => ResetAudio();

    public bool TryTakeCue(long sourceSystemDate, out TranslatedCue cue)
    {
        cue = default;
        if (!_clock.TryMap(
                sourceSystemDate,
                out long currentMediaTicks,
                out int generation,
                out PlaybackClockFailure failure))
        {
            ReportClockFailure(sourceSystemDate, failure);
            return false;
        }

        long lastMapMetric = Volatile.Read(ref _lastClockMapMetricSystemTick);
        if (sourceSystemDate - lastMapMetric >= 5 * VLCTick.Second &&
            Interlocked.CompareExchange(
                ref _lastClockMapMetricSystemTick,
                sourceSystemDate,
                lastMapMetric) == lastMapMetric)
        {
            _status.Enqueue(
                $"event=clock_map source_date={sourceSystemDate} presented_media_pts={currentMediaTicks} " +
                $"decode_lead={_clock.MeasuredDecodeLeadTicks} generation={generation}");
        }

        return _options.Mode switch
        {
            LiveAudioTranslationMode.Prepared =>
                TryTakePreparedCue(sourceSystemDate, currentMediaTicks, generation, out cue),
            LiveAudioTranslationMode.LiveSync =>
                TryTakeSynchronizedWorkerCue(currentMediaTicks, generation, out cue),
            LiveAudioTranslationMode.LiveImmediate =>
                TryTakeImmediateCue(currentMediaTicks, generation, out cue),
            _ => false
        };
    }

    public bool TryTakeStatus(out string message) => _status.TryDequeue(out message!);

    private void QueueAudio(
        LiveAudioSampleFormat format,
        int sampleRate,
        ushort channels,
        long mediaPts,
        long blockDuration,
        ReadOnlySpan<byte> audioBytes)
    {
        long sequence = Interlocked.Increment(ref _audioSequence) - 1;
        _worker!.TryQueueAudio(
            format,
            sampleRate,
            channels,
            mediaPts,
            blockDuration,
            audioBytes,
            _clock.Generation,
            sequence);
    }

    private bool TryTakePreparedCue(
        long sourceSystemDate,
        long currentMediaTicks,
        int generation,
        out TranslatedCue cue)
    {
        cue = default;
        TimedCueFileReader timeline = _timeline!;
        timeline.Refresh();
        if (timeline.LastError != null &&
            !string.Equals(_reportedTimelineError, timeline.LastError, StringComparison.Ordinal))
        {
            _reportedTimelineError = timeline.LastError;
            _status.Enqueue($"event=cue_file outcome=record-rejected error={Sanitize(timeline.LastError)}");
        }

        TimedCueManifest manifest = timeline.Manifest!;
        long timelineTicks = currentMediaTicks - manifest.TimelineOffsetTicks;
        if (_scheduler.TrySchedule(
                timeline.Cues,
                timelineTicks,
                generation,
                manifest.GenerationId,
                out ScheduledCue scheduled))
        {
            cue = CreateTranslatedCue(scheduled, generation, semanticLatencyTicks: 0);
            ReportScheduled(cue, timelineTicks);
            return true;
        }

        TimedCueProgress? progress = timeline.ReadProgress();
        if (progress is { Complete: false } && timelineTicks > progress.ProcessedAudioTicks &&
            sourceSystemDate - Volatile.Read(ref _lastUnderrunSystemTick) >= 5 * VLCTick.Second)
        {
            Volatile.Write(ref _lastUnderrunSystemTick, sourceSystemDate);
            _status.Enqueue(
                $"event=lead_underrun media_pts={timelineTicks} prepared_ticks={progress.ProcessedAudioTicks} " +
                $"generation={generation} outcome=no-subtitle");
        }
        return false;
    }

    private bool TryTakeSynchronizedWorkerCue(
        long currentMediaTicks,
        int generation,
        out TranslatedCue cue)
    {
        cue = default;
        lock (_cueSync)
        {
            _workerCues.RemoveAll(candidate =>
                candidate.Generation != generation ||
                candidate.Cue.EndMediaTicks <= currentMediaTicks - VLCTick.Second);
            if (_workerCues.Count == 0)
                return false;

            TimedCue[] timedCues = _workerCues.Select(candidate => candidate.Cue).ToArray();
            if (!_scheduler.TrySchedule(
                    timedCues,
                    currentMediaTicks,
                    generation,
                    _options.SessionId.ToString("N"),
                    out ScheduledCue scheduled))
            {
                return false;
            }
            WorkerCue metadata = _workerCues.First(candidate =>
                candidate.Cue.Sequence == scheduled.Cue.Sequence);
            cue = CreateTranslatedCue(scheduled, generation, metadata.SemanticLatencyTicks);
        }
        ReportScheduled(cue, currentMediaTicks);
        return true;
    }

    private bool TryTakeImmediateCue(long currentMediaTicks, int generation, out TranslatedCue cue)
    {
        cue = default;
        WorkerCue candidate;
        lock (_cueSync)
        {
            if (_latestImmediateCue is not { } value)
                return false;
            candidate = value;
        }

        long ageTicks = currentMediaTicks - candidate.Cue.EndMediaTicks;
        long maximumAgeTicks = _options.MaximumCaptionAgeMilliseconds * VLCTick.Millisecond;
        if (candidate.Generation != generation || ageTicks > maximumAgeTicks)
        {
            ClearImmediateCue(candidate);
            long dropped = Interlocked.Increment(ref _staleCompletions);
            _status.Enqueue(
                $"event=caption_drop reason=stale age_ms={ageTicks / 1000d:F1} " +
                $"generation={generation} stale_completions={dropped}");
            return false;
        }
        if (ageTicks < 0)
            return false;
        if (!ClearImmediateCue(candidate))
            return false;

        long durationTicks = Math.Min(
            _options.SubtitleDurationMilliseconds * VLCTick.Millisecond,
            maximumAgeTicks - ageTicks);
        cue = new TranslatedCue(
            candidate.Cue.Text,
            checked((int)Math.Clamp(durationTicks / VLCTick.Millisecond, 100, 5_000)),
            candidate.Cue.Sequence,
            ageTicks,
            candidate.SemanticLatencyTicks,
            generation);
        return true;
    }

    private bool ClearImmediateCue(WorkerCue expected)
    {
        lock (_cueSync)
        {
            if (_latestImmediateCue is not { } current ||
                current.Generation != expected.Generation ||
                current.Cue.Sequence != expected.Cue.Sequence)
            {
                return false;
            }
            _latestImmediateCue = null;
            return true;
        }
    }

    private void HandleWorkerMessage(LiveProtocolMessage message)
    {
        switch (message.Header.MessageType)
        {
            case LiveMessageType.Cue:
                HandleCue(message);
                break;
            case LiveMessageType.Metrics:
                LiveMetricsMessage metrics = LiveProtocol.DecodeMetrics(message.Payload);
                _status.Enqueue(
                    $"event=worker_metrics rolling_rtf={metrics.RollingRealTimeFactor:F3} " +
                    $"total_rtf={metrics.TotalRealTimeFactor:F3} cue_p50={metrics.CueLatencyP50Ticks} " +
                    $"cue_p95={metrics.CueLatencyP95Ticks} cue_p99={metrics.CueLatencyP99Ticks} " +
                    $"worker_queue={metrics.QueueDepth} dropped_audio={metrics.DroppedAudio} " +
                    $"dropped_utterances={metrics.DroppedUtterances} " +
                    $"stale_completions={metrics.StaleCompletions} transport_queue={_worker?.QueueDepth ?? 0}");
                break;
            case LiveMessageType.Error:
                LiveErrorMessage error = LiveProtocol.DecodeError(message.Payload);
                if (error.Fatal)
                    _acceptingAudio = false;
                _status.Enqueue(
                    $"event=worker_error code={Sanitize(error.Code)} fatal={error.Fatal} " +
                    $"error={Sanitize(error.Message)}");
                break;
        }
    }

    private void HandleCue(LiveProtocolMessage message)
    {
        LiveCueMessage received = LiveProtocol.DecodeCue(message.Payload);
        int currentGeneration = _clock.Generation;
        if (message.Header.PlaybackGeneration != currentGeneration)
        {
            Interlocked.Increment(ref _staleCompletions);
            return;
        }

        var candidate = new WorkerCue(
            new TimedCue(
                message.Header.Sequence,
                received.SourceStartPts,
                received.SourceEndPts,
                TimedCueText.Normalize(received.Text)).NormalizeAndValidate(),
            message.Header.PlaybackGeneration,
            received.CompletedSystemTicks,
            received.SemanticLatencyTicks);

        // Discard obsolete work again immediately before cue delivery.
        if (candidate.Generation != _clock.Generation)
        {
            Interlocked.Increment(ref _staleCompletions);
            return;
        }

        lock (_cueSync)
        {
            if (_options.Mode == LiveAudioTranslationMode.LiveImmediate)
            {
                _latestImmediateCue = candidate;
            }
            else
            {
                int insertion = _workerCues.BinarySearch(
                    candidate,
                    WorkerCueStartComparer.Instance);
                if (insertion < 0)
                    insertion = ~insertion;
                _workerCues.Insert(insertion, candidate);
                if (_workerCues.Count > MaximumBufferedCues)
                    _workerCues.RemoveAt(0);
            }
        }
        _status.Enqueue(
            $"event=translated sequence={candidate.Cue.Sequence} audio_start={candidate.Cue.StartMediaTicks} " +
            $"audio_end={candidate.Cue.EndMediaTicks} semantic_latency={candidate.SemanticLatencyTicks} " +
            $"generation={candidate.Generation} outcome=queued");
    }

    private void ClearWorkerPipeline(bool sendFlush)
    {
        lock (_cueSync)
        {
            _workerCues.Clear();
            _latestImmediateCue = null;
            _scheduler.Reset();
        }
        if (sendFlush)
        {
            long sequence = Interlocked.Increment(ref _controlSequence) - 1;
            _worker?.Flush(_clock.Generation, sequence);
        }
        _acceptingAudio = true;
    }

    private static TranslatedCue CreateTranslatedCue(
        ScheduledCue scheduled,
        int generation,
        long semanticLatencyTicks)
    {
        long durationTicks = Math.Clamp(scheduled.RemainingTicks, 50_000, 30 * VLCTick.Second);
        return new TranslatedCue(
            scheduled.Cue.Text,
            checked((int)Math.Max(50, durationTicks / VLCTick.Millisecond)),
            scheduled.Cue.Sequence,
            scheduled.SchedulingErrorTicks,
            semanticLatencyTicks,
            generation);
    }

    private void ReportScheduled(TranslatedCue cue, long mediaTicks)
    {
        bool resumeSample = cue.Generation != _schedulerMetricGeneration;
        if (resumeSample)
            _schedulerMetricGeneration = cue.Generation;
        else
            _schedulerErrors.Add(Math.Abs(cue.SchedulingErrorTicks));
        (long p50, long p95, long p99) = _schedulerErrors.Snapshot();
        _status.Enqueue(
            $"event=subtitle outcome=scheduled sequence={cue.Sequence} media_pts={mediaTicks} " +
            $"scheduler_error_ms={cue.SchedulingErrorTicks / 1000d:F1} " +
            $"scheduler_sample={(resumeSample ? "resume-age" : "steady-state")} " +
            $"scheduler_p50_ms={p50 / 1000d:F1} scheduler_p95_ms={p95 / 1000d:F1} " +
            $"scheduler_p99_ms={p99 / 1000d:F1} " +
            $"semantic_latency_ms={cue.SemanticLatencyTicks / 1000d:F1} generation={cue.Generation}");
    }

    private void AddLeadSample(long lead)
    {
        _leadSampleCount++;
        double delta = lead - _leadMean;
        _leadMean += delta / _leadSampleCount;
        _leadM2 += delta * (lead - _leadMean);
    }

    private double LeadStandardDeviation =>
        _leadSampleCount < 2 ? 0 : Math.Sqrt(_leadM2 / (_leadSampleCount - 1));

    private void ResetLeadStatistics()
    {
        _leadSampleCount = 0;
        _leadMean = 0;
        _leadM2 = 0;
    }

    private void ReportClockFailure(long sourceSystemDate, PlaybackClockFailure failure)
    {
        long last = Volatile.Read(ref _lastClockWarningSystemTick);
        if (sourceSystemDate - last < 5 * VLCTick.Second ||
            Interlocked.CompareExchange(ref _lastClockWarningSystemTick, sourceSystemDate, last) != last)
        {
            return;
        }
        _status.Enqueue($"event=clock_unavailable reason={failure} outcome=no-subtitle");
    }

    private static string ModeName(LiveAudioTranslationMode mode) =>
        mode switch
        {
            LiveAudioTranslationMode.Prepared => "prepared",
            LiveAudioTranslationMode.LiveImmediate => "live-immediate",
            LiveAudioTranslationMode.LiveSync => "live-sync",
            _ => "unknown"
        };

    private static string Sanitize(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Replace(' ', '-');

    private sealed class WorkerCueStartComparer : IComparer<WorkerCue>
    {
        public static WorkerCueStartComparer Instance { get; } = new();

        public int Compare(WorkerCue left, WorkerCue right)
        {
            int start = left.Cue.StartMediaTicks.CompareTo(right.Cue.StartMediaTicks);
            return start != 0 ? start : left.Cue.Sequence.CompareTo(right.Cue.Sequence);
        }
    }
}

internal static class LiveTranslationHubRegistry
{
    private static readonly object Sync = new();
    private static LiveTranslationHub? _hub;
    private static LiveAudioTranslationOptions? _options;
    private static int _references;

    public static LiveTranslationHubLease Acquire(LiveAudioTranslationOptions options)
    {
        lock (Sync)
        {
            if (_hub == null)
            {
                _options = options;
                _hub = new LiveTranslationHub(options);
            }
            else if (_options != options)
            {
                throw new InvalidOperationException(
                    "Live translator configuration changed. Restart VLC before using the new configuration.");
            }

            if (_references == 0)
                _hub.BeginSession();
            _references++;
            return new LiveTranslationHubLease(_hub);
        }
    }

    public static void Release()
    {
        lock (Sync)
        {
            if (_references > 0)
                _references--;
            if (_references == 0)
                _hub?.EndSession();
        }
    }
}

internal sealed class LiveTranslationHubLease(LiveTranslationHub hub) : IDisposable
{
    private int _disposed;
    public LiveTranslationHub Hub { get; } = hub;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            LiveTranslationHubRegistry.Release();
    }
}
