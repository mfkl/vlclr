using System.Diagnostics;
using VLCLR.Native;
using VLCLR.ObjectDetection;
using VLCLR.Plugin;

namespace YoloObjectSearch;

[VLCModule("dotnet_yolo_search")]
[VLCCapability("video filter", Score = 0)]
[VLCDescription(".NET Native AOT GPU YOLO Object Search")]
[VLCConfig(
    "dotnet-yolo-search-model",
    VLCConfigType.String,
    Default = "yolox_nano.onnx",
    Description = "YOLOX ONNX model",
    LongDescription = "Path to a YOLOX-Nano or YOLOX-Tiny 416x416 ONNX model")]
[VLCConfig(
    "dotnet-yolo-search-runtime-dir",
    VLCConfigType.String,
    Default = "",
    Description = "OpenVINO runtime directory",
    LongDescription = "Directory containing openvino_c.dll and openvino.dll")]
[VLCConfig(
    "dotnet-yolo-search-query",
    VLCConfigType.String,
    Default = "",
    Description = "Object search query",
    LongDescription = "Empty labels every object; for example: show me the ball")]
[VLCConfig(
    "dotnet-yolo-search-confidence",
    VLCConfigType.Float,
    Default = 0.30f,
    Min = 0.0f,
    Max = 1.0f,
    Description = "Detection confidence",
    LongDescription = "Minimum objectness multiplied by class confidence")]
[VLCConfig(
    "dotnet-yolo-search-rate",
    VLCConfigType.Float,
    Default = 15.0f,
    Min = 1.0f,
    Max = 60.0f,
    Description = "Inference rate",
    LongDescription = "Maximum GPU inference submissions per second")]
[VLCConfig(
    "dotnet-yolo-search-overlay-enabled",
    VLCConfigType.Bool,
    Default = true,
    Description = "Draw detection boxes",
    LongDescription = "GPU-compose fresh detection boxes onto NV12 video")]
[VLCConfig(
    "dotnet-yolo-search-overlay-ttl-ms",
    VLCConfigType.Integer,
    Default = 250,
    Min = 50,
    Max = 2000,
    Description = "Detection box lifetime",
    LongDescription = "Hide boxes older than this many media-time milliseconds")]
public partial class YoloObjectSearchFilter : VLCVideoFilterBase
{
    private readonly ObjectDetection[] _overlayDetections =
        new ObjectDetection[12];
    private readonly MediaTimestampGate _mediaTimestampGate = new();

    private GpuYoloXDetector? _detector;
    private D3D11DetectionOverlay? _overlay;
    private D3D11OutputPictureAllocator? _outputPictures;
    private DetectionOverlaySelector _overlaySelector = new();
    private DetectionQuery? _query;
    private string _modelPath = "";
    private float _confidence = 0.30f;
    private float _targetRate = 15.0f;
    private bool _overlayEnabled = true;
    private bool _capabilitiesReported;
    private long _reportedGeneration = -1;
    private long _selectedOverlayGeneration = -1;
    private int _selectedOverlaySourceWidth;
    private int _selectedOverlaySourceHeight;
    private int _selectedOverlayCount;
    private long _lastStatusLogTimestamp;

    protected override bool OnOpen(VLCFilterContext context)
    {
        if (!VLCFourCC.IsD3D11Opaque(context.Chroma))
        {
            context.Logger.Error(
                "[YoloSearch] Startup check failed (chroma): expected a " +
                "VLC D3D11 opaque hardware frame, but received " +
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
                    "[YoloSearch] Startup check failed " +
                    $"(OpenVINO runtime): {problem}");
            }
            return false;
        }

        Environment.SetEnvironmentVariable(
            "OPENVINO_RUNTIME_DIR",
            runtimeInspection.RuntimeDirectory);
        context.Logger.Info(
            "[YoloSearch] Startup check passed (OpenVINO runtime): " +
            $"version={runtimeInspection.RuntimeVersion}, " +
            $"directory={runtimeInspection.RuntimeDirectory}, " +
            $"TBB={runtimeInspection.TbbPath}.");

        try
        {
            _modelPath = Path.GetFullPath(
                Config.Model ?? "yolox_nano.onnx");
        }
        catch (Exception exception)
        {
            context.Logger.Error(
                "[YoloSearch] Startup check failed (model): " +
                $"invalid path: {exception.Message}");
            return false;
        }
        if (!File.Exists(_modelPath))
        {
            context.Logger.Error(
                "[YoloSearch] Startup check failed (model): " +
                $"file not found: {_modelPath}. Set " +
                "--dotnet-yolo-search-model=<path>.");
            return false;
        }
        context.Logger.Info(
            "[YoloSearch] Startup check passed (model): " +
            $"{_modelPath} ({new FileInfo(_modelPath).Length:N0} bytes).");

        float configuredConfidence = Config.Confidence;
        _confidence = configuredConfidence <= 0
            ? 0.30f
            : Math.Clamp(configuredConfidence, 0.01f, 1.0f);
        float configuredRate = Config.Rate;
        _targetRate = configuredRate <= 0
            ? 15.0f
            : Math.Clamp(configuredRate, 1.0f, 60.0f);
        _overlayEnabled = Config.OverlayEnabled;
        int overlayTtlMilliseconds = checked((int)Math.Clamp(
            Config.OverlayTtlMs,
            50L,
            2000L));
        _overlaySelector = new DetectionOverlaySelector(
            new DetectionOverlayOptions(
                TimeSpan.FromMilliseconds(overlayTtlMilliseconds),
                TimeSpan.FromMilliseconds(50)));

        string queryText = Config.Query?.Trim() ?? "";
        if (queryText.Length > 0)
        {
            var parser = new DetectionQueryParser(
                Coco80ObjectCatalog.Create());
            if (!parser.TryParse(
                    queryText,
                    out DetectionQuery? parsedQuery,
                    _confidence) ||
                parsedQuery is null)
            {
                context.Logger.Error(
                    $"[YoloSearch] Unknown v1 object query: '{queryText}'.");
                return false;
            }

            _query = parsedQuery;
            _confidence = parsedQuery.MinimumConfidence;
            context.Logger.Info(
                $"[YoloSearch] Search target: " +
                $"{_query.ObjectClass.Label} " +
                $"(class {_query.ObjectClass.Id}).");
        }
        else
        {
            context.Logger.Info(
                "[YoloSearch] Automatic labeling mode: all COCO-80 objects.");
        }

        context.Logger.Info(
            $"[YoloSearch] Pure C# GPU mode, model={_modelPath}, " +
            $"rate={_targetRate:F1} Hz, confidence={_confidence:F2}, " +
            $"overlay={_overlayEnabled}, " +
            $"overlay-ttl={overlayTtlMilliseconds} ms.");
        if (!context.PassThroughVideoContext())
        {
            context.Logger.Error(
                "[YoloSearch] Startup check failed (VLC video context): " +
                "the D3D11 input has no hardware video context to " +
                "propagate to the filter output.");
            return false;
        }
        return true;
    }

    protected override void OnFirstFrame(VLCFrame frame)
    {
        if (!frame.TryGetD3D11Surface(out VLCD3D11Surface surface))
        {
            Context.Logger.Error(
                "[YoloSearch] Startup check failed (D3D11 surface): " +
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
                    D3D11CapabilityDiagnostics.Inspect(
                        surface.Texture);
                Context.Logger.Info(
                    "[YoloSearch] Startup D3D11 capabilities: " +
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
                    "[YoloSearch] Could not inspect the D3D11 adapter: " +
                    exception.Message);
            }
        }

        if (_detector is null)
        {
            try
            {
                _detector = new GpuYoloXDetector(
                    surface.Texture,
                    frame.Width,
                    frame.Height,
                    _modelPath,
                    _confidence,
                    _targetRate);
                Context.Logger.Info(
                    "[YoloSearch] GPU worker started; OpenVINO is compiling " +
                    "the model in the background.");
            }
            catch (Exception exception)
            {
                Context.Logger.Error(
                    "[YoloSearch] Startup check failed (D3D11 video " +
                    "processor): the decoder adapter must support reading " +
                    "the source texture and producing NV12. " +
                    exception.Message);
            }
        }

        if (_overlayEnabled && _overlay is null)
        {
            try
            {
                _overlay = new D3D11DetectionOverlay(
                    surface.Texture,
                    frame.Width,
                    frame.Height);
                _outputPictures =
                    new D3D11OutputPictureAllocator(
                        surface.Texture,
                        Context);
                Context.Logger.Info(
                    "[YoloSearch] D3D11 NV12 box/label overlay ready; " +
                    "decoder surfaces remain read-only.");
            }
            catch (Exception exception)
            {
                _overlay?.Dispose();
                _overlay = null;
                _outputPictures?.Dispose();
                _outputPictures = null;
                _overlayEnabled = false;
                Context.Logger.Error(
                    "[YoloSearch] Startup check failed (GPU overlay): " +
                    "drawing requires an NV12 decoder surface and a D3D11 " +
                    "video processor with two streams, BGRA input, and " +
                    $"alpha blending. {exception.Message}");
            }
        }
    }

    protected override nint ProcessFrameToOutput(VLCFrame frame)
    {
        bool mediaTimeAdvanced =
            _mediaTimestampGate.TryAdvance(frame.Date);

        GpuYoloXDetector? detector = _detector;
        if (detector is null)
        {
            return frame.NativePtr;
        }

        bool hasSurface =
            frame.TryGetD3D11Surface(out VLCD3D11Surface surface);
        if (mediaTimeAdvanced && hasSurface)
        {
            detector.TrySubmit(
                surface,
                frame.Width,
                frame.Height,
                frame.Date);
        }

        DetectionBatch? latest = detector.Latest;
        if (latest is not null &&
            latest.Generation != _reportedGeneration)
        {
            _reportedGeneration = latest.Generation;
            Report(latest);
        }

        if (mediaTimeAdvanced)
        {
            UpdateSelectedOverlay(latest, frame.Date);
        }

        if (_overlay is not null &&
            hasSurface &&
            _selectedOverlayCount > 0)
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
                    _selectedOverlayGeneration,
                    _selectedOverlaySourceWidth,
                    _selectedOverlaySourceHeight,
                    _overlayDetections.AsSpan(
                        0,
                        _selectedOverlayCount));
                ReportFailure(detector);
                return outputPicture;
            }
            catch (Exception exception)
            {
                if (outputPicture != 0)
                {
                    VLCCore.PictureRelease(outputPicture);
                }
                Context.Logger.Error(
                    $"[YoloSearch] GPU overlay disabled: " +
                    $"{exception.Message}");
                _overlay.Dispose();
                _overlay = null;
                _overlayEnabled = false;
            }
        }

        ReportFailure(detector);
        return frame.NativePtr;
    }

    private void UpdateSelectedOverlay(
        DetectionBatch? latest,
        long frameDate)
    {
        _selectedOverlayCount = 0;
        if (latest is null ||
            !TryGetMediaTime(frameDate, out TimeSpan currentMediaTime))
        {
            return;
        }

        int count = _overlaySelector.Select(
            latest,
            currentMediaTime,
            _query,
            _overlayDetections);
        if (count == 0)
        {
            return;
        }

        _selectedOverlayGeneration = latest.Generation;
        _selectedOverlaySourceWidth = latest.SourceWidth;
        _selectedOverlaySourceHeight = latest.SourceHeight;
        _selectedOverlayCount = count;
    }

    private void ReportFailure(GpuYoloXDetector detector)
    {
        string? failure = detector.Failure;
        long now = Stopwatch.GetTimestamp();
        if (failure is not null &&
            Stopwatch.GetElapsedTime(_lastStatusLogTimestamp, now) >=
            TimeSpan.FromSeconds(2))
        {
            _lastStatusLogTimestamp = now;
            Context.Logger.Error($"[YoloSearch] {failure}");
        }
    }

    protected override void OnFlush()
    {
        _mediaTimestampGate.Reset();
        _selectedOverlayCount = 0;
        _selectedOverlayGeneration = -1;
        _detector?.ResetTimeline();
        _overlay?.Reset();
        _reportedGeneration = -1;
    }

    protected override void OnClose()
    {
        D3D11DetectionOverlay? overlay = _overlay;
        if (overlay is not null)
        {
            Context.Logger.Info(
                $"[YoloSearch] GPU overlay summary: " +
                $"rendered={overlay.RenderedFrames}, " +
                $"uploads={overlay.UploadedBatches}, " +
                $"boxes={overlay.UploadedBoxes}.");
        }
        overlay?.Dispose();
        _overlay = null;

        D3D11OutputPictureAllocator? outputPictures =
            _outputPictures;
        if (outputPictures is not null)
        {
            Context.Logger.Info(
                "[YoloSearch] D3D11 output picture summary: " +
                $"created={outputPictures.CreatedSurfaces}, " +
                $"reused={outputPictures.ReusedSurfaces}.");
        }
        outputPictures?.Dispose();
        _outputPictures = null;

        GpuYoloXDetector? detector = _detector;
        detector?.Dispose();
        if (detector is not null)
        {
            DetectorStatistics statistics = detector.Statistics;
            Context.Logger.Info(
                $"[YoloSearch] GPU summary: submitted={statistics.Submitted}, " +
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
        _detector = null;
        Context.ReleaseOutputVideoContext();
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

    private void Report(DetectionBatch batch)
    {
        IEnumerable<ObjectDetection> matches = batch.Detections;
        if (_query is not null)
        {
            matches = matches.Where(detection =>
                detection.ClassId == _query.ObjectClass.Id &&
                detection.Confidence >= _query.MinimumConfidence);
        }

        ObjectDetection[] visible = matches.Take(12).ToArray();
        if (visible.Length == 0)
        {
            return;
        }

        string labels = string.Join(
            "; ",
            visible.Select(detection =>
                $"{detection.Label} {detection.Confidence:P0} " +
                $"@ {detection.Box.X:F0},{detection.Box.Y:F0} " +
                $"{detection.Box.Width:F0}x{detection.Box.Height:F0}"));
        Context.Logger.Info(
            $"[YoloSearch] {batch.MediaTime}: {labels} " +
            $"(GPU inference {batch.InferenceDuration.TotalMilliseconds:F1} ms)");
    }
}
