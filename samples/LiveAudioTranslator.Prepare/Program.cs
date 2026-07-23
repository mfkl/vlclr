using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LiveAudioTranslator;
using SubtitleTranslator;
using Whisper.net;
using Whisper.net.LibraryLoader;

return await PreparationProgram.RunAsync(args);

internal static class PreparationProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        TimedCueFileWriter? writer = null;
        PreparationOptions? options = null;
        var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            options = PreparationOptions.Parse(args);
            using var wave = new WaveReader(options.WavePath);
            string whisperIdentity = ValidateWhisperBundle(options.WhisperModelPath);
            if (!string.IsNullOrWhiteSpace(options.WhisperRuntimePath) &&
                !File.Exists(options.WhisperRuntimePath))
            {
                throw new FileNotFoundException("Whisper runtime not found.", options.WhisperRuntimePath);
            }
            _ = ModelManifest.LoadAndValidate(
                options.TranslationModelPath,
                "en",
                options.TargetLanguage,
                verifyHashes: true);
            NativeLoadResult nativeLoad = OnnxNativeResolver.EnsureLoadedResult(options.TranslationModelPath);
            if (!nativeLoad.Success)
                throw new InvalidOperationException(nativeLoad.Diagnostics);

            ConfigureWhisperRuntime(options.WhisperRuntimePath);
            var initialization = Stopwatch.StartNew();
            using var whisperFactory = WhisperFactory.FromPath(options.WhisperModelPath);
            WhisperProcessorBuilder builder = whisperFactory.CreateBuilder()
                .WithThreads(options.WhisperThreads)
                .WithTranslate()
                .WithNoContext()
                .WithSingleSegment();
            builder = options.SourceLanguage == "auto"
                ? builder.WithLanguageDetection()
                : builder.WithLanguage(options.SourceLanguage);
            using var whisper = builder.Build();
            using var translator = new OnnxTranslator(
                options.TranslationModelPath,
                "en",
                options.TargetLanguage,
                new OnnxTranslatorOptions
                {
                    IntraOpThreads = options.TranslationThreads,
                    MaximumSourceTokens = 128,
                    MaximumOutputTokens = 128,
                    UseDecoderCache = true,
                    CacheActivationTokenCount = 32,
                    VerifyModelHashes = false
                });
            _ = await TranscribeAsync(whisper, new float[8_000], cancellation.Token);
            _ = translator.Translate("model warm up");
            initialization.Stop();

            string generation = Guid.NewGuid().ToString("D");
            var manifest = new TimedCueManifest
            {
                MediaIdentity = NormalizeMediaIdentity(options.MediaIdentity),
                SourceLanguage = options.SourceLanguage,
                TargetLanguage = options.TargetLanguage,
                WhisperModelIdentity = whisperIdentity,
                AudioDurationTicks = wave.DurationTicks,
                TimelineOffsetTicks = options.TimelineOffsetTicks,
                GenerationId = generation
            };
            writer = new TimedCueFileWriter(options.CueFilePath, manifest);
            var audioTimer = Stopwatch.StartNew();
            var whisperDuration = TimeSpan.Zero;
            var translationDuration = TimeSpan.Zero;
            var emitted = new Queue<TimedAudioSegment>();
            var segmenter = new StreamingAudioSegmenter(
                options.VadThreshold,
                options.SilenceMilliseconds,
                options.MaximumUtteranceMilliseconds,
                emitted.Enqueue);
            var cache = new TranslationCache(512);
            short[] buffer = new short[StreamingAudioSegmenter.OutputSampleRate];
            long samplesRead = 0;
            long cueCount = 0;
            long lastCueStart = -1;
            long previousCueEnd = 0;
            bool previousForcedSplit = false;
            string previousEnglish = "";

            Console.WriteLine(
                $"event=ready model_init_ms={initialization.Elapsed.TotalMilliseconds:F1} " +
                $"audio_duration_ticks={wave.DurationTicks} whisper_threads={options.WhisperThreads} " +
                $"translation_threads={options.TranslationThreads}");

            while (true)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                int count = wave.ReadSamples(buffer);
                if (count == 0)
                    break;
                long blockStart = samplesRead * 1_000_000L / wave.SampleRate;
                long blockEnd = (samplesRead + count) * 1_000_000L / wave.SampleRate;
                segmenter.PushPcm16(
                    buffer.AsSpan(0, count),
                    wave.SampleRate,
                    wave.Channels,
                    blockStart,
                    blockEnd - blockStart);
                samplesRead += count;
                await DrainSegmentsAsync();
                WriteProgress(complete: false, error: null);
            }

            segmenter.Flush();
            await DrainSegmentsAsync();
            audioTimer.Stop();
            WriteProgress(complete: true, error: null);
            Console.WriteLine(
                $"event=complete audio_seconds={wave.DurationTicks / 1_000_000d:F3} " +
                $"processing_ms={audioTimer.Elapsed.TotalMilliseconds:F1} " +
                $"rtf={audioTimer.Elapsed.TotalMilliseconds * 1000d / wave.DurationTicks:F4} " +
                $"whisper_ms={whisperDuration.TotalMilliseconds:F1} " +
                $"translation_ms={translationDuration.TotalMilliseconds:F1} cues={cueCount}");
            return 0;

            async Task DrainSegmentsAsync()
            {
                while (emitted.TryDequeue(out TimedAudioSegment segment))
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var whisperTimer = Stopwatch.StartNew();
                    string rawEnglish = await TranscribeAsync(whisper, segment.Samples, cancellation.Token);
                    whisperTimer.Stop();
                    whisperDuration += whisperTimer.Elapsed;
                    string english = previousForcedSplit
                        ? TranscriptStitcher.RemoveForcedSplitOverlap(previousEnglish, rawEnglish)
                        : rawEnglish;
                    previousEnglish = rawEnglish;
                    if (english.Length == 0)
                    {
                        previousForcedSplit = segment.ForcedSplit;
                        continue;
                    }

                    var translationTimer = Stopwatch.StartNew();
                    string translated = TimedCueText.Normalize(cache.GetOrTranslate(english, translator));
                    translationTimer.Stop();
                    translationDuration += translationTimer.Elapsed;
                    if (translated.Length == 0)
                    {
                        previousForcedSplit = segment.ForcedSplit;
                        continue;
                    }

                    long start = segment.StartMediaTicks;
                    if (previousForcedSplit)
                        start = Math.Max(start, previousCueEnd);
                    start = Math.Max(start, lastCueStart);
                    long end = Math.Max(start + 1, segment.EndMediaTicks);
                    if (end - start < 250_000)
                    {
                        previousForcedSplit = segment.ForcedSplit;
                        continue;
                    }
                    var cue = new TimedCue(cueCount, start, end, translated).NormalizeAndValidate();
                    writer.Append(cue);
                    cueCount++;
                    lastCueStart = start;
                    previousCueEnd = end;
                    previousForcedSplit = segment.ForcedSplit;
                    Console.WriteLine(
                        $"event=cue cue={TranslationTextNormalizer.ComputeCueHash(english)} " +
                        $"sequence={cue.Sequence} start_ticks={cue.StartMediaTicks} end_ticks={cue.EndMediaTicks} " +
                        $"whisper_ms={whisperTimer.Elapsed.TotalMilliseconds:F1} " +
                        $"translation_ms={translationTimer.Elapsed.TotalMilliseconds:F1}");
                }
            }

            void WriteProgress(bool complete, string? error)
            {
                long processedTicks = samplesRead * 1_000_000L / wave.SampleRate;
                writer.WriteProgress(new TimedCueProgress
                {
                    GenerationId = generation,
                    ProcessedAudioTicks = processedTicks,
                    PreparedThroughTicks = processedTicks,
                    AudioDurationTicks = wave.DurationTicks,
                    ProcessingWallMilliseconds = (long)audioTimer.Elapsed.TotalMilliseconds,
                    CueCount = cueCount,
                    Complete = complete,
                    Error = error
                });
            }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("event=failed error=cancelled");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"event=failed error={Sanitize($"{ex.GetType().Name}:{ex.Message}")}");
            return 1;
        }
        finally
        {
            writer?.Dispose();
            Console.CancelKeyPress -= cancelHandler;
            cancellation.Dispose();
        }
    }

    private static async Task<string> TranscribeAsync(
        WhisperProcessor processor,
        float[] samples,
        CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        await foreach (SegmentData segment in processor.ProcessAsync(samples, cancellationToken))
        {
            string part = segment.Text.Trim();
            if (part.Length == 0 ||
                (part.StartsWith('[') && part.EndsWith(']')) ||
                (part.StartsWith('(') && part.EndsWith(')')))
            {
                continue;
            }
            if (text.Length > 0)
                text.Append(' ');
            text.Append(part);
        }
        return TranslationTextNormalizer.NormalizeCacheKey(text.ToString());
    }

    private static string ValidateWhisperBundle(string modelPath)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("Whisper model not found.", modelPath);
        string? directory = Path.GetDirectoryName(modelPath);
        string manifestPath = Path.Combine(directory ?? "", "model-manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("Whisper model manifest not found.", manifestPath);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        JsonElement root = document.RootElement;
        if (root.GetProperty("formatVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported Whisper model manifest version.");
        string fileName = Path.GetFileName(modelPath);
        JsonElement file = root.GetProperty("files").EnumerateArray()
            .FirstOrDefault(item => string.Equals(
                item.GetProperty("fileName").GetString(), fileName, StringComparison.OrdinalIgnoreCase));
        if (file.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException("Whisper model is not declared by its bundle manifest.");
        long expectedSize = file.GetProperty("size").GetInt64();
        string expectedHash = file.GetProperty("sha256").GetString() ?? "";
        if (new FileInfo(modelPath).Length != expectedSize)
            throw new InvalidDataException("Whisper model size does not match its manifest.");
        using var stream = File.OpenRead(modelPath);
        string actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Whisper model hash does not match its manifest.");
        string variant = root.TryGetProperty("modelVariant", out JsonElement variantElement)
            ? variantElement.GetString() ?? fileName
            : fileName;
        return $"{variant}:{actualHash[..16]}";
    }

    private static void ConfigureWhisperRuntime(string? runtimePath)
    {
        if (string.IsNullOrWhiteSpace(runtimePath))
            return;
        string runtimeDirectory = Path.GetDirectoryName(runtimePath)!;
        string runtimesDirectory = Path.GetDirectoryName(runtimeDirectory)!;
        string searchRoot = Path.GetDirectoryName(runtimesDirectory)!;
        RuntimeOptions.LibraryPath = Path.Combine(searchRoot, "whisper-loader-anchor.dll");
    }

    private static string NormalizeMediaIdentity(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.IsAbsoluteUri)
            return uri.AbsoluteUri;
        return new Uri(Path.GetFullPath(value)).AbsoluteUri;
    }

    private static string Sanitize(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Replace(' ', '-');
}

internal sealed record PreparationOptions
{
    public required string WavePath { get; init; }
    public required string CueFilePath { get; init; }
    public required string MediaIdentity { get; init; }
    public required string WhisperModelPath { get; init; }
    public string? WhisperRuntimePath { get; init; }
    public required string TranslationModelPath { get; init; }
    public string SourceLanguage { get; init; } = "auto";
    public string TargetLanguage { get; init; } = "fr";
    public int WhisperThreads { get; init; } = 2;
    public int TranslationThreads { get; init; } = 1;
    public float VadThreshold { get; init; } = 0.012f;
    public int SilenceMilliseconds { get; init; } = 400;
    public int MaximumUtteranceMilliseconds { get; init; } = 2_500;
    public long TimelineOffsetTicks { get; init; }

    public static PreparationOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected argument: {argument}");
            string key;
            string value;
            int equals = argument.IndexOf('=');
            if (equals >= 0)
            {
                key = argument[2..equals];
                value = argument[(equals + 1)..];
            }
            else
            {
                key = argument[2..];
                if (++index >= args.Length)
                    throw new ArgumentException($"Missing value for --{key}.");
                value = args[index];
            }
            values[key] = value;
        }

        string Required(string key) => values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing required --{key} option.");
        int Integer(string key, int fallback, int minimum, int maximum) => values.TryGetValue(key, out string? value)
            ? Math.Clamp(int.Parse(value, CultureInfo.InvariantCulture), minimum, maximum)
            : fallback;
        float Float(string key, float fallback, float minimum, float maximum) => values.TryGetValue(key, out string? value)
            ? Math.Clamp(float.Parse(value, CultureInfo.InvariantCulture), minimum, maximum)
            : fallback;

        return new PreparationOptions
        {
            WavePath = Path.GetFullPath(Required("wave")),
            CueFilePath = Path.GetFullPath(Required("cue-file")),
            MediaIdentity = Required("media"),
            WhisperModelPath = Path.GetFullPath(Required("whisper-model")),
            WhisperRuntimePath = values.TryGetValue("whisper-runtime", out string? runtime)
                ? Path.GetFullPath(runtime)
                : null,
            TranslationModelPath = Path.GetFullPath(Required("translation-model")),
            SourceLanguage = values.GetValueOrDefault("source-language", "auto").Trim().ToLowerInvariant(),
            TargetLanguage = values.GetValueOrDefault("target-language", "fr").Trim().ToLowerInvariant(),
            WhisperThreads = Integer("whisper-threads", 2, 1, 8),
            TranslationThreads = Integer("translation-threads", 1, 1, 8),
            VadThreshold = Float("vad-threshold", 0.012f, 0.001f, 0.25f),
            SilenceMilliseconds = Integer("silence-ms", 400, 200, 1_000),
            MaximumUtteranceMilliseconds = Integer("max-utterance-ms", 2_500, 1_000, 10_000),
            TimelineOffsetTicks = values.TryGetValue("timeline-offset-ticks", out string? offset)
                ? long.Parse(offset, CultureInfo.InvariantCulture)
                : 0
        };
    }
}
