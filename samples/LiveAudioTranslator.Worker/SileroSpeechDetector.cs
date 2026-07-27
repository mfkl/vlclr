using LiveAudioTranslator;
using Whisper.net;

namespace LiveAudioTranslator.Worker;

internal sealed class SileroSpeechDetector : IStreamingSpeechDetector
{
    private readonly WhisperVadFactory _factory;
    private readonly WhisperVadProcessor _processor;
    private long _processedSamples;

    private SileroSpeechDetector(WhisperVadFactory factory, WhisperVadProcessor processor)
    {
        _factory = factory;
        _processor = processor;
    }

    public static SileroSpeechDetector Create(string speechModelDirectory, int threadCount)
    {
        string directory = Path.Combine(speechModelDirectory, "silero-vad");
        IReadOnlyList<ValidatedRuntimeAsset> assets = PackagedRuntimeAssets.LoadAndValidate(
            Path.Combine(directory, "model-manifest.json"),
            "silero-vad-v6.2.0");
        string modelPath = assets.Single(asset =>
            string.Equals(Path.GetExtension(asset.FileName), ".bin", StringComparison.OrdinalIgnoreCase))
            .FullPath;
        var factory = WhisperVadFactory.FromPath(modelPath);
        try
        {
            WhisperVadProcessor processor = factory.CreateBuilder()
                .WithThreads(Math.Clamp(threadCount, 1, Environment.ProcessorCount))
                .WithUseGpu(false)
                .WithThreshold(0.5f)
                .WithMinSpeechDuration(TimeSpan.FromMilliseconds(20))
                .WithMinSilenceDuration(TimeSpan.FromMilliseconds(20))
                .WithMaxSpeechDuration(TimeSpan.FromSeconds(10))
                .WithSpeechPadding(TimeSpan.Zero)
                .WithSamplesOverlap(TimeSpan.Zero)
                .Build();
            return new SileroSpeechDetector(factory, processor);
        }
        catch
        {
            factory.Dispose();
            throw;
        }
    }

    public bool IsSpeech(ReadOnlySpan<float> frame)
    {
        _processedSamples += frame.Length;
        IReadOnlyList<VadSegmentData> segments = _processor.DetectSpeechNoReset(frame);
        if (segments.Count == 0)
            return false;

        TimeSpan current = TimeSpan.FromSeconds(
            _processedSamples / (double)StreamingAudioSegmenter.OutputSampleRate);
        VadSegmentData last = segments[^1];
        return last.Start <= current &&
               last.End >= current - TimeSpan.FromMilliseconds(40);
    }

    public void Reset()
    {
        _processedSamples = 0;
        _processor.ResetState();
    }

    public void Dispose()
    {
        _processor.Dispose();
        _factory.Dispose();
    }
}
