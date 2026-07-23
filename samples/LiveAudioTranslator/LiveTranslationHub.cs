using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using SubtitleTranslator;
using VLCLR.Native;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace LiveAudioTranslator;

internal readonly record struct TranslatedCue(
    string Text,
    int DurationMilliseconds,
    long Sequence,
    long SchedulingErrorTicks,
    int Generation);

internal readonly record struct PendingUtterance(
    TimedAudioSegment Segment,
    int Generation,
    long EnqueuedSystemTicks);

internal readonly record struct LiveCue(
    string Text,
    long SourceEndMediaTicks,
    int RequestedDurationMilliseconds,
    long Sequence,
    int Generation,
    long CompletedSystemTicks);

/// <summary>
/// Shared state for the audio-filter and sub-source modules. Synchronized mode
/// owns only a clock and an append-only cue reader. Native model inference is
/// created exclusively for the explicitly requested immediate-live mode.
/// </summary>
internal sealed class LiveTranslationHub
{
    private readonly LiveAudioTranslationOptions _options;
    private readonly object _ingestSync = new();
    private readonly object _cueSync = new();
    private readonly PlaybackClockMapper _clock;
    private readonly ConcurrentQueue<string> _status = new();
    private readonly TimedCueFileReader? _timeline;
    private readonly CueScheduler? _scheduler;
    private readonly StreamingAudioSegmenter? _segmenter;
    private readonly LatestWorkQueue<PendingUtterance>? _workQueue;
    private readonly Task? _worker;
    private LiveCue? _latestLiveCue;
    private long _warmupAudioDrops;
    private long _queueDrops;
    private long _staleDrops;
    private long _liveSequence;
    private long _lastClockMetricSystemTick;
    private long _lastClockMapMetricSystemTick;
    private long _lastClockWarningSystemTick;
    private long _lastUnderrunSystemTick;
    private string? _reportedTimelineError;

    public LiveTranslationHub(LiveAudioTranslationOptions options)
    {
        _options = options;
        _clock = new PlaybackClockMapper(options.StaleClockMilliseconds * VLCTick.Millisecond);
        if (options.Mode == LiveAudioTranslationMode.Synchronized)
        {
            if (string.IsNullOrWhiteSpace(options.CueFilePath))
                throw new InvalidOperationException("Synchronized mode requires --live-translator-cue-file.");
            _timeline = new TimedCueFileReader(options.CueFilePath);
            _scheduler = new CueScheduler(options.EarlyCueToleranceMilliseconds * VLCTick.Millisecond);
            _status.Enqueue(
                $"event=ready mode=sync generation={_timeline.Manifest!.GenerationId} " +
                $"cues={_timeline.Cues.Count} duration_ticks={_timeline.Manifest.AudioDurationTicks}");
        }
        else
        {
            _workQueue = new LatestWorkQueue<PendingUtterance>();
            _segmenter = new StreamingAudioSegmenter(
                options.VadThreshold,
                options.SilenceMilliseconds,
                options.MaximumUtteranceMilliseconds,
                QueueLatestUtterance);
            _worker = Task.Run(ProcessLiveLoopAsync);
        }
    }

    public LiveAudioTranslationMode Mode => _options.Mode;

    public void ObserveAudio(long mediaPts, long blockDuration, long systemTick, bool discontinuity)
    {
        bool generationChanged = _clock.Observe(mediaPts, blockDuration, systemTick, discontinuity);
        if (generationChanged && _options.Mode == LiveAudioTranslationMode.Live)
            ClearLivePipeline();

        long last = Volatile.Read(ref _lastClockMetricSystemTick);
        if (systemTick - last >= 5 * VLCTick.Second &&
            Interlocked.CompareExchange(ref _lastClockMetricSystemTick, systemTick, last) == last)
        {
            _status.Enqueue(
                $"event=clock_anchor media_pts={mediaPts} block_duration={blockDuration} " +
                $"system_tick={systemTick} generation={_clock.Generation}");
        }
    }

    public void PushFloat32(
        ReadOnlySpan<float> samples,
        int sampleRate,
        int channels,
        long mediaPts,
        long blockDuration)
    {
        if (_segmenter == null)
            return;
        if (!_workQueue!.IsReady)
        {
            ReportWarmupDrop();
            return;
        }
        lock (_ingestSync)
            _segmenter.PushFloat32(samples, sampleRate, channels, mediaPts, blockDuration);
    }

    public void PushPcm16(
        ReadOnlySpan<short> samples,
        int sampleRate,
        int channels,
        long mediaPts,
        long blockDuration)
    {
        if (_segmenter == null)
            return;
        if (!_workQueue!.IsReady)
        {
            ReportWarmupDrop();
            return;
        }
        lock (_ingestSync)
            _segmenter.PushPcm16(samples, sampleRate, channels, mediaPts, blockDuration);
    }

    public void ResetAudio()
    {
        _clock.Reset();
        if (_options.Mode == LiveAudioTranslationMode.Live)
            ClearLivePipeline();
        else
            _scheduler?.Reset();
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
                $"event=clock_map source_date={sourceSystemDate} media_pts={currentMediaTicks} " +
                $"generation={generation}");
        }

        return _options.Mode == LiveAudioTranslationMode.Synchronized
            ? TryTakeSynchronizedCue(sourceSystemDate, currentMediaTicks, generation, out cue)
            : TryTakeLiveCue(currentMediaTicks, generation, out cue);
    }

    public bool TryTakeStatus(out string message) => _status.TryDequeue(out message!);

    private bool TryTakeSynchronizedCue(
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
            _status.Enqueue($"event=cue_file outcome=record-rejected error={SanitizeValue(timeline.LastError)}");
        }

        TimedCueManifest manifest = timeline.Manifest!;
        long timelineTicks = currentMediaTicks - manifest.TimelineOffsetTicks;
        if (_scheduler!.TrySchedule(
                timeline.Cues,
                timelineTicks,
                generation,
                manifest.GenerationId,
                out ScheduledCue scheduled))
        {
            long durationTicks = Math.Clamp(scheduled.RemainingTicks, 50_000, 30 * VLCTick.Second);
            cue = new TranslatedCue(
                scheduled.Cue.Text,
                checked((int)Math.Max(50, durationTicks / VLCTick.Millisecond)),
                scheduled.Cue.Sequence,
                scheduled.SchedulingErrorTicks,
                generation);
            _status.Enqueue(
                $"event=subtitle outcome=scheduled sequence={cue.Sequence} media_pts={timelineTicks} " +
                $"error_ms={cue.SchedulingErrorTicks / 1000d:F1} generation={generation}");
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

    private bool TryTakeLiveCue(long currentMediaTicks, int generation, out TranslatedCue cue)
    {
        cue = default;
        LiveCue candidate;
        lock (_cueSync)
        {
            if (_latestLiveCue is not { } value)
                return false;
            _latestLiveCue = null;
            candidate = value;
        }

        long ageTicks = currentMediaTicks - candidate.SourceEndMediaTicks;
        long maximumAgeTicks = _options.MaximumCaptionAgeMilliseconds * VLCTick.Millisecond;
        if (candidate.Generation != generation || ageTicks < 0 || ageTicks > maximumAgeTicks)
        {
            long dropped = Interlocked.Increment(ref _staleDrops);
            _status.Enqueue(
                $"event=caption_drop reason=stale age_ms={ageTicks / 1000d:F1} " +
                $"generation={generation} stale_drops={dropped}");
            return false;
        }

        long remainingAgeTicks = maximumAgeTicks - ageTicks;
        int duration = checked((int)Math.Clamp(
            Math.Min(candidate.RequestedDurationMilliseconds * VLCTick.Millisecond, remainingAgeTicks) /
                VLCTick.Millisecond,
            100,
            _options.SubtitleDurationMilliseconds));
        cue = new TranslatedCue(
            candidate.Text,
            duration,
            candidate.Sequence,
            ageTicks,
            generation);
        return true;
    }

    private void QueueLatestUtterance(TimedAudioSegment segment)
    {
        var utterance = new PendingUtterance(segment, _clock.Generation, VLCCore.TickNow());
        LatestWorkOfferResult result = _workQueue!.Offer(utterance);
        if (result == LatestWorkOfferResult.Replaced)
        {
            long drops = Interlocked.Increment(ref _queueDrops);
            _status.Enqueue($"event=audio_queue outcome=replaced-oldest queue_drops={drops}");
        }
    }

    private async Task ProcessLiveLoopAsync()
    {
        try
        {
            var initialization = Stopwatch.StartNew();
            ValidateLiveFiles();
            NativeLoadResult nativeLoad = OnnxNativeResolver.EnsureLoadedResult(_options.TranslationModelPath);
            if (!nativeLoad.Success)
                throw new InvalidOperationException(nativeLoad.Diagnostics);

            ConfigureWhisperRuntime();
            using var whisperFactory = WhisperFactory.FromPath(_options.WhisperModelPath);
            WhisperProcessorBuilder processorBuilder = whisperFactory.CreateBuilder()
                .WithThreads(_options.WhisperThreads)
                .WithTranslate()
                .WithNoContext()
                .WithSingleSegment();
            processorBuilder = _options.SourceLanguage == "auto"
                ? processorBuilder.WithLanguageDetection()
                : processorBuilder.WithLanguage(_options.SourceLanguage);
            using var whisper = processorBuilder.Build();
            using var translator = new OnnxTranslator(
                _options.TranslationModelPath,
                "en",
                _options.TargetLanguage,
                new OnnxTranslatorOptions
                {
                    IntraOpThreads = _options.TranslationThreads,
                    MaximumSourceTokens = 128,
                    MaximumOutputTokens = 128,
                    UseDecoderCache = true,
                    CacheActivationTokenCount = 32,
                    VerifyModelHashes = true
                });

            // Force native initialization and first-inference allocations
            // before the audio segmenter is allowed to accept PCM.
            _ = await TranscribeAsync(whisper, new float[8_000], CancellationToken.None);
            _ = translator.Translate("model warm up");
            initialization.Stop();
            _workQueue!.MarkReady();
            _status.Enqueue(
                $"event=ready mode=live model_init_ms={initialization.Elapsed.TotalMilliseconds:F1} " +
                $"source={_options.SourceLanguage} target={_options.TargetLanguage} " +
                $"whisper_threads={_options.WhisperThreads} translation_threads={_options.TranslationThreads}");

            var cache = new TranslationCache(512);
            string previousEnglish = "";
            bool previousForcedSplit = false;
            int transcriptGeneration = int.MinValue;
            while (true)
            {
                PendingUtterance work = await _workQueue.TakeAsync();
                if (work.Generation != _clock.Generation || IsStale(work.Segment.EndMediaTicks))
                {
                    Interlocked.Increment(ref _staleDrops);
                    continue;
                }

                if (transcriptGeneration != work.Generation)
                {
                    transcriptGeneration = work.Generation;
                    previousEnglish = "";
                    previousForcedSplit = false;
                }

                var total = Stopwatch.StartNew();
                string rawEnglish = await TranscribeAsync(whisper, work.Segment.Samples, CancellationToken.None);
                if (work.Generation != _clock.Generation || IsStale(work.Segment.EndMediaTicks))
                {
                    Interlocked.Increment(ref _staleDrops);
                    continue;
                }
                string english = previousForcedSplit
                    ? TranscriptStitcher.RemoveForcedSplitOverlap(previousEnglish, rawEnglish)
                    : rawEnglish;
                previousEnglish = rawEnglish;
                previousForcedSplit = work.Segment.ForcedSplit;
                if (english.Length == 0)
                    continue;

                var translationTimer = Stopwatch.StartNew();
                string translated = TimedCueText.Normalize(cache.GetOrTranslate(english, translator));
                translationTimer.Stop();
                total.Stop();
                if (translated.Length == 0 || work.Generation != _clock.Generation ||
                    IsStale(work.Segment.EndMediaTicks))
                {
                    Interlocked.Increment(ref _staleDrops);
                    continue;
                }

                long sequence = Interlocked.Increment(ref _liveSequence) - 1;
                lock (_cueSync)
                {
                    _latestLiveCue = new LiveCue(
                        translated,
                        work.Segment.EndMediaTicks,
                        _options.SubtitleDurationMilliseconds,
                        sequence,
                        work.Generation,
                        VLCCore.TickNow());
                }
                _status.Enqueue(
                    $"event=translated mode=live cue={TranslationTextNormalizer.ComputeCueHash(english)} " +
                    $"sequence={sequence} audio_start={work.Segment.StartMediaTicks} " +
                    $"audio_end={work.Segment.EndMediaTicks} translation_ms={translationTimer.Elapsed.TotalMilliseconds:F1} " +
                    $"total_ms={total.Elapsed.TotalMilliseconds:F1} outcome=latest");
            }
        }
        catch (Exception ex)
        {
            _status.Enqueue($"event=failed mode=live error={SanitizeValue($"{ex.GetType().Name}:{ex.Message}")}");
        }
    }

    private bool IsStale(long sourceEndMediaTicks)
    {
        if (!_clock.TryMap(VLCCore.TickNow(), out long currentMediaTicks, out _))
            return true;
        return currentMediaTicks - sourceEndMediaTicks >
            _options.MaximumCaptionAgeMilliseconds * VLCTick.Millisecond;
    }

    private static async Task<string> TranscribeAsync(
        WhisperProcessor processor,
        float[] utterance,
        CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        await foreach (SegmentData segment in processor.ProcessAsync(utterance, cancellationToken))
        {
            string part = segment.Text.Trim();
            if (part.Length == 0 || IsNonSpeechLabel(part))
                continue;
            if (text.Length > 0)
                text.Append(' ');
            text.Append(part);
        }
        return TranslationTextNormalizer.NormalizeCacheKey(text.ToString());
    }

    private void ConfigureWhisperRuntime()
    {
        string whisperRuntimeDirectory = Path.GetDirectoryName(_options.WhisperRuntimePath)!;
        string runtimesDirectory = Path.GetDirectoryName(whisperRuntimeDirectory)!;
        string whisperSearchRoot = Path.GetDirectoryName(runtimesDirectory)!;
        RuntimeOptions.LibraryPath = Path.Combine(whisperSearchRoot, "whisper-loader-anchor.dll");
    }

    private void ValidateLiveFiles()
    {
        if (!File.Exists(_options.WhisperModelPath))
            throw new FileNotFoundException("Whisper model not found", _options.WhisperModelPath);
        if (!File.Exists(_options.WhisperRuntimePath))
            throw new FileNotFoundException("Whisper runtime not found", _options.WhisperRuntimePath);
        if (!Directory.Exists(_options.TranslationModelPath))
            throw new DirectoryNotFoundException($"Translation model not found: {_options.TranslationModelPath}");
    }

    private void ClearLivePipeline()
    {
        lock (_ingestSync)
            _segmenter?.Reset();
        _workQueue?.Clear();
        lock (_cueSync)
            _latestLiveCue = null;
    }

    private void ReportWarmupDrop()
    {
        long drops = Interlocked.Increment(ref _warmupAudioDrops);
        if (drops == 1 || drops % 100 == 0)
            _status.Enqueue($"event=audio_drop reason=model-warmup warmup_drops={drops}");
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

    private static bool IsNonSpeechLabel(string text) =>
        (text.StartsWith('[') && text.EndsWith(']')) ||
        (text.StartsWith('(') && text.EndsWith(')'));

    private static string SanitizeValue(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Replace(' ', '-');
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
