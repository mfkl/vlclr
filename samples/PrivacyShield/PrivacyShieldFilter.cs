using System.Diagnostics;
using VLCLR.Native;
using VLCLR.ObjectDetection;
using VLCLR.Plugin;
using YoloObjectSearch;

namespace PrivacyShield;

[VLCModule("dotnet_privacy_shield")]
[VLCCapability("video filter", Score = 0)]
[VLCDescription(".NET Native AOT GPU Privacy Shield")]
[VLCConfig(
    "dotnet-privacy-shield-model",
    VLCConfigType.String,
    Default = "yolox_nano.onnx",
    Description = "YOLOX ONNX model",
    LongDescription = "Path to a YOLOX-Nano or YOLOX-Tiny 416x416 ONNX model")]
[VLCConfig(
    "dotnet-privacy-shield-face-model",
    VLCConfigType.String,
    Default = "",
    Description = "Face detection model",
    LongDescription = "Path to the Open Model Zoo face-detection-retail-0004 XML model")]
[VLCConfig(
    "dotnet-privacy-shield-license-plate-model",
    VLCConfigType.String,
    Default = "",
    Description = "License plate detection model",
    LongDescription = "Path to the Open Model Zoo vehicle-license-plate-detection-barrier-0106 XML model")]
[VLCConfig(
    "dotnet-privacy-shield-runtime-dir",
    VLCConfigType.String,
    Default = "",
    Description = "OpenVINO runtime directory",
    LongDescription = "Directory containing openvino_c.dll and openvino.dll")]
[VLCConfig(
    "dotnet-privacy-shield-classes",
    VLCConfigType.String,
    Default = "person",
    Description = "Classes to redact",
    LongDescription = "Comma-separated COCO-80 labels, face, license plate, all, or *")]
[VLCConfig(
    "dotnet-privacy-shield-confidence",
    VLCConfigType.Float,
    Default = 0.30f,
    Min = 0.0f,
    Max = 1.0f,
    Description = "Detection confidence",
    LongDescription = "Minimum objectness multiplied by class confidence")]
[VLCConfig(
    "dotnet-privacy-shield-rate",
    VLCConfigType.Float,
    Default = 15.0f,
    Min = 1.0f,
    Max = 60.0f,
    Description = "Inference rate",
    LongDescription = "Maximum GPU inference submissions per second")]
[VLCConfig(
    "dotnet-privacy-shield-mode",
    VLCConfigType.String,
    Default = "solid",
    Description = "Redaction effect",
    LongDescription = "Redaction effect: solid, mosaic, or blur")]
[VLCConfig(
    "dotnet-privacy-shield-blur-radius",
    VLCConfigType.Integer,
    Default = 32,
    Min = 4,
    Max = 128,
    Description = "Blur radius",
    LongDescription = "Approximate source-pixel radius for GPU blur")]
[VLCConfig(
    "dotnet-privacy-shield-pixel-size",
    VLCConfigType.Integer,
    Default = 24,
    Min = 4,
    Max = 128,
    Description = "Mosaic pixel size",
    LongDescription = "Approximate source-pixel block size for GPU mosaic")]
[VLCConfig(
    "dotnet-privacy-shield-padding",
    VLCConfigType.Integer,
    Default = 12,
    Min = 0,
    Max = 200,
    Description = "Redaction padding",
    LongDescription = "Pixels added around each detected object")]
[VLCConfig(
    "dotnet-privacy-shield-ttl-ms",
    VLCConfigType.Integer,
    Default = 250,
    Min = 50,
    Max = 2000,
    Description = "Redaction lifetime",
    LongDescription = "Hide detections older than this many media-time milliseconds")]
[VLCConfig(
    "dotnet-privacy-shield-hold-ms",
    VLCConfigType.Integer,
    Default = 500,
    Min = 0,
    Max = 5000,
    Description = "Missed-detection hold time",
    LongDescription = "Keep unmatched redactions for this many media-time milliseconds")]
public partial class PrivacyShieldFilter : VLCVideoFilterBase
{
    private const int RedactionsPerDetector = 32;
    private const int MaximumDetectorCount = 3;

    private sealed class DetectorState : IDisposable
    {
        public DetectorState(
            ObjectDetectionModelProfile profile,
            GpuObjectDetector detector,
            DetectionPersistenceOptions persistenceOptions)
        {
            Profile = profile;
            Detector = detector;
            PersistenceTracker = new DetectionPersistenceTracker(
                RedactionsPerDetector,
                persistenceOptions);
        }

        public ObjectDetectionModelProfile Profile { get; }

        public GpuObjectDetector Detector { get; }

        public DetectionPersistenceTracker PersistenceTracker { get; }

        public ObjectDetection[] Candidates { get; } =
            new ObjectDetection[128];

        public ObjectDetection[] Observed { get; } =
            new ObjectDetection[RedactionsPerDetector];

        public ObjectDetection[] Selected { get; } =
            new ObjectDetection[RedactionsPerDetector];

        public long ProcessedGeneration { get; set; } = -1;

        public long PresentedRevision { get; set; } = -1;

        public long LastFailureLogTimestamp { get; set; }

        public void Dispose() => Detector.Dispose();
    }

    private readonly List<ObjectDetectionModelProfile> _modelProfiles = [];
    private readonly List<DetectorState> _detectors = [];
    private readonly ObjectDetection[] _redactions =
        new ObjectDetection[
            RedactionsPerDetector * MaximumDetectorCount];
    private readonly MediaTimestampGate _mediaTimestampGate = new();

    private D3D11DetectionOverlay? _overlay;
    private D3D11OutputPictureAllocator? _outputPictures;
    private DetectionOverlaySelector _overlaySelector = new();
    private DetectionPersistenceOptions _persistenceOptions =
        DetectionPersistenceOptions.Default;
    private PrivacyClassSelection? _classSelection;
    private float _confidence = 0.30f;
    private float _targetRate = 15.0f;
    private RedactionEffectMode _redactionMode =
        RedactionEffectMode.Solid;
    private int _blurRadiusPixels = 32;
    private int _mosaicPixelSize = 24;
    private int _paddingPixels = 12;
    private bool _capabilitiesReported;
    private long _selectedGeneration = -1;
    private int _selectedSourceWidth;
    private int _selectedSourceHeight;
    private int _selectedCount;
    private long _lastActivityLogTimestamp;

    protected override bool OnOpen(VLCFilterContext context)
    {
        if (!VLCFourCC.IsD3D11Opaque(context.Chroma))
        {
            context.Logger.Error(
                "[PrivacyShield] Startup check failed (chroma): expected " +
                "a VLC D3D11 opaque hardware frame, but received " +
                $"{context.ChromaString}. Enable D3D11 hardware decoding; " +
                "the sample has no CPU fallback.");
            return false;
        }

        string? runtimeDirectory = Config.RuntimeDir?.Trim();
        if (string.IsNullOrWhiteSpace(runtimeDirectory))
        {
            runtimeDirectory = Environment.GetEnvironmentVariable(
                "OPENVINO_RUNTIME_DIR");
        }

        OpenVinoRuntimeInspection runtimeInspection =
            OpenVinoRuntimePrerequisites.Inspect(runtimeDirectory);
        if (!runtimeInspection.IsUsable)
        {
            foreach (string problem in runtimeInspection.Problems)
            {
                context.Logger.Error(
                    "[PrivacyShield] Startup check failed " +
                    $"(OpenVINO runtime): {problem}");
            }
            return false;
        }

        Environment.SetEnvironmentVariable(
            "OPENVINO_RUNTIME_DIR",
            runtimeInspection.RuntimeDirectory);
        context.Logger.Info(
            "[PrivacyShield] Startup check passed (OpenVINO runtime): " +
            $"version={runtimeInspection.RuntimeVersion}, " +
            $"directory={runtimeInspection.RuntimeDirectory}, " +
            $"TBB={runtimeInspection.TbbPath}.");

        if (!PrivacyClassSelection.TryParse(
                Config.Classes,
                PrivacyObjectCatalog.Create(),
                out _classSelection,
                out string? selectionError))
        {
            context.Logger.Error(
                "[PrivacyShield] Startup check failed (classes): " +
                selectionError);
            return false;
        }
        if (!RedactionEffectModeParser.TryParse(
                Config.Mode,
                out _redactionMode))
        {
            context.Logger.Error(
                "[PrivacyShield] Startup check failed (mode): expected " +
                "solid, mosaic, or blur; received " +
                $"'{Config.Mode}'.");
            return false;
        }

        float configuredConfidence = Config.Confidence;
        _confidence = configuredConfidence <= 0
            ? 0.30f
            : Math.Clamp(configuredConfidence, 0.01f, 1.0f);
        float configuredRate = Config.Rate;
        _targetRate = configuredRate <= 0
            ? 15.0f
            : Math.Clamp(configuredRate, 1.0f, 60.0f);
        _paddingPixels = checked((int)Math.Clamp(
            Config.Padding,
            0L,
            200L));
        _blurRadiusPixels = checked((int)Math.Clamp(
            Config.BlurRadius,
            4L,
            128L));
        _mosaicPixelSize = checked((int)Math.Clamp(
            Config.PixelSize,
            4L,
            128L));
        int ttlMilliseconds = checked((int)Math.Clamp(
            Config.TtlMs,
            50L,
            2000L));
        int holdMilliseconds = checked((int)Math.Clamp(
            Config.HoldMs,
            0L,
            5000L));
        _overlaySelector = new DetectionOverlaySelector(
            new DetectionOverlayOptions(
                TimeSpan.FromMilliseconds(ttlMilliseconds),
                TimeSpan.FromMilliseconds(50)));
        _persistenceOptions = new DetectionPersistenceOptions(
            TimeSpan.FromMilliseconds(holdMilliseconds),
            0.20f);
        if (!ConfigureModelProfiles(context))
        {
            return false;
        }

        context.Logger.Info(
            "[PrivacyShield] GPU redaction mode, " +
            $"classes={_classSelection!.Description}, " +
            $"mode={_redactionMode.ToString().ToLowerInvariant()}, " +
            $"detectors={string.Join(", ", _modelProfiles.Select(
                profile => profile.Name))}, rate={_targetRate:F1} Hz, " +
            $"confidence={_confidence:F2}, padding={_paddingPixels}px, " +
            $"blur-radius={_blurRadiusPixels}px, " +
            $"pixel-size={_mosaicPixelSize}px, " +
            $"ttl={ttlMilliseconds} ms, hold={holdMilliseconds} ms.");
        if (!context.PassThroughVideoContext())
        {
            context.Logger.Error(
                "[PrivacyShield] Startup check failed (VLC video context): " +
                "the D3D11 input has no hardware video context to " +
                "propagate to the filter output.");
            return false;
        }
        return true;
    }

    protected override void OnFirstFrame(VLCFrame frame)
    {
        if (_detectors.Count > 0 || _overlay is not null)
        {
            return;
        }

        if (!frame.TryGetD3D11Surface(out VLCD3D11Surface surface))
        {
            Context.Logger.Error(
                "[PrivacyShield] Startup check failed (D3D11 surface): " +
                "VLC negotiated D3D11 opaque chroma but did not provide " +
                "an ID3D11Texture2D.");
            return;
        }

        if (!_capabilitiesReported)
        {
            _capabilitiesReported = true;
            try
            {
                D3D11CapabilitySnapshot capabilities =
                    D3D11CapabilityDiagnostics.Inspect(surface.Texture);
                Context.Logger.Info(
                    "[PrivacyShield] Startup D3D11 capabilities: " +
                    capabilities.Format(
                        surface.ArraySlice,
                        frame.Width,
                        frame.Height) +
                    ". Required: D3D11 video processing, NV12 output, " +
                    "and an Intel GPU usable by OpenVINO.");
            }
            catch (Exception exception)
            {
                Context.Logger.Warning(
                    "[PrivacyShield] Could not inspect the D3D11 adapter: " +
                    exception.Message);
            }
        }

        try
        {
            foreach (ObjectDetectionModelProfile profile in _modelProfiles)
            {
                var detector = new GpuObjectDetector(
                    surface.Texture,
                    frame.Width,
                    frame.Height,
                    profile,
                    _targetRate);
                _detectors.Add(new DetectorState(
                    profile,
                    detector,
                    _persistenceOptions));
            }
            D3D11DetectionOverlayOptions overlayOptions =
                _redactionMode switch
                {
                    RedactionEffectMode.Mosaic =>
                        D3D11DetectionOverlayOptions.MosaicRedaction(
                            _paddingPixels,
                            _mosaicPixelSize),
                    RedactionEffectMode.Blur =>
                        D3D11DetectionOverlayOptions.BlurRedaction(
                            _paddingPixels,
                            _blurRadiusPixels),
                    _ =>
                        D3D11DetectionOverlayOptions.SolidBlackRedaction(
                            _paddingPixels)
                };
            _overlay = new D3D11DetectionOverlay(
                surface.Texture,
                frame.Width,
                frame.Height,
                overlayOptions);
            _outputPictures = new D3D11OutputPictureAllocator(
                surface.Texture,
                Context);
            Context.Logger.Info(
                $"[PrivacyShield] {_detectors.Count} GPU detector(s) and " +
                $"{_redactionMode.ToString()
                    .ToLowerInvariant()} D3D11 redaction compositor ready; " +
                "decoder surfaces remain read-only.");
        }
        catch (Exception exception)
        {
            foreach (DetectorState detector in _detectors)
            {
                detector.Dispose();
            }
            _detectors.Clear();
            _overlay?.Dispose();
            _overlay = null;
            _outputPictures?.Dispose();
            _outputPictures = null;
            Context.Logger.Error(
                "[PrivacyShield] Startup check failed (GPU pipeline): " +
                "redaction requires an NV12 decoder surface and a D3D11 " +
                "video processor with two streams, BGRA input, and alpha " +
                $"blending. {exception.Message}");
        }
    }

    protected override nint ProcessFrameToOutput(VLCFrame frame)
    {
        bool mediaTimeAdvanced =
            _mediaTimestampGate.TryAdvance(frame.Date);
        if (_detectors.Count == 0)
        {
            return frame.NativePtr;
        }

        bool hasSurface =
            frame.TryGetD3D11Surface(out VLCD3D11Surface surface);
        if (mediaTimeAdvanced && hasSurface)
        {
            foreach (DetectorState detector in _detectors)
            {
                detector.Detector.TrySubmit(
                    surface,
                    frame.Width,
                    frame.Height,
                    frame.Date);
            }
        }

        if (mediaTimeAdvanced)
        {
            UpdateSelectedRedactions(
                frame.Date,
                frame.Width,
                frame.Height);
            ReportActivity();
        }

        if (_overlay is not null &&
            hasSurface &&
            _selectedCount > 0)
        {
            nint outputPicture = 0;
            try
            {
                outputPicture = _outputPictures!.RentPicture();
                var outputFrame = new VLCFrame(
                    outputPicture,
                    Context);
                if (!outputFrame.TryGetD3D11Surface(
                        out VLCD3D11Surface outputSurface))
                {
                    throw new InvalidOperationException(
                        "VLC allocated an output picture without a " +
                        "D3D11 texture.");
                }

                _overlay.Render(
                    surface,
                    outputSurface,
                    _selectedGeneration,
                    _selectedSourceWidth,
                    _selectedSourceHeight,
                    _redactions.AsSpan(0, _selectedCount));
                ReportFailures();
                return outputPicture;
            }
            catch (Exception exception)
            {
                if (outputPicture != 0)
                {
                    VLCCore.PictureRelease(outputPicture);
                }
                Context.Logger.Error(
                    "[PrivacyShield] GPU redaction disabled: " +
                    exception.Message);
                _overlay.Dispose();
                _overlay = null;
            }
        }

        ReportFailures();
        return frame.NativePtr;
    }

    private void UpdateSelectedRedactions(
        long frameDate,
        int sourceWidth,
        int sourceHeight)
    {
        if (!TryGetMediaTime(
                frameDate,
                out TimeSpan currentMediaTime))
        {
            _selectedCount = 0;
            return;
        }

        int selectedCount = 0;
        bool visualChange = false;
        PrivacyClassSelection selection = _classSelection!;
        foreach (DetectorState detector in _detectors)
        {
            DetectionBatch? latest = detector.Detector.Latest;
            if (latest is not null &&
                latest.Generation != detector.ProcessedGeneration)
            {
                detector.ProcessedGeneration = latest.Generation;
                int candidateCount = _overlaySelector.Select(
                    latest,
                    currentMediaTime,
                    null,
                    detector.Candidates);
                int observedCount = 0;
                for (int index = 0;
                     index < candidateCount &&
                     observedCount < detector.Observed.Length;
                     index++)
                {
                    ObjectDetection detection = detector.Candidates[index];
                    if (selection.Contains(detection.ClassId))
                    {
                        detector.Observed[observedCount++] = detection;
                    }
                }

                detector.PersistenceTracker.Observe(
                    latest.Generation,
                    latest.MediaTime,
                    detector.Observed.AsSpan(0, observedCount));
            }

            int detectorCount = detector.PersistenceTracker.Snapshot(
                currentMediaTime,
                detector.Selected);
            if (detector.PresentedRevision !=
                detector.PersistenceTracker.Revision)
            {
                detector.PresentedRevision =
                    detector.PersistenceTracker.Revision;
                visualChange = true;
            }

            int copyCount = Math.Min(
                detectorCount,
                _redactions.Length - selectedCount);
            detector.Selected.AsSpan(0, copyCount).CopyTo(
                _redactions.AsSpan(selectedCount));
            selectedCount += copyCount;
        }

        _selectedCount = selectedCount;
        _selectedSourceWidth = sourceWidth;
        _selectedSourceHeight = sourceHeight;
        if (visualChange)
        {
            _selectedGeneration++;
        }
    }

    private void ReportActivity()
    {
        DetectionBatch? newest = null;
        foreach (DetectorState detector in _detectors)
        {
            DetectionBatch? latest = detector.Detector.Latest;
            if (latest is not null &&
                (newest is null || latest.MediaTime > newest.MediaTime))
            {
                newest = latest;
            }
        }
        if (newest is null)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(
                _lastActivityLogTimestamp,
                now) < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _lastActivityLogTimestamp = now;
        string inferenceSummary = string.Join(
            ", ",
            _detectors.Select(detector =>
            {
                DetectionBatch? latest = detector.Detector.Latest;
                return latest is null
                    ? $"{detector.Profile.Name}=warming"
                    : $"{detector.Profile.Name}=" +
                      $"{latest.InferenceDuration.TotalMilliseconds:F1} ms";
            }));
        Context.Logger.Info(
            $"[PrivacyShield] Redacting {_selectedCount} object(s) at " +
            $"{newest.MediaTime}; GPU inference {inferenceSummary}.");
    }

    private void ReportFailures()
    {
        long now = Stopwatch.GetTimestamp();
        foreach (DetectorState detector in _detectors)
        {
            string? failure = detector.Detector.Failure;
            if (failure is not null &&
                Stopwatch.GetElapsedTime(
                    detector.LastFailureLogTimestamp,
                    now) >= TimeSpan.FromSeconds(2))
            {
                detector.LastFailureLogTimestamp = now;
                Context.Logger.Error(
                    $"[PrivacyShield] {detector.Profile.Name}: {failure}");
            }
        }
    }

    protected override void OnFlush()
    {
        _mediaTimestampGate.Reset();
        _selectedCount = 0;
        _selectedGeneration = -1;
        foreach (DetectorState detector in _detectors)
        {
            detector.ProcessedGeneration = -1;
            detector.PresentedRevision = -1;
            detector.PersistenceTracker.Reset();
            detector.Detector.ResetTimeline();
        }
        _overlay?.Reset();
    }

    protected override void OnClose()
    {
        D3D11DetectionOverlay? overlay = _overlay;
        if (overlay is not null)
        {
            Context.Logger.Info(
                "[PrivacyShield] GPU redaction summary: " +
                $"rendered={overlay.RenderedFrames}, " +
                $"uploads={overlay.UploadedBatches}, " +
                $"regions={overlay.UploadedBoxes}.");
        }
        overlay?.Dispose();
        _overlay = null;

        D3D11OutputPictureAllocator? outputPictures = _outputPictures;
        if (outputPictures is not null)
        {
            Context.Logger.Info(
                "[PrivacyShield] D3D11 output picture summary: " +
                $"created={outputPictures.CreatedSurfaces}, " +
                $"reused={outputPictures.ReusedSurfaces}.");
        }
        outputPictures?.Dispose();
        _outputPictures = null;

        foreach (DetectorState detector in _detectors)
        {
            detector.Dispose();
            DetectorStatistics statistics = detector.Detector.Statistics;
            Context.Logger.Info(
                $"[PrivacyShield] {detector.Profile.Name} GPU summary: " +
                $"submitted={statistics.Submitted}, " +
                $"inferred={statistics.Inferred}, " +
                $"warmup-skipped={statistics.WarmupSkipped}, " +
                $"rate-skipped={statistics.RateSkipped}, " +
                $"busy-skipped={statistics.BusySkipped}, " +
                $"blit avg/max={statistics.AverageBlitMilliseconds:F3}/" +
                $"{statistics.MaximumBlitMilliseconds:F3} ms, " +
                $"inference avg/max=" +
                $"{statistics.AverageInferenceMilliseconds:F3}/" +
                $"{statistics.MaximumInferenceMilliseconds:F3} ms.");
        }
        _detectors.Clear();
        Context.ReleaseOutputVideoContext();
    }

    private bool ConfigureModelProfiles(VLCFilterContext context)
    {
        _modelProfiles.Clear();
        PrivacyClassSelection selection = _classSelection!;
        if (selection.IncludesAll || selection.HasExplicitCocoClass)
        {
            if (!TryResolveModelPath(
                    context,
                    Config.Model,
                    "yolox_nano.onnx",
                    "--dotnet-privacy-shield-model",
                    "COCO-80 YOLOX",
                    out string modelPath))
            {
                return false;
            }

            var decoder = new YoloXOutputDecoder(
                Coco80ObjectCatalog.Create(),
                new YoloXDecoderOptions(
                    ConfidenceThreshold: _confidence));
            _modelProfiles.Add(new ObjectDetectionModelProfile(
                "COCO-80 YOLOX",
                modelPath,
                ObjectDetectionModelInputLayout.Nchw,
                decoder));
        }

        bool faceConfigured =
            !string.IsNullOrWhiteSpace(Config.FaceModel);
        if (selection.HasExplicitClass(
                PrivacyObjectCatalog.FaceClassId) ||
            (selection.IncludesAll && faceConfigured))
        {
            if (!TryResolveModelPath(
                    context,
                    Config.FaceModel,
                    null,
                    "--dotnet-privacy-shield-face-model",
                    "face",
                    out string faceModelPath))
            {
                return false;
            }

            var decoder = new SsdDetectionOutputDecoder(
                [
                    new SsdDetectionClassMapping(
                        1,
                        PrivacyObjectCatalog.Face)
                ],
                new SsdDetectionOutputDecoderOptions(
                    ConfidenceThreshold: _confidence));
            _modelProfiles.Add(new ObjectDetectionModelProfile(
                "face",
                faceModelPath,
                ObjectDetectionModelInputLayout.Nchw,
                decoder));
        }

        bool licensePlateConfigured =
            !string.IsNullOrWhiteSpace(Config.LicensePlateModel);
        if (selection.HasExplicitClass(
                PrivacyObjectCatalog.LicensePlateClassId) ||
            (selection.IncludesAll && licensePlateConfigured))
        {
            if (!TryResolveModelPath(
                    context,
                    Config.LicensePlateModel,
                    null,
                    "--dotnet-privacy-shield-license-plate-model",
                    "license plate",
                    out string licensePlateModelPath))
            {
                return false;
            }

            var decoder = new SsdDetectionOutputDecoder(
                [
                    new SsdDetectionClassMapping(
                        2,
                        PrivacyObjectCatalog.LicensePlate)
                ],
                new SsdDetectionOutputDecoderOptions(
                    ConfidenceThreshold: _confidence));
            _modelProfiles.Add(new ObjectDetectionModelProfile(
                "license plate",
                licensePlateModelPath,
                ObjectDetectionModelInputLayout.Nhwc,
                decoder));
        }

        if (_modelProfiles.Count > 0)
        {
            return true;
        }

        context.Logger.Error(
            "[PrivacyShield] Startup check failed (models): no detector " +
            "was configured for the selected classes.");
        return false;
    }

    private static bool TryResolveModelPath(
        VLCFilterContext context,
        string? configuredPath,
        string? fallbackPath,
        string optionName,
        string detectorName,
        out string modelPath)
    {
        string? path = string.IsNullOrWhiteSpace(configuredPath)
            ? fallbackPath
            : configuredPath.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            context.Logger.Error(
                $"[PrivacyShield] Startup check failed ({detectorName} " +
                $"model): set {optionName}=<path> when selecting " +
                $"{detectorName}.");
            modelPath = "";
            return false;
        }

        try
        {
            modelPath = Path.GetFullPath(path);
        }
        catch (Exception exception)
        {
            context.Logger.Error(
                $"[PrivacyShield] Startup check failed ({detectorName} " +
                $"model): invalid path: {exception.Message}");
            modelPath = "";
            return false;
        }

        if (!File.Exists(modelPath))
        {
            context.Logger.Error(
                $"[PrivacyShield] Startup check failed ({detectorName} " +
                $"model): file not found: {modelPath}. Set " +
                $"{optionName}=<path>.");
            return false;
        }

        long modelBytes = new FileInfo(modelPath).Length;
        if (Path.GetExtension(modelPath).Equals(
                ".xml",
                StringComparison.OrdinalIgnoreCase))
        {
            string weightsPath = Path.ChangeExtension(modelPath, ".bin");
            if (!File.Exists(weightsPath))
            {
                context.Logger.Error(
                    $"[PrivacyShield] Startup check failed ({detectorName} " +
                    $"model): companion weights file not found: " +
                    $"{weightsPath}.");
                return false;
            }
            modelBytes += new FileInfo(weightsPath).Length;
        }

        context.Logger.Info(
            $"[PrivacyShield] Startup check passed ({detectorName} model): " +
            $"{modelPath} ({modelBytes:N0} bytes).");
        return true;
    }

    private static bool TryGetMediaTime(
        long vlcTicks,
        out TimeSpan mediaTime)
    {
        if (vlcTicks < 0 || vlcTicks > long.MaxValue / 10)
        {
            mediaTime = default;
            return false;
        }

        mediaTime = TimeSpan.FromTicks(vlcTicks * 10);
        return true;
    }
}
