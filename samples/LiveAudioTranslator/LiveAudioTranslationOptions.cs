using VLCLR.Plugin;

namespace LiveAudioTranslator;

internal enum LiveAudioTranslationMode
{
    Prepared,
    LiveImmediate,
    LiveSync
}

internal sealed record LiveAudioTranslationOptions
{
    public required LiveAudioTranslationMode Mode { get; init; }
    public required string CueFilePath { get; init; }
    public required Guid SessionId { get; init; }
    public required string PipeName { get; init; }
    public string SpeechModelId { get; init; } = "whisper-tiny-multilingual";
    public string TranslationModelId { get; init; } = "opus-mt-en-fr";
    public string SpeechProviderId { get; init; } = "auto";
    public string TranslationProviderId { get; init; } = "auto";
    public string SourceLanguage { get; init; } = "auto";
    public string TargetLanguage { get; init; } = "fr";
    public int InputDelayMilliseconds { get; init; } = 15_000;
    public int MaximumUtteranceMilliseconds { get; init; } = 2_500;
    public int BurstJitterMilliseconds { get; init; } = 2_000;
    public int SubtitleDurationMilliseconds { get; init; } = 2_500;
    public int MaximumCaptionAgeMilliseconds { get; init; } = 7_000;
    public int EarlyCueToleranceMilliseconds { get; init; } = 80;
    public int StaleClockMilliseconds { get; init; } = 2_000;
    public int ClockLeadToleranceMilliseconds { get; init; } = 1_000;

    public long TransportQueueBudgetTicks =>
        checked((long)(InputDelayMilliseconds + MaximumUtteranceMilliseconds + BurstJitterMilliseconds) * 1_000);

    public static LiveAudioTranslationOptions Read(nint objectPtr)
    {
        var values = new VLCConfiguration(objectPtr);
        string modeValue =
            values.GetString("live-translator-mode", "live-immediate") ?? "live-immediate";
        LiveAudioTranslationMode mode = ParseMode(modeValue);
        string cueFile = values.GetString("live-translator-cue-file") ?? "";
        string sessionValue = values.GetString("live-translator-session") ?? "";
        Guid sessionId = Guid.TryParse(sessionValue, out Guid parsed) ? parsed : Guid.Empty;
        string pipeName = NormalizeIdentifier(values.GetString("live-translator-pipe"), "");
        int delay = mode == LiveAudioTranslationMode.LiveSync
            ? (int)Math.Clamp(values.GetInteger("live-translator-input-delay-ms", 15_000), 8_000, 60_000)
            : 0;

        return new LiveAudioTranslationOptions
        {
            Mode = mode,
            CueFilePath = string.IsNullOrWhiteSpace(cueFile) ? "" : Path.GetFullPath(cueFile),
            SessionId = sessionId,
            PipeName = pipeName,
            SpeechModelId = NormalizeIdentifier(
                values.GetString("live-translator-speech-model"),
                "whisper-tiny-multilingual"),
            TranslationModelId = NormalizeIdentifier(
                values.GetString("live-translator-translation-model"),
                "opus-mt-en-fr"),
            SpeechProviderId = NormalizeIdentifier(
                values.GetString("live-translator-speech-provider"),
                "auto"),
            TranslationProviderId = NormalizeIdentifier(
                values.GetString("live-translator-translation-provider"),
                "auto"),
            SourceLanguage = NormalizeLanguage(values.GetString("live-translator-source-language"), "auto"),
            TargetLanguage = NormalizeLanguage(values.GetString("live-translator-target-language"), "fr"),
            InputDelayMilliseconds = delay,
            MaximumUtteranceMilliseconds =
                (int)Math.Clamp(
                    values.GetInteger("live-translator-max-utterance-ms", 2_500),
                    1_000,
                    15_000),
            BurstJitterMilliseconds =
                (int)Math.Clamp(values.GetInteger("live-translator-burst-jitter-ms", 2_000), 250, 10_000),
            SubtitleDurationMilliseconds =
                (int)Math.Clamp(values.GetInteger("live-translator-subtitle-duration-ms", 2_500), 500, 5_000),
            MaximumCaptionAgeMilliseconds =
                (int)Math.Clamp(
                    values.GetInteger("live-translator-maximum-age-ms", 7_000),
                    500,
                    10_000),
            EarlyCueToleranceMilliseconds =
                (int)Math.Clamp(values.GetInteger("live-translator-early-tolerance-ms", 80), 0, 500),
            StaleClockMilliseconds =
                (int)Math.Clamp(values.GetInteger("live-translator-stale-clock-ms", 2_000), 250, 10_000),
            ClockLeadToleranceMilliseconds =
                (int)Math.Clamp(values.GetInteger("live-translator-lead-tolerance-ms", 1_000), 250, 5_000)
        };
    }

    private static LiveAudioTranslationMode ParseMode(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "prepared" or "sync" => LiveAudioTranslationMode.Prepared,
            "live-immediate" or "live" => LiveAudioTranslationMode.LiveImmediate,
            "live-sync" => LiveAudioTranslationMode.LiveSync,
            _ => throw new InvalidOperationException(
                $"Unknown live translator mode '{value}'. Expected prepared, live-immediate, or live-sync.")
        };

    private static string NormalizeIdentifier(string? value, string fallback)
    {
        string result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
        if (result.Length > 128 || result.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.')))
        {
            throw new InvalidOperationException("Live translator identifier contains unsupported characters.");
        }
        return result;
    }

    private static string NormalizeLanguage(string? value, string fallback) =>
        NormalizeIdentifier(value, fallback);
}
