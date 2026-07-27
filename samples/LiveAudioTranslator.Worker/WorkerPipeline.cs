using System.Buffers.Binary;
using System.Diagnostics;
using System.Threading.Channels;
using LiveAudioTranslator;
using SubtitleTranslator;
using VLCLR.LiveTranslation.Metrics;
using VLCLR.LiveTranslation.Models;
using VLCLR.LiveTranslation.Protocol;

namespace LiveAudioTranslator.Worker;

internal readonly record struct PendingUtterance(
    TimedAudioSegment Segment,
    int Generation,
    long EnqueuedTimestamp);

internal sealed class WorkerPipeline : IAsyncDisposable
{
    private const long TicksPerSecond = 1_000_000;
    private readonly LiveConfigureMessage _configuration;
    private readonly ISpeechRecognizer? _speech;
    private readonly ITranslationEngine? _translation;
    private readonly TranslationCache? _translationCache;
    private readonly StreamingAudioSegmenter? _segmenter;
    private readonly Channel<PendingUtterance> _utterances;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly LatencyStatistics _latencies = new();
    private readonly Queue<double> _rollingRtf = new();
    private readonly object _metricsSync = new();
    private readonly Task _processor;
    private int _generation;
    private long _cueSequence;
    private long _droppedUtterances;
    private long _staleCompletions;
    private long _processedAudioTicks;
    private long _inferenceTicks;
    private string _previousEnglish = "";
    private bool _previousForcedSplit;
    private int _transcriptGeneration = int.MinValue;
    private bool _fakeCueSent;
    private long _latestAudioEndPts;

    private WorkerPipeline(
        LiveConfigureMessage configuration,
        ISpeechRecognizer? speech,
        ITranslationEngine? translation,
        IStreamingSpeechDetector? speechDetector = null)
    {
        _configuration = configuration;
        _speech = speech;
        _translation = translation;
        _translationCache = translation == null ? null : new TranslationCache(512);
        _utterances = Channel.CreateBounded<PendingUtterance>(
            new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });
        if (!configuration.FakeInference)
        {
            _segmenter = new StreamingAudioSegmenter(
                configuration.EnergyVadThreshold,
                configuration.VadSilenceMilliseconds,
                configuration.MaximumUtteranceMilliseconds,
                QueueUtterance,
                speechDetector);
        }
        _processor = Task.Run(ProcessAsync);
    }

    public Func<int, long, LiveCueMessage, ValueTask>? CueReady { get; set; }
    public Func<LiveMetricsMessage, ValueTask>? MetricsReady { get; set; }

    public static async Task<(WorkerPipeline Pipeline, LiveReadyMessage Ready)> CreateAsync(
        string catalogPath,
        LiveConfigureMessage configuration,
        CancellationToken cancellationToken)
    {
        var total = Stopwatch.StartNew();
        if (configuration.FakeInference)
        {
            var fake = new WorkerPipeline(configuration, null, null);
            return (
                fake,
                new LiveReadyMessage
                {
                    SpeechModelId = configuration.SpeechModelId,
                    TranslationModelId = configuration.TranslationModelId,
                    SpeechProviderId = "fake",
                    TranslationProviderId = "fake",
                    ProviderFallbackReason = "",
                    InitializationMilliseconds = 0,
                    WarmupMilliseconds = 0
                });
        }

        ModelProfileCatalog catalog = ModelProfileCatalog.Load(catalogPath);
        ResolvedModelProfile speechProfile = catalog.Resolve(
            catalogPath,
            configuration.SpeechModelId,
            "speech-to-english");
        ResolvedModelProfile translationProfile = catalog.Resolve(
            catalogPath,
            configuration.TranslationModelId,
            "translation");
        string speechProvider = ResolveProvider(
            configuration.SpeechProviderId,
            PackagedProviders.Speech,
            speechProfile.Profile);
        string translationProvider = ResolveProvider(
            configuration.TranslationProviderId,
            PackagedProviders.Translation,
            translationProfile.Profile);
        string fallbackReason = BuildFallbackReason(
            configuration,
            speechProvider,
            translationProvider);
        var speechProviderFactory = new PackagedInferenceProviderFactory(
            speechProvider,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["device"] = configuration.SpeechDeviceId
            });
        InferenceProviderSelection speechSelection =
            speechProviderFactory.CreateSelection(speechProfile.Profile);
        var speechFactory = new WhisperSpeechRecognizerFactory();
        ISpeechRecognizer? speech = null;
        ITranslationEngine? translation = null;
        IStreamingSpeechDetector? speechDetector = null;
        try
        {
            Console.WriteLine("event=worker_init stage=speech-factory");
            speech = speechFactory.Create(
                speechProfile,
                speechSelection,
                configuration.SourceLanguage,
                configuration.SpeechThreads);
            Console.WriteLine("event=worker_init stage=translation-factory");
            var translationFactory = new MarianTranslationEngineFactory();
            if (!string.Equals(
                    translationProfile.Profile.AdapterId,
                    translationFactory.AdapterId,
                    StringComparison.Ordinal) ||
                !translationFactory.Supports(translationProfile.Profile.ModelFamily))
            {
                throw new InvalidOperationException(
                    $"Unsupported translation adapter '{translationProfile.Profile.AdapterId}'.");
            }
            translation = translationFactory.Create(
                translationProfile.ModelDirectory,
                "en",
                configuration.TargetLanguage,
                translationProvider,
                configuration.TranslationThreads);
            total.Stop();

            var warmup = Stopwatch.StartNew();
            Console.WriteLine("event=worker_init stage=speech-warmup");
            _ = await speech.RecognizeEnglishAsync(
                new float[8_000],
                cancellationToken).ConfigureAwait(false);
            Console.WriteLine("event=worker_init stage=translation-warmup");
            _ = translation.TranslateDetailed("model warm up");
            warmup.Stop();

            try
            {
                Console.WriteLine("event=worker_init stage=vad-factory");
                speechDetector = SileroSpeechDetector.Create(
                    speechProfile.ModelDirectory,
                    configuration.SpeechThreads);
                _ = speechDetector.IsSpeech(new float[320]);
                speechDetector.Reset();
                Console.WriteLine("event=worker_init stage=vad-ready");
            }
            catch (Exception exception)
            {
                speechDetector?.Dispose();
                speechDetector = null;
                fallbackReason = string.Join(
                    ',',
                    new[]
                    {
                        fallbackReason,
                        $"vad-energy-fallback-{WorkerLog.Sanitize(exception.GetType().Name)}"
                    }.Where(value => value.Length > 0));
            }

            var pipeline = new WorkerPipeline(
                configuration,
                speech,
                translation,
                speechDetector);
            return (
                pipeline,
                new LiveReadyMessage
                {
                    SpeechModelId = configuration.SpeechModelId,
                    TranslationModelId = configuration.TranslationModelId,
                    SpeechProviderId = speechProvider,
                    TranslationProviderId = translationProvider,
                    ProviderFallbackReason = fallbackReason,
                    InitializationMilliseconds = total.ElapsedMilliseconds,
                    WarmupMilliseconds = warmup.ElapsedMilliseconds
                });
        }
        catch
        {
            speech?.Dispose();
            translation?.Dispose();
            speechDetector?.Dispose();
            throw;
        }
    }

    public void PushAudio(LiveAudioMessage audio, int generation)
    {
        if (generation != Volatile.Read(ref _generation))
            return;
        Volatile.Write(
            ref _latestAudioEndPts,
            Math.Max(Volatile.Read(ref _latestAudioEndPts), audio.SourcePts + audio.DurationTicks));
        if (_configuration.FakeInference)
        {
            PushFake(audio, generation);
            return;
        }

        if (audio.Format == LiveAudioSampleFormat.Float32LittleEndian)
        {
            int sampleCount = audio.AudioBytes.Length / sizeof(float);
            var samples = new float[sampleCount];
            for (int index = 0; index < samples.Length; index++)
            {
                int bits = BinaryPrimitives.ReadInt32LittleEndian(
                    audio.AudioBytes.AsSpan(index * sizeof(float), sizeof(float)));
                samples[index] = BitConverter.Int32BitsToSingle(bits);
            }
            _segmenter!.PushFloat32(
                samples,
                audio.SampleRate,
                audio.Channels,
                audio.SourcePts,
                audio.DurationTicks);
        }
        else
        {
            int sampleCount = audio.AudioBytes.Length / sizeof(short);
            var samples = new short[sampleCount];
            for (int index = 0; index < samples.Length; index++)
            {
                samples[index] = BinaryPrimitives.ReadInt16LittleEndian(
                    audio.AudioBytes.AsSpan(index * sizeof(short), sizeof(short)));
            }
            _segmenter!.PushPcm16(
                samples,
                audio.SampleRate,
                audio.Channels,
                audio.SourcePts,
                audio.DurationTicks);
        }
    }

    public void Flush(int generation)
    {
        Volatile.Write(ref _generation, generation);
        _segmenter?.Reset();
        while (_utterances.Reader.TryRead(out _))
            Interlocked.Increment(ref _droppedUtterances);
        _previousEnglish = "";
        _previousForcedSplit = false;
        _transcriptGeneration = generation;
        _fakeCueSent = false;
        Volatile.Write(ref _latestAudioEndPts, 0);
    }

    private void QueueUtterance(TimedAudioSegment segment)
    {
        var utterance = new PendingUtterance(
            segment,
            Volatile.Read(ref _generation),
            Stopwatch.GetTimestamp());
        if (_utterances.Writer.TryWrite(utterance))
            return;
        if (_utterances.Reader.TryRead(out _))
            Interlocked.Increment(ref _droppedUtterances);
        if (!_utterances.Writer.TryWrite(utterance))
            Interlocked.Increment(ref _droppedUtterances);
    }

    private async Task ProcessAsync()
    {
        try
        {
            await foreach (PendingUtterance work in _utterances.Reader.ReadAllAsync(_shutdown.Token))
            {
                if (work.Generation != Volatile.Read(ref _generation))
                {
                    Interlocked.Increment(ref _staleCompletions);
                    continue;
                }
                long lagBudget = Math.Max(
                    0,
                    (_configuration.InputDelayMilliseconds - 1_000) * 1_000L);
                if (lagBudget > 0 &&
                    Volatile.Read(ref _latestAudioEndPts) - work.Segment.StartMediaTicks >= lagBudget)
                {
                    Interlocked.Increment(ref _droppedUtterances);
                    continue;
                }
                if (_transcriptGeneration != work.Generation)
                {
                    _transcriptGeneration = work.Generation;
                    _previousEnglish = "";
                    _previousForcedSplit = false;
                }

                var total = Stopwatch.StartNew();
                SpeechRecognitionResult recognized = await _speech!.RecognizeEnglishAsync(
                    work.Segment.Samples,
                    _shutdown.Token).ConfigureAwait(false);
                if (work.Generation != Volatile.Read(ref _generation))
                {
                    Interlocked.Increment(ref _staleCompletions);
                    continue;
                }
                string english = _previousForcedSplit
                    ? TranscriptStitcher.RemoveForcedSplitOverlap(_previousEnglish, recognized.Text)
                    : recognized.Text;
                _previousEnglish = recognized.Text;
                _previousForcedSplit = work.Segment.ForcedSplit;
                if (english.Length == 0)
                    continue;

                // Obsolete completions are discarded both before translation
                // and again before cue delivery.
                if (work.Generation != Volatile.Read(ref _generation))
                {
                    Interlocked.Increment(ref _staleCompletions);
                    continue;
                }
                string translated = TimedCueText.Normalize(
                    _translationCache!.GetOrTranslate(english, _translation!));
                total.Stop();
                if (translated.Length == 0 ||
                    work.Generation != Volatile.Read(ref _generation))
                {
                    Interlocked.Increment(ref _staleCompletions);
                    continue;
                }

                long semanticLatency = StopwatchTicksToVlcTicks(
                    Stopwatch.GetTimestamp() - work.EnqueuedTimestamp);
                _latencies.Add(semanticLatency);
                RecordRtf(total.Elapsed, work.Segment.EndMediaTicks - work.Segment.StartMediaTicks);
                long sequence = Interlocked.Increment(ref _cueSequence) - 1;
                if (CueReady != null)
                {
                    await CueReady(
                        work.Generation,
                        sequence,
                        new LiveCueMessage
                        {
                            SourceStartPts = work.Segment.StartMediaTicks,
                            SourceEndPts = work.Segment.EndMediaTicks,
                            CompletedSystemTicks = StopwatchTicksToVlcTicks(Stopwatch.GetTimestamp()),
                            SemanticLatencyTicks = semanticLatency,
                            Text = translated
                        }).ConfigureAwait(false);
                }
                await ReportMetricsAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void PushFake(LiveAudioMessage audio, int generation)
    {
        if (_fakeCueSent)
            return;
        _fakeCueSent = true;
        long cueStart = audio.SourcePts;
        long cueEnd = checked(audio.SourcePts + Math.Max(audio.DurationTicks, TicksPerSecond));
        long sequence = Interlocked.Increment(ref _cueSequence) - 1;
        Console.WriteLine(
            $"event=fake_cue sequence={sequence} generation={generation} start={cueStart} end={cueEnd}");
        if (CueReady != null)
        {
            _ = CueReady(
                generation,
                sequence,
                new LiveCueMessage
                {
                    SourceStartPts = cueStart,
                    SourceEndPts = cueEnd,
                    CompletedSystemTicks = StopwatchTicksToVlcTicks(Stopwatch.GetTimestamp()),
                    SemanticLatencyTicks = 0,
                    Text = "VLCLR LIVE SYNC 0042"
                });
        }
    }

    private void RecordRtf(TimeSpan inference, long audioTicks)
    {
        if (audioTicks <= 0)
            return;
        double rtf = inference.TotalSeconds / (audioTicks / (double)TicksPerSecond);
        lock (_metricsSync)
        {
            _rollingRtf.Enqueue(rtf);
            while (_rollingRtf.Count > 64)
                _rollingRtf.Dequeue();
            _processedAudioTicks += audioTicks;
            _inferenceTicks += (long)(inference.TotalSeconds * TicksPerSecond);
        }
    }

    private async ValueTask ReportMetricsAsync()
    {
        if (MetricsReady == null)
            return;
        LiveMetricsMessage metrics;
        lock (_metricsSync)
        {
            (long p50, long p95, long p99) = _latencies.Snapshot();
            metrics = new LiveMetricsMessage
            {
                RollingRealTimeFactor = _rollingRtf.Count == 0 ? 0 : _rollingRtf.Average(),
                TotalRealTimeFactor = _processedAudioTicks == 0
                    ? 0
                    : _inferenceTicks / (double)_processedAudioTicks,
                CueLatencyP50Ticks = p50,
                CueLatencyP95Ticks = p95,
                CueLatencyP99Ticks = p99,
                QueueDepth = _utterances.Reader.Count,
                DroppedUtterances = Interlocked.Read(ref _droppedUtterances),
                StaleCompletions = Interlocked.Read(ref _staleCompletions)
            };
        }
        await MetricsReady(metrics).ConfigureAwait(false);
    }

    private static string ResolveProvider(string requested, string packaged, ModelProfile profile)
    {
        string selected = string.Equals(requested, "auto", StringComparison.Ordinal) ? packaged : requested;
        if (!profile.CompatibleProviders.Contains(selected, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"Provider '{selected}' is incompatible with model profile '{profile.Id}'.");
        if (!string.Equals(selected, packaged, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"This worker packages provider '{packaged}', not requested '{selected}'.");
        return selected;
    }

    private static string BuildFallbackReason(
        LiveConfigureMessage configuration,
        string speechProvider,
        string translationProvider)
    {
        var reasons = new List<string>();
        if (!string.Equals(configuration.SpeechProviderId, "auto", StringComparison.Ordinal) &&
            !string.Equals(configuration.SpeechProviderId, speechProvider, StringComparison.Ordinal))
        {
            reasons.Add("speech-provider-unavailable");
        }
        if (!string.Equals(configuration.TranslationProviderId, "auto", StringComparison.Ordinal) &&
            !string.Equals(configuration.TranslationProviderId, translationProvider, StringComparison.Ordinal))
        {
            reasons.Add("translation-provider-unavailable");
        }
        return string.Join(',', reasons);
    }

    private static long StopwatchTicksToVlcTicks(long ticks) =>
        checked((long)(ticks * (double)TicksPerSecond / Stopwatch.Frequency));

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _utterances.Writer.TryComplete();
        try
        {
            await _processor.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        if (_speech is IAsyncDisposable asyncSpeech)
            await asyncSpeech.DisposeAsync().ConfigureAwait(false);
        else
            _speech?.Dispose();
        _translation?.Dispose();
        _segmenter?.Dispose();
        _shutdown.Dispose();
    }
}
