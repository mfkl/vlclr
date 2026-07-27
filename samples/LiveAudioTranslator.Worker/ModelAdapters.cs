using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SubtitleTranslator;
using VLCLR.LiveTranslation.Models;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace LiveAudioTranslator.Worker;

internal static class PackagedProviders
{
#if WHISPER_OPENVINO
    public const string Speech = "openvino";
    public static RuntimeLibrary SpeechRuntime => RuntimeLibrary.OpenVino;
#elif WHISPER_VULKAN
    public const string Speech = "vulkan";
    public static RuntimeLibrary SpeechRuntime => RuntimeLibrary.Vulkan;
#else
    public const string Speech = "cpu";
    public static RuntimeLibrary SpeechRuntime => RuntimeLibrary.Cpu;
#endif

#if ORT_DIRECTML
    public const string Translation = "directml";
    public const string TranslationRuntimeVersion = "ONNX Runtime DirectML 1.24.4";
#elif ORT_OPENVINO
    public const string Translation = "openvino";
    public const string TranslationRuntimeVersion = "ONNX Runtime OpenVINO 1.27.1";
#else
    public const string Translation = "cpu";
    public const string TranslationRuntimeVersion = "ONNX Runtime 1.27.1";
#endif
}

internal sealed class PackagedInferenceProviderFactory : IInferenceProviderFactory
{
    public PackagedInferenceProviderFactory(string providerId) => ProviderId = providerId;

    public string ProviderId { get; }
    public string RuntimeVersion =>
        ProviderId is "cpu" or "openvino" or "vulkan" ? "Whisper.net 1.9.1" : "ONNX Runtime 1.27.1";

    public bool IsAvailable(ModelProfile profile, out string reason)
    {
        bool compatible = profile.CompatibleProviders.Contains(ProviderId, StringComparer.Ordinal);
        reason = compatible ? "" : $"Profile '{profile.Id}' does not support '{ProviderId}'.";
        return compatible;
    }

    public InferenceProviderSelection CreateSelection(ModelProfile profile)
    {
        if (!IsAvailable(profile, out string reason))
            throw new InvalidOperationException(reason);
        return new InferenceProviderSelection(
            ProviderId,
            RuntimeVersion,
            AppContext.BaseDirectory,
            new Dictionary<string, string>(StringComparer.Ordinal));
    }
}

internal sealed class WhisperSpeechRecognizerFactory : ISpeechRecognizerFactory
{
    public string AdapterId => "whisper.net";

    public bool Supports(ModelProfile profile) =>
        string.Equals(profile.AdapterId, AdapterId, StringComparison.Ordinal);

    public ISpeechRecognizer Create(
        ResolvedModelProfile profile,
        InferenceProviderSelection provider,
        string sourceLanguage,
        int threadCount)
    {
        if (!Supports(profile.Profile))
            throw new InvalidOperationException($"Unsupported speech adapter '{profile.Profile.AdapterId}'.");
        if (!string.Equals(provider.ProviderId, PackagedProviders.Speech, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"This worker packages speech provider '{PackagedProviders.Speech}', not '{provider.ProviderId}'.");
        }

        string modelPath = SpeechManifest.LoadAndValidate(profile.ManifestPath).ModelPath;
        RuntimeOptions.RuntimeLibraryOrder = [PackagedProviders.SpeechRuntime];
        var factory = WhisperFactory.FromPath(modelPath);
        WhisperProcessorBuilder builder = factory.CreateBuilder()
            .WithThreads(Math.Clamp(threadCount, 1, Environment.ProcessorCount))
            .WithTranslate()
            .WithNoContext()
            .WithSingleSegment();
#if WHISPER_OPENVINO
        IReadOnlyList<ValidatedRuntimeAsset> encoderAssets =
            PackagedRuntimeAssets.LoadAndValidate(
                Path.Combine(
                    profile.ModelDirectory,
                    "openvino-tiny",
                    "model-manifest.json"),
                "whisper-openvino-encoder-tiny");
        string encoderManifest = encoderAssets.Single(asset =>
            string.Equals(
                Path.GetExtension(asset.FileName),
                ".xml",
                StringComparison.OrdinalIgnoreCase)).FullPath;
        builder = builder.WithOpenVinoEncoder(encoderManifest, "GPU", null);
#endif
        builder = string.Equals(sourceLanguage, "auto", StringComparison.Ordinal)
            ? builder.WithLanguageDetection()
            : builder.WithLanguage(sourceLanguage);
        return new WhisperSpeechRecognizer(factory, builder.Build());
    }
}

internal sealed class WhisperSpeechRecognizer(
    WhisperFactory factory,
    WhisperProcessor processor) : ISpeechRecognizer, IAsyncDisposable
{
    public async ValueTask<SpeechRecognitionResult> RecognizeEnglishAsync(
        ReadOnlyMemory<float> mono16KhzSamples,
        CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var text = new StringBuilder();
        await foreach (SegmentData segment in processor.ProcessAsync(
                           mono16KhzSamples.ToArray(),
                           cancellationToken).ConfigureAwait(false))
        {
            string part = segment.Text.Trim();
            if (part.Length == 0 || IsNonSpeechLabel(part))
                continue;
            if (text.Length > 0)
                text.Append(' ');
            text.Append(part);
        }
        timer.Stop();
        return new SpeechRecognitionResult(
            TranslationTextNormalizer.NormalizeCacheKey(text.ToString()),
            timer.Elapsed);
    }

    public void Dispose()
    {
        processor.Dispose();
        factory.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await processor.DisposeAsync().ConfigureAwait(false);
        factory.Dispose();
    }

    private static bool IsNonSpeechLabel(string text) =>
        (text.StartsWith('[') && text.EndsWith(']')) ||
        (text.StartsWith('(') && text.EndsWith(')'));
}

internal sealed class MarianTranslationEngineFactory : ITranslationEngineFactory
{
    public string AdapterId => "marian-onnx";

    public bool Supports(string modelFamily) =>
        modelFamily.Contains("Marian", StringComparison.OrdinalIgnoreCase);

    public ITranslationEngine Create(
        string modelDirectory,
        string sourceLanguage,
        string targetLanguage,
        string providerId,
        int threadCount) =>
        new OnnxTranslator(
            modelDirectory,
            sourceLanguage,
            targetLanguage,
            new OnnxTranslatorOptions
            {
                ProviderId = providerId,
                IntraOpThreads = threadCount,
                MaximumSourceTokens = 128,
                MaximumOutputTokens = 128,
                UseDecoderCache = true,
                CacheActivationTokenCount = 32,
                VerifyModelHashes = true
            });
}

internal sealed record SpeechManifestResult(string ModelPath, string Hash);

internal static class SpeechManifest
{
    public static SpeechManifestResult LoadAndValidate(string manifestPath)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        JsonElement root = document.RootElement;
        if (root.GetProperty("formatVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported speech manifest version.");
        JsonElement[] files = root.GetProperty("files").EnumerateArray().ToArray();
        JsonElement file = files.Single(element =>
            string.Equals(
                element.GetProperty("role").GetString(),
                "speech-to-english",
                StringComparison.Ordinal));
        string fileName = file.GetProperty("fileName").GetString() ?? "";
        if (Path.IsPathRooted(fileName) ||
            fileName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw new InvalidDataException("Speech manifest contains an unsafe file name.");
        }
        string modelPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, fileName);
        long expectedSize = file.GetProperty("size").GetInt64();
        if (!File.Exists(modelPath) || new FileInfo(modelPath).Length != expectedSize)
            throw new InvalidDataException("Speech model size validation failed.");
        string expectedHash = file.GetProperty("sha256").GetString() ?? "";
        using var stream = File.OpenRead(modelPath);
        string actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Speech model hash validation failed.");
        return new SpeechManifestResult(modelPath, actualHash);
    }
}
