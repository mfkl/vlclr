using System.Reflection;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using VLCLR.Imaging;
using VLCLR.Native;
using VLCLR.Plugin;
using VLCLR.Rendering;
using VLCLR.Text;

namespace SubtitleTranslator;

[VLCModule("dotnet_subtitle_translator")]
[VLCCapability("text renderer", Score = 1)]
[VLCDescription("Offline subtitle translator (.NET Native AOT + ONNX Runtime)")]
[VLCConfig("translator-source-lang", VLCConfigType.String, Default = "en",
    Description = "Source language", LongDescription = "Source language ISO code")]
[VLCConfig("translator-target-lang", VLCConfigType.String, Default = "fr",
    Description = "Target language", LongDescription = "Target language ISO code")]
[VLCConfig("translator-model-path", VLCConfigType.Directory, Default = "",
    Description = "Model directory", LongDescription = "Manifest-based ONNX model directory; auto-detected when empty")]
[VLCConfig("translator-provider", VLCConfigType.String, Default = "cpu",
    Description = "Inference provider", LongDescription = "Inference provider; the shipping implementation supports cpu")]
[VLCConfig("translator-threads", VLCConfigType.Integer, Default = 4, Min = 1, Max = 64,
    Description = "ONNX CPU threads", LongDescription = "ONNX Runtime intra-op CPU thread count")]
[VLCConfig("translator-max-source-tokens", VLCConfigType.Integer, Default = 128, Min = 1, Max = 512,
    Description = "Maximum source tokens", LongDescription = "Reject cues with more source tokens")]
[VLCConfig("translator-max-output-tokens", VLCConfigType.Integer, Default = 128, Min = 1, Max = 512,
    Description = "Maximum output tokens", LongDescription = "Stop generation after this many tokens")]
[VLCConfig("translator-deadline-ms", VLCConfigType.Integer, Default = 500, Min = 1, Max = 5000,
    Description = "Translation deadline", LongDescription = "Maximum time the renderer waits for a cache miss")]
[VLCConfig("translator-cache-size", VLCConfigType.Integer, Default = 512, Min = 1, Max = 8192,
    Description = "Translation cache size", LongDescription = "Maximum number of translated cues retained in memory")]
[VLCConfig("translator-show-original-on-timeout", VLCConfigType.Bool, Default = true,
    Description = "Show original on timeout", LongDescription = "Render the original cue when translation misses its deadline")]
public partial class TranslatorPlugin : VLCTextRendererBase
{
    private const int DefaultWidth = 1920;
    private TranslationServiceLease? _serviceLease;
    private TextCanvas? _canvas;
    private string _sourceLanguage = "en";
    private string _targetLanguage = "fr";
    private int _deadlineMilliseconds = 500;
    private bool _showOriginalOnTimeout = true;
    private string? _initializationFailure;
    private long _renderCount;

    protected override bool OnOpen(VLCRendererContext context)
    {
        context.Logger.Info("[SubtitleTranslator] Opening translator plugin");

        try
        {
            FontManager.LoadEmbeddedFont(
                Assembly.GetExecutingAssembly(),
                "SubtitleTranslator.Resources.JetBrainsMono-Regular.ttf",
                setAsDefault: true);

            var config = Config;
            _sourceLanguage = NormalizeLanguage(config.TranslatorSourceLang, "en");
            _targetLanguage = NormalizeLanguage(config.TranslatorTargetLang, "fr");
            string provider = (config.TranslatorProvider ?? "cpu").Trim().ToLowerInvariant();
            int threads = (int)Math.Clamp(config.TranslatorThreads, 1, Environment.ProcessorCount);
            int maximumSourceTokens = (int)Math.Clamp(config.TranslatorMaxSourceTokens, 1, 512);
            int maximumOutputTokens = (int)Math.Clamp(config.TranslatorMaxOutputTokens, 1, 512);
            _deadlineMilliseconds = (int)Math.Clamp(config.TranslatorDeadlineMs, 1, 5000);
            int cacheCapacity = (int)Math.Clamp(config.TranslatorCacheSize, 1, 8192);
            _showOriginalOnTimeout = config.TranslatorShowOriginalOnTimeout;

            if (!string.Equals(provider, "cpu", StringComparison.Ordinal))
            {
                _initializationFailure = $"unsupported-provider-{provider}";
                context.Logger.Warning(
                    $"[SubtitleTranslator] Provider '{provider}' is not available; original subtitles will be rendered.");
                return true;
            }

            string? modelDirectory = ResolveModelPath(
                config.TranslatorModelPath,
                _sourceLanguage,
                _targetLanguage);
            if (modelDirectory == null)
            {
                _initializationFailure = "model-not-found";
                context.Logger.Warning(
                    "[SubtitleTranslator] Model bundle not found; original subtitles will be rendered. " +
                    "Set --translator-model-path to a directory containing model-manifest.json.");
                return true;
            }

            NativeLoadResult nativeLoad = OnnxNativeResolver.EnsureLoadedResult(modelDirectory);
            foreach (string line in nativeLoad.Diagnostics.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                context.Logger.Info($"[SubtitleTranslator] resolver {line.Trim()}");
            if (!nativeLoad.Success)
            {
                _initializationFailure = "onnxruntime-load-failed";
                context.Logger.Warning("[SubtitleTranslator] ONNX Runtime unavailable; original subtitles will be rendered.");
                return true;
            }

            int queueCapacity = Math.Clamp(cacheCapacity / 64, 2, 16);
            var serviceKey = new TranslationServiceKey(
                modelDirectory,
                _sourceLanguage,
                _targetLanguage,
                provider,
                threads,
                maximumSourceTokens,
                maximumOutputTokens,
                cacheCapacity,
                queueCapacity);
            _serviceLease = TranslationServiceRegistry.Acquire(
                serviceKey,
                () => new OnnxTranslator(
                    modelDirectory,
                    _sourceLanguage,
                    _targetLanguage,
                    new OnnxTranslatorOptions
                    {
                        IntraOpThreads = threads,
                        MaximumSourceTokens = maximumSourceTokens,
                        MaximumOutputTokens = maximumOutputTokens,
                        UseDecoderCache = true,
                        VerifyModelHashes = true
                    }));

            TranslationResponse warmup = _serviceLease.Service.Translate("Hello", TimeSpan.FromSeconds(10));
            if (!warmup.IsSuccess)
            {
                _serviceLease.Dispose();
                _serviceLease = null;
                _initializationFailure = $"warmup-{warmup.Outcome.ToString().ToLowerInvariant()}";
                context.Logger.Warning(
                    $"[SubtitleTranslator] Model warm-up failed ({warmup.Outcome}); original subtitles will be rendered.");
                return true;
            }

            context.Logger.Info(
                $"[SubtitleTranslator] Model ready source={_sourceLanguage} target={_targetLanguage} " +
                $"provider=cpu threads={threads} runtime={nativeLoad.Version ?? "unknown"} " +
                $"shared_services={TranslationServiceRegistry.ActiveServiceCount}");
            return true;
        }
        catch (Exception ex)
        {
            _serviceLease?.Dispose();
            _serviceLease = null;
            _initializationFailure = ex.GetType().Name;
            context.Logger.Error(
                $"[SubtitleTranslator] Initialization failed ({ex.GetType().Name}): {ex.Message}. " +
                "Original subtitles will be rendered.");
            return true;
        }
    }

    protected override void OnClose()
    {
        Context.Logger.Info($"[SubtitleTranslator] Closing rendered={_renderCount}");
        _serviceLease?.Dispose();
        _serviceLease = null;
        _canvas?.Dispose();
        _canvas = null;
    }

    protected override unsafe nint RenderText(VLCTextRequest request)
    {
        _renderCount++;
        var segments = TextSegmentParser.ParseWithVisibility(
            RegionPtr,
            forceWhiteText: true,
            forceOutline: true,
            outlineWidth: 3);
        if (segments.Count == 0 || segments.TrueForAll(segment => segment.IsEmpty))
            return 0;

        string originalText = TextSegmentParser.GetCombinedText(segments);
        if (string.IsNullOrWhiteSpace(originalText))
            return 0;

        TranslationResponse? response = null;
        string renderedText = originalText;
        string renderedOutcome = "fallback";
        if (_serviceLease != null)
        {
            response = _serviceLease.Service.Translate(
                originalText,
                TimeSpan.FromMilliseconds(_deadlineMilliseconds));
            if (response.Value.IsSuccess)
            {
                renderedText = response.Value.Text;
                renderedOutcome = "translated";
            }
            else if (response.Value.Outcome == TranslationOutcome.DeadlineExceeded && !_showOriginalOnTimeout)
            {
                LogTranslationEvent(originalText, response, "suppressed");
                return 0;
            }
        }

        LogTranslationEvent(originalText, response, renderedOutcome);

        ref VLCFilter filter = ref Unsafe.AsRef<VLCFilter>((void*)Context.NativePtr);
        uint videoWidth = filter.FormatOut.Video.Width > 0 ? filter.FormatOut.Video.Width : DefaultWidth;
        int viewportWidth = Math.Max((int)videoWidth, 320);
        int maximumRegionWidth = request.MaxWidth > 0
            ? Math.Min(request.MaxWidth, viewportWidth)
            : (int)(viewportWidth * 0.9f);

        _canvas ??= new TextCanvas(1, 1);
        TextAlignment alignment = request.HorizontalAlignment;
        Image<Rgba32>? image = _canvas.RenderTextRegion(
            renderedText,
            segments[0].Style,
            maximumRegionWidth,
            alignment);
        if (image == null)
            return 0;

        nint outputRegion = PictureConverter.ToSubpictureRegion(
            image,
            ChromaListPtr,
            request.Alignment);
        if (outputRegion != 0 && _renderCount <= 5)
        {
            Context.Logger.Info(
                $"[SubtitleTranslator] Created compact region {image.Width}x{image.Height} alignment={alignment}");
        }

        return outputRegion;
    }

    private void LogTranslationEvent(
        string originalText,
        TranslationResponse? response,
        string renderedOutcome)
    {
        TranslationResponse value = response ?? new TranslationResponse(
            TranslationOutcome.Failed,
            originalText,
            false,
            null,
            TimeSpan.Zero,
            TimeSpan.Zero,
            _initializationFailure ?? "service-unavailable");
        TranslationResult? details = value.Details;
        string deadline = value.Outcome == TranslationOutcome.DeadlineExceeded ? "missed" : "met";
        Context.Logger.Info(
            $"[SubtitleTranslator] event cue={TranslationTextNormalizer.ComputeCueHash(originalText)} " +
            $"cache={(value.CacheHit ? "hit" : "miss")} source={_sourceLanguage} target={_targetLanguage} " +
            $"source_tokens={details?.SourceTokenCount ?? 0} output_tokens={details?.OutputTokenCount ?? 0} " +
            $"queue_ms={value.QueueDuration.TotalMilliseconds:F1} " +
            $"inference_ms={details?.InferenceDuration.TotalMilliseconds ?? 0:F1} " +
            $"deadline={deadline} service_outcome={value.Outcome.ToString().ToLowerInvariant()} " +
            $"rendered={renderedOutcome} error={value.ErrorType ?? "none"}");
    }

    private static string NormalizeLanguage(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();

    private static string? ResolveModelPath(
        string? configuredPath,
        string sourceLanguage,
        string targetLanguage)
    {
        string pairName = $"opus-mt-{sourceLanguage}-{targetLanguage}";
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            candidates.Add(configuredPath);
        }
        else
        {
            string? hostRoot = OnnxNativeResolver.GetHostRootDirectory();
            if (!string.IsNullOrWhiteSpace(hostRoot))
            {
                candidates.Add(Path.Combine(hostRoot, "models", pairName));
                candidates.Add(Path.Combine(hostRoot, "models"));
            }
            candidates.Add(Path.Combine(AppContext.BaseDirectory, "models", pairName));
            candidates.Add(Path.Combine(AppContext.BaseDirectory, "models"));
            candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "models", pairName));
            candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "models"));
        }

        foreach (string candidate in candidates)
        {
            try
            {
                if (!Directory.Exists(candidate))
                    continue;
                string resolved = ModelManifest.ResolveModelDirectory(candidate, sourceLanguage, targetLanguage);
                if (File.Exists(Path.Combine(resolved, "model-manifest.json")))
                    return resolved;
            }
            catch
            {
            }
        }

        return null;
    }
}
