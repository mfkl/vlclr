namespace SubtitleTranslator;

/// <summary>
/// Dependency-free translation contract used by the queue/cache layer and unit tests.
/// </summary>
public interface ITranslationEngine : IDisposable
{
    TranslationResult TranslateDetailed(string text);
}

/// <summary>
/// Creates translation adapters without coupling playback or transport code to
/// a particular model family or inference provider.
/// </summary>
public interface ITranslationEngineFactory
{
    string AdapterId { get; }
    bool Supports(string modelFamily);
    ITranslationEngine Create(
        string modelDirectory,
        string sourceLanguage,
        string targetLanguage,
        string providerId,
        int threadCount);
}

public readonly record struct TranslationResult(
    string Text,
    int[] OutputTokenIds,
    int SourceTokenCount,
    int OutputTokenCount,
    TimeSpan TokenizeDuration,
    TimeSpan EncoderDuration,
    TimeSpan DecoderDuration,
    TimeSpan DetokenizeDuration)
{
    public TimeSpan InferenceDuration => EncoderDuration + DecoderDuration;
    public TimeSpan TotalDuration => TokenizeDuration + EncoderDuration + DecoderDuration + DetokenizeDuration;
}

public sealed record OnnxTranslatorOptions
{
    public string ProviderId { get; init; } = "cpu";
    public int IntraOpThreads { get; init; } = 4;
    public int MaximumSourceTokens { get; init; } = 128;
    public int MaximumOutputTokens { get; init; } = 128;
    public bool UseDecoderCache { get; init; } = true;
    public int CacheActivationTokenCount { get; init; } = 32;
    public bool VerifyDecoderCache { get; init; }
    public float CacheParityGuardMargin { get; init; } = 0.1f;
    public bool VerifyModelHashes { get; init; } = true;
}
