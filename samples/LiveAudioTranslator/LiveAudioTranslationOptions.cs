using SubtitleTranslator;
using VLCLR.Plugin;

namespace LiveAudioTranslator;

internal sealed record LiveAudioTranslationOptions
{
    public required string WhisperModelPath { get; init; }
    public required string WhisperRuntimePath { get; init; }
    public required string TranslationModelPath { get; init; }
    public string SourceLanguage { get; init; } = "auto";
    public string TargetLanguage { get; init; } = "fr";
    public int WhisperThreads { get; init; } = 4;
    public int TranslationThreads { get; init; } = 4;
    public float VadThreshold { get; init; } = 0.012f;
    public int SilenceMilliseconds { get; init; } = 650;
    public int MaximumUtteranceMilliseconds { get; init; } = 6_000;
    public int SubtitleDurationMilliseconds { get; init; } = 3_500;

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

        return new LiveAudioTranslationOptions
        {
            WhisperModelPath = Path.GetFullPath(whisperPath),
            WhisperRuntimePath = Path.GetFullPath(whisperRuntimePath),
            TranslationModelPath = Path.GetFullPath(translationPath),
            SourceLanguage = NormalizeLanguage(values.GetString("live-translator-source-language"), "auto"),
            TargetLanguage = NormalizeLanguage(values.GetString("live-translator-target-language"), "fr"),
            WhisperThreads = ClampThreads(values.GetInteger("live-translator-whisper-threads", 4)),
            TranslationThreads = ClampThreads(values.GetInteger("live-translator-translation-threads", 4)),
            VadThreshold = Math.Clamp(values.GetFloat("live-translator-vad-threshold", 0.012f), 0.001f, 0.25f),
            SilenceMilliseconds = (int)Math.Clamp(values.GetInteger("live-translator-silence-ms", 650), 200, 3_000),
            MaximumUtteranceMilliseconds =
                (int)Math.Clamp(values.GetInteger("live-translator-max-utterance-ms", 6_000), 1_000, 20_000),
            SubtitleDurationMilliseconds =
                (int)Math.Clamp(values.GetInteger("live-translator-subtitle-duration-ms", 3_500), 500, 10_000)
        };
    }

    private static int ClampThreads(long value) =>
        (int)Math.Clamp(value, 1, Math.Max(1, Environment.ProcessorCount));

    private static string NormalizeLanguage(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
}
