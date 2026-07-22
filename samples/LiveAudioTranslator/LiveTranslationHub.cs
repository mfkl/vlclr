using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using SubtitleTranslator;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace LiveAudioTranslator;

internal readonly record struct TranslatedCue(string Text, int DurationMilliseconds);

/// <summary>
/// Shared, non-blocking audio-to-subtitle pipeline used by the audio-filter and
/// sub-source modules exported from this DLL.
/// </summary>
internal sealed class LiveTranslationHub : IDisposable
{
    private readonly LiveAudioTranslationOptions _options;
    private readonly object _ingestSync = new();
    private readonly StreamingAudioSegmenter _segmenter;
    private readonly Channel<float[]> _utterances;
    private readonly ConcurrentQueue<TranslatedCue> _cues = new();
    private readonly ConcurrentQueue<string> _status = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;
    private int _disposed;

    public LiveTranslationHub(LiveAudioTranslationOptions options)
    {
        _options = options;
        _utterances = Channel.CreateBounded<float[]>(new BoundedChannelOptions(2)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        _segmenter = new StreamingAudioSegmenter(
            options.VadThreshold,
            options.SilenceMilliseconds,
            options.MaximumUtteranceMilliseconds,
            QueueUtterance);
        _worker = Task.Run(ProcessLoopAsync);
    }

    public void PushFloat32(ReadOnlySpan<float> samples, int sampleRate, int channels)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        lock (_ingestSync)
            _segmenter.PushFloat32(samples, sampleRate, channels);
    }

    public void PushPcm16(ReadOnlySpan<short> samples, int sampleRate, int channels)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        lock (_ingestSync)
            _segmenter.PushPcm16(samples, sampleRate, channels);
    }

    public void ResetAudio()
    {
        lock (_ingestSync)
            _segmenter.Reset();
        while (_utterances.Reader.TryRead(out _))
        {
        }
    }

    public bool TryTakeCue(out TranslatedCue cue) => _cues.TryDequeue(out cue);

    public bool TryTakeStatus(out string message) => _status.TryDequeue(out message!);

    private void QueueUtterance(float[] samples)
    {
        if (!_utterances.Writer.TryWrite(samples))
            _status.Enqueue("event=audio_queue_full outcome=dropped");
    }

    private async Task ProcessLoopAsync()
    {
        try
        {
            ValidateFiles();
            NativeLoadResult nativeLoad = OnnxNativeResolver.EnsureLoadedResult(_options.TranslationModelPath);
            if (!nativeLoad.Success)
                throw new InvalidOperationException(nativeLoad.Diagnostics);

            // Whisper.net treats LibraryPath as an anchor and appends
            // runtimes/win-x64 itself. Point the anchor at the VLC root while
            // separately validating the exact deployed whisper.dll below.
            string whisperRuntimeDirectory = Path.GetDirectoryName(_options.WhisperRuntimePath)!;
            string runtimesDirectory = Path.GetDirectoryName(whisperRuntimeDirectory)!;
            string whisperSearchRoot = Path.GetDirectoryName(runtimesDirectory)!;
            RuntimeOptions.LibraryPath = Path.Combine(whisperSearchRoot, "whisper-loader-anchor.dll");
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
            var cache = new TranslationCache(512);

            _status.Enqueue(
                $"event=ready whisper={Path.GetFileName(_options.WhisperModelPath)} " +
                $"runtime={Path.GetFileName(_options.WhisperRuntimePath)} " +
                $"source={_options.SourceLanguage} target={_options.TargetLanguage} " +
                $"whisper_threads={_options.WhisperThreads} translation_threads={_options.TranslationThreads}");

            await foreach (float[] utterance in _utterances.Reader.ReadAllAsync(_shutdown.Token))
            {
                var total = Stopwatch.StartNew();
                string english = await TranscribeAsync(whisper, utterance, _shutdown.Token);
                if (english.Length == 0)
                {
                    _status.Enqueue("event=utterance outcome=no-speech");
                    continue;
                }

                var translationTimer = Stopwatch.StartNew();
                string french = cache.GetOrTranslate(english, translator);
                translationTimer.Stop();
                total.Stop();

                int duration = Math.Clamp(
                    Math.Max(_options.SubtitleDurationMilliseconds, french.Length * 65),
                    1_500,
                    8_000);
                while (_cues.Count >= 4 && _cues.TryDequeue(out _))
                {
                }
                _cues.Enqueue(new TranslatedCue(french, duration));
                _status.Enqueue(
                    $"event=translated cue={TranslationTextNormalizer.ComputeCueHash(english)} " +
                    $"audio_ms={utterance.Length * 1_000 / StreamingAudioSegmenter.OutputSampleRate} " +
                    $"translation_ms={translationTimer.Elapsed.TotalMilliseconds:F1} " +
                    $"total_ms={total.Elapsed.TotalMilliseconds:F1} outcome=queued");
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _status.Enqueue($"event=failed error={SanitizeError(ex)}");
        }
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

    private void ValidateFiles()
    {
        if (!File.Exists(_options.WhisperModelPath))
            throw new FileNotFoundException("Whisper model not found", _options.WhisperModelPath);
        if (!File.Exists(_options.WhisperRuntimePath))
            throw new FileNotFoundException("Whisper runtime not found", _options.WhisperRuntimePath);
        if (!Directory.Exists(_options.TranslationModelPath))
            throw new DirectoryNotFoundException($"Translation model not found: {_options.TranslationModelPath}");
    }

    private static bool IsNonSpeechLabel(string text) =>
        (text.StartsWith('[') && text.EndsWith(']')) ||
        (text.StartsWith('(') && text.EndsWith(')'));

    private static string SanitizeError(Exception exception)
    {
        string value = $"{exception.GetType().Name}:{exception.Message}";
        return value.Replace('\r', ' ').Replace('\n', ' ').Replace(' ', '-');
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _utterances.Writer.TryComplete();
        _shutdown.Cancel();
        try
        {
            _worker.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
        }
        _shutdown.Dispose();
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
                    "Live translator modules resolved different configurations in the same VLC process.");
            }

            _references++;
            return new LiveTranslationHubLease(_hub);
        }
    }

    public static void Release()
    {
        LiveTranslationHub? dispose = null;
        lock (Sync)
        {
            if (_references > 0)
                _references--;
            if (_references == 0)
            {
                dispose = _hub;
                _hub = null;
                _options = null;
            }
        }
        dispose?.Dispose();
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
