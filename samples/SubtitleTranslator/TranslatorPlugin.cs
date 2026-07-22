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

/// <summary>
/// AI-powered live subtitle translator using ONNX MarianMT models.
/// Translates subtitle text in real-time, fully offline.
/// </summary>
[VLCModule("dotnet_subtitle_translator")]
[VLCCapability("text renderer", Score = 1)]
[VLCDescription("AI-powered live subtitle translator (.NET AOT + ONNX)")]
[VLCConfig("translator-source-lang", VLCConfigType.String, Default = "en",
    Description = "Source language", LongDescription = "Source language ISO code (e.g. en, de, es)")]
[VLCConfig("translator-target-lang", VLCConfigType.String, Default = "fr",
    Description = "Target language", LongDescription = "Target language ISO code (e.g. fr, de, es)")]
[VLCConfig("translator-model-path", VLCConfigType.Directory, Default = "",
    Description = "Model directory", LongDescription = "Path to ONNX model directory (auto-detected if empty)")]
public partial class TranslatorPlugin : VLCTextRendererBase
{
    private OnnxTranslator? _translator;
    private TranslationCache? _cache;
    private TextCanvas? _canvas;
    private long _renderCount;

    private const int DefaultWidth = 1920;
    protected override bool OnOpen(VLCRendererContext context)
    {
        context.Logger.Info("[SubtitleTranslator] Opening translator plugin");

        try
        {
            // Preload onnxruntime native DLL before any ONNX types are used
            context.Logger.Info("[SubtitleTranslator] Loading ONNX Runtime native library...");
            var resolverDiag = OnnxNativeResolver.EnsureLoaded();
            foreach (var line in resolverDiag.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                context.Logger.Info($"[SubtitleTranslator] Loading resolver: {line.Trim()}");
            context.Logger.Info($"[SubtitleTranslator] Loading result: {OnnxNativeResolver.LoadedFrom ?? "FAILED"}");

            // Initialize font manager with embedded JetBrains Mono font
            var assembly = Assembly.GetExecutingAssembly();
            FontManager.LoadEmbeddedFont(
                assembly,
                "SubtitleTranslator.Resources.JetBrainsMono-Regular.ttf",
                setAsDefault: true);

            // Resolve model path
            var modelPath = ResolveModelPath();
            if (modelPath == null)
            {
                context.Logger.Error("[SubtitleTranslator] Could not find ONNX models. " +
                    "Set SUBTITLE_TRANSLATOR_MODEL_PATH or run download-model.ps1");
                return false;
            }

            var config = Config;
            string sourceLang = config.TranslatorSourceLang ?? "en";
            string targetLang = config.TranslatorTargetLang ?? "fr";

            context.Logger.Info($"[SubtitleTranslator] Loading ONNX models from: {modelPath}");
            context.Logger.Info($"[SubtitleTranslator] Translation: {sourceLang} -> {targetLang}");

            _translator = new OnnxTranslator(modelPath, sourceLang, targetLang);
            _cache = new TranslationCache(capacity: 256);

            // Pre-warm the model to avoid first-subtitle latency
            context.Logger.Info("[SubtitleTranslator] Pre-warming model...");
            _translator.Warmup();
            context.Logger.Info("[SubtitleTranslator] Model ready");

            return true;
        }
        catch (Exception ex)
        {
            context.Logger.Error($"[SubtitleTranslator] Failed to initialize: {ex.Message}");
            // Log inner exceptions for TypeInitializationException
            var inner = ex.InnerException;
            while (inner != null)
            {
                context.Logger.Error($"[SubtitleTranslator]   Inner: {inner.GetType().Name}: {inner.Message}");
                inner = inner.InnerException;
            }
            context.Logger.Error($"[SubtitleTranslator]   Stack: {ex.StackTrace}");
            return false;
        }
    }

    protected override void OnClose()
    {
        Context.Logger.Info($"[SubtitleTranslator] Closing, rendered {_renderCount} subtitles");
        _translator?.Dispose();
        _canvas?.Dispose();
        _canvas = null;
    }

    protected override unsafe nint RenderText(VLCTextRequest request)
    {
        _renderCount++;

        // Parse text segments from the region
        var segments = TextSegmentParser.ParseWithVisibility(
            RegionPtr,
            forceWhiteText: true,
            forceOutline: true,
            outlineWidth: 3);

        if (segments.Count == 0 || segments.TrueForAll(s => s.IsEmpty))
            return 0;

        // Get the combined text for translation
        string originalText = TextSegmentParser.GetCombinedText(segments);
        if (string.IsNullOrWhiteSpace(originalText))
            return 0;

        // Translate (with caching)
        string translatedText;
        try
        {
            translatedText = _cache!.GetOrTranslate(originalText, _translator!);
        }
        catch (Exception ex)
        {
            // Fallback: render original text if translation fails
            if (_renderCount <= 5)
                Context.Logger.Warning($"[SubtitleTranslator] Translation failed: {ex.Message}");
            translatedText = originalText;
        }

        if (_renderCount <= 5)
        {
            Context.Logger.Info($"[SubtitleTranslator] #{_renderCount}: \"{originalText}\" -> \"{translatedText}\"");
        }

        // Get video dimensions from filter's format
        ref VLCFilter filter = ref Unsafe.AsRef<VLCFilter>((void*)Context.NativePtr);
        uint videoWidth = filter.FormatOut.Video.Width > 0 ? filter.FormatOut.Video.Width : (uint)DefaultWidth;
        int viewportWidth = Math.Max((int)videoWidth, 320);
        int maximumRegionWidth = request.MaxWidth > 0
            ? Math.Min(request.MaxWidth, viewportWidth)
            : (int)(viewportWidth * 0.9f);

        // VLC positions this tightly-sized image using the source alignment.
        if (_canvas == null)
            _canvas = new TextCanvas(1, 1);

        // Use the style from the first segment for the translated text
        var style = segments[0].Style;
        TextAlignment alignment = request.HorizontalAlignment;

        Image<Rgba32>? image = _canvas.RenderTextRegion(
            translatedText,
            style,
            maximumRegionWidth,
            alignment);
        if (image == null)
            return 0;

        // Convert to a compact VLC region while preserving source positioning.
        return PictureConverter.ToSubpictureRegion(image, ChromaListPtr, request.Alignment);
    }

    /// <summary>
    /// Try to find the ONNX model directory.
    /// Priority: typed VLC option > relative to DLL > relative to CWD.
    /// </summary>
    private string? ResolveModelPath()
    {
        // 1. Typed VLC configuration option.
        var configuredPath = Config.TranslatorModelPath;
        if (!string.IsNullOrWhiteSpace(configuredPath) && Directory.Exists(configuredPath))
            return Path.GetFullPath(configuredPath);

        // 2. Relative to app base directory
        var baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDir))
        {
            var relPath = Path.Combine(baseDir, "models");
            if (Directory.Exists(relPath))
                return relPath;
        }

        // 3. Relative to current working directory
        var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), "models");
        if (Directory.Exists(cwdPath))
            return cwdPath;

        return null;
    }
}
