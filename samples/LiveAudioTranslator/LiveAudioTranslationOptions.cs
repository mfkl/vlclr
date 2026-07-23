using SubtitleTranslator;
using VLCLR.Plugin;

namespace LiveAudioTranslator;

internal enum LiveAudioTranslationMode
{
    Synchronized,
    Live
}

internal sealed record LiveAudioTranslationOptions
{
    public required LiveAudioTranslationMode Mode { get; init; }
    public required string CueFilePath { get; init; }
    public required string WhisperModelPath { get; init; }
    public required string WhisperRuntimePath { get; init; }
    public required string TranslationModelPath { get; init; }
    public string SourceLanguage { get; init; } = "auto";
    public string TargetLanguage { get; init; } = "fr";
    public int WhisperThreads { get; init; } = 2;
    public int TranslationThreads { get; init; } = 1;
    public float VadThreshold { get; init; } = 0.012f;
    public int SilenceMilliseconds { get; init; } = 400;
    public int MaximumUtteranceMilliseconds { get; init; } = 2_500;
    public int SubtitleDurationMilliseconds { get; init; } = 2_500;
    public int MaximumCaptionAgeMilliseconds { get; init; } = 3_500;
    public int EarlyCueToleranceMilliseconds { get; init; } = 80;
    public int StaleClockMilliseconds { get; init; } = 2_000;

    public static LiveAudioTranslationOptions Read(nint objectPtr)
    {
        var values = new VLCConfiguration(objectPtr);
        string? hostRoot = OnnxNativeResolver.GetHostRootDirectory();
        string baseDirectory = hostRoot ?? AppContext.BaseDirectory;

        string whisperPath = values.GetString("live-translator-whisper-model") ?? "";
        if (string.IsNullOrWhiteSpace(whisperPath))
            whisperPath = Path.Combine(baseDirectory, "models", "whisper", "ggml-tiny.bin");

        string whisperRuntimePath = values.GetString("live-translator-whisper-runtime") ?? "";
        if (string.IsNullOrWhiteSpace(whisperRuntimePath))
            whisperRuntimePath = Path.Combine(baseDirectory, "runtimes", "win-x64", "whisper.dll");

        string translationPath = values.GetString("live-translator-translation-model") ?? "";
        if (string.IsNullOrWhiteSpace(translationPath))
            translationPath = Path.Combine(baseDirectory, "models", "opus-mt-en-fr");

        string modeValue = values.GetString("live-translator-mode", "sync") ?? "sync";
        LiveAudioTranslationMode mode = string.Equals(modeValue.Trim(), "live", StringComparison.OrdinalIgnoreCase)
            ? LiveAudioTranslationMode.Live
            : LiveAudioTranslationMode.Synchronized;
        string cueFile = values.GetString("live-translator-cue-file") ?? "";

        return new LiveAudioTranslationOptions
        {
            Mode = mode,
            CueFilePath = string.IsNullOrWhiteSpace(cueFile) ? "" : Path.GetFullPath(cueFile),
            WhisperModelPath = Path.GetFullPath(whisperPath),
            WhisperRuntimePath = Path.GetFullPath(whisperRuntimePath),
            TranslationModelPath = Path.GetFullPath(translationPath),
            SourceLanguage = NormalizeLanguage(values.GetString("live-translator-source-language"), "auto"),
            TargetLanguage = NormalizeLanguage(values.GetString("live-translator-target-language"), "fr"),
            WhisperThreads = ClampThreads(values.GetInteger("live-translator-whisper-threads", 2)),
            TranslationThreads = ClampThreads(values.GetInteger("live-translator-translation-threads", 1)),
            VadThreshold = Math.Clamp(values.GetFloat("live-translator-vad-threshold", 0.012f), 0.001f, 0.25f),
            SilenceMilliseconds = (int)Math.Clamp(values.GetInteger("live-translator-silence-ms", 400), 200, 1_000),
            MaximumUtteranceMilliseconds =
                (int)Math.Clamp(values.GetInteger("live-translator-max-utterance-ms", 2_500), 1_000, 4_000),
            SubtitleDurationMilliseconds =
                (int)Math.Clamp(values.GetInteger("live-translator-subtitle-duration-ms", 2_500), 500, 5_000),
            MaximumCaptionAgeMilliseconds =
                (int)Math.Clamp(values.GetInteger("live-translator-maximum-age-ms", 3_500), 500, 10_000),
            EarlyCueToleranceMilliseconds =
                (int)Math.Clamp(values.GetInteger("live-translator-early-tolerance-ms", 80), 0, 500),
            StaleClockMilliseconds =
                (int)Math.Clamp(values.GetInteger("live-translator-stale-clock-ms", 2_000), 250, 10_000)
        };
    }

    private static int ClampThreads(long value) =>
        (int)Math.Clamp(value, 1, Math.Max(1, Math.Min(8, Environment.ProcessorCount)));

    private static string NormalizeLanguage(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
}
