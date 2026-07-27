namespace VLCLR.LiveTranslation.Models;

public sealed record SpeechRecognitionResult(
    string Text,
    TimeSpan InferenceDuration);

public interface ISpeechRecognizer : IDisposable
{
    ValueTask<SpeechRecognitionResult> RecognizeEnglishAsync(
        ReadOnlyMemory<float> mono16KhzSamples,
        CancellationToken cancellationToken);
}

public interface ISpeechRecognizerFactory
{
    string AdapterId { get; }
    bool Supports(ModelProfile profile);
    ISpeechRecognizer Create(
        ResolvedModelProfile profile,
        InferenceProviderSelection provider,
        string sourceLanguage,
        int threadCount);
}

public interface IInferenceProviderFactory
{
    string ProviderId { get; }
    string RuntimeVersion { get; }
    bool IsAvailable(ModelProfile profile, out string reason);
    InferenceProviderSelection CreateSelection(ModelProfile profile);
}

public sealed record InferenceProviderSelection(
    string ProviderId,
    string RuntimeVersion,
    string NativeRuntimeDirectory,
    IReadOnlyDictionary<string, string> Settings);

public sealed record ProviderQualificationResult
{
    public required string ProviderId { get; init; }
    public required bool Available { get; init; }
    public required bool QualityAccepted { get; init; }
    public required double TotalRealTimeFactor { get; init; }
    public required double CpuBaselineRealTimeFactor { get; init; }
    public required long PeakPrivateBytes { get; init; }
    public required string OutputHash { get; init; }
    public required string FailureReason { get; init; }

    public bool IsQualified =>
        Available &&
        QualityAccepted &&
        TotalRealTimeFactor <= 0.75 &&
        CpuBaselineRealTimeFactor > 0 &&
        TotalRealTimeFactor <= CpuBaselineRealTimeFactor * 0.8;
}
