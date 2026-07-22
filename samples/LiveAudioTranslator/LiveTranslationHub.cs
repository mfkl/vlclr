using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using SubtitleTranslator;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace LiveAudioTranslator;

internal readonly record struct TranslatedCue(string Text, int DurationMilliseconds);

internal readonly record struct PendingUtterance(float[] Samples, int Generation);

/// <summary>
/// Shared, non-blocking audio-to-subtitle pipeline used by the audio-filter and
/// sub-source modules exported from this DLL.
/// </summary>
internal sealed class LiveTranslationHub
{
    private readonly LiveAudioTranslationOptions _options;
    private readonly object _ingestSync = new();
    private readonly StreamingAudioSegmenter _segmenter;
    private readonly Channel<PendingUtterance> _utterances;
    private readonly ConcurrentQueue<TranslatedCue> _cues = new();
    private readonly ConcurrentQueue<string> _status = new();
    private readonly Task _worker;
    private int _generation;

    public LiveTranslationHub(LiveAudioTranslationOptions options)
    {
        _options = options;
        _utterances = Channel.CreateBounded<PendingUtterance>(new BoundedChannelOptions(2)
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
        lock (_ingestSync)
            _segmenter.PushFloat32(samples, sampleRate, channels);
    }

    public void PushPcm16(ReadOnlySpan<short> samples, int sampleRate, int channels)
    {
        lock (_ingestSync)
            _segmenter.PushPcm16(samples, sampleRate, channels);
    }

    public void ResetAudio()
    {
        lock (_ingestSync)
        {
            Interlocked.Increment(ref _generation);
            _segmenter.Reset();
        }
        while (_utterances.Reader.TryRead(out _))
        {
        }
        while (_cues.TryDequeue(out _))
        {
        }
    }

    public void BeginSession() => ResetAudio();

    public void EndSession() => ResetAudio();

    public bool TryTakeCue(out TranslatedCue cue) => _cues.TryDequeue(out cue);

    public bool TryTakeStatus(out string message) => _status.TryDequeue(out message!);

    private void QueueUtterance(float[] samples)
    {
        var utterance = new PendingUtterance(samples, Volatile.Read(ref _generation));
        if (!_utterances.Writer.TryWrite(utterance))
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

            await foreach (PendingUtterance pending in _utterances.Reader.ReadAllAsync())
            {
                var total = Stopwatch.StartNew();
                string english = await TranscribeAsync(whisper, pending.Samples, CancellationToken.None);
                if (pending.Generation != Volatile.Read(ref _generation))
                    continue;
                if (english.Length == 0)
                {
                    _status.Enqueue("event=utterance outcome=no-speech");
                    continue;
                }

                var translationTimer = Stopwatch.StartNew();
                string french = cache.GetOrTranslate(english, translator);
                translationTimer.Stop();
                total.Stop();
                if (pending.Generation != Volatile.Read(ref _generation))
                    continue;

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
                    $"audio_ms={pending.Samples.Length * 1_000 / StreamingAudioSegmenter.OutputSampleRate} " +
                    $"translation_ms={translationTimer.Elapsed.TotalMilliseconds:F1} " +
                    $"total_ms={total.Elapsed.TotalMilliseconds:F1} outcome=queued");
            }
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
