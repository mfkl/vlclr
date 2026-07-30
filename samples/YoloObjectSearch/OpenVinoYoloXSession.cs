using System.Diagnostics;
using System.Runtime.InteropServices;
using TerraFX.Interop;
using VLCLR.ObjectDetection;

namespace YoloObjectSearch;

internal sealed unsafe class OpenVinoYoloXSession : IDisposable
{
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly YoloXOutputDecoder _decoder;

    private nint _core;
    private nint _remoteContext;
    private nint _yTensor;
    private nint _uvTensor;
    private nint _compiledModel;
    private nint _inferRequest;
    private nint _outputTensor;
    private nint _outputData;
    private int _outputElementCount;
    private bool _disposed;

    public OpenVinoYoloXSession(
        ID3D11Device* device,
        ID3D11Texture2D* inferenceTexture,
        string modelPath,
        YoloXOutputDecoder decoder)
    {
        if (device is null)
        {
            throw new ArgumentNullException(nameof(device));
        }
        if (inferenceTexture is null)
        {
            throw new ArgumentNullException(nameof(inferenceTexture));
        }
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException(
                "YOLOX model was not found.",
                modelPath);
        }

        _decoder = decoder ??
            throw new ArgumentNullException(nameof(decoder));

        try
        {
            CreateCoreAndRemoteInputs(device, inferenceTexture);
            CompileAndBind(modelPath);

            Check(
                OpenVinoNative.InferRequestInfer(_inferRequest),
                "ov_infer_request_infer(warmup)");
            Check(
                OpenVinoNative.InferRequestGetOutputByIndex(
                    _inferRequest,
                    0,
                    out _outputTensor),
                "ov_infer_request_get_output_tensor_by_index");
            Check(
                OpenVinoNative.TensorGetSize(
                    _outputTensor,
                    out nuint outputElementCount),
                "ov_tensor_get_size");
            _outputElementCount = checked((int)outputElementCount);
            if (_outputElementCount != _decoder.ExpectedOutputLength)
            {
                throw new InvalidOperationException(
                    $"YOLOX output contains {_outputElementCount} values; " +
                    $"expected {_decoder.ExpectedOutputLength}.");
            }

            Check(
                OpenVinoNative.TensorGetData(
                    _outputTensor,
                    out _outputData),
                "ov_tensor_data");
            if (_outputData == 0)
            {
                throw new InvalidOperationException(
                    "OpenVINO returned a null output tensor pointer.");
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public DetectionBatch Infer(
        long generation,
        int sourceWidth,
        int sourceHeight,
        TimeSpan mediaTime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Stopwatch timer = Stopwatch.StartNew();
        Check(
            OpenVinoNative.InferRequestInfer(_inferRequest),
            "ov_infer_request_infer");
        timer.Stop();

        IReadOnlyList<ObjectDetection> detections = _decoder.Decode(
            new ReadOnlySpan<float>(
                (void*)_outputData,
                _outputElementCount),
            sourceWidth,
            sourceHeight);

        return new DetectionBatch(
            _sessionId,
            generation,
            mediaTime,
            sourceWidth,
            sourceHeight,
            timer.Elapsed,
            detections);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        OpenVinoNative.TensorFree(_outputTensor);
        OpenVinoNative.InferRequestFree(_inferRequest);
        OpenVinoNative.CompiledModelFree(_compiledModel);
        OpenVinoNative.TensorFree(_uvTensor);
        OpenVinoNative.TensorFree(_yTensor);
        OpenVinoNative.RemoteContextFree(_remoteContext);
        OpenVinoNative.CoreFree(_core);

        _outputTensor = 0;
        _outputData = 0;
        _inferRequest = 0;
        _compiledModel = 0;
        _uvTensor = 0;
        _yTensor = 0;
        _remoteContext = 0;
        _core = 0;
        _disposed = true;
    }

    private void CreateCoreAndRemoteInputs(
        ID3D11Device* device,
        ID3D11Texture2D* inferenceTexture)
    {
        Check(OpenVinoNative.CoreCreate(out _core), "ov_core_create");

        using Utf8String gpu = new("GPU");
        using Utf8String contextTypeKey = new("CONTEXT_TYPE");
        using Utf8String contextTypeValue = new("VA_SHARED");
        using Utf8String deviceKey = new("VA_DEVICE");
        using Utf8String tileKey = new("TILE_ID");
        using Utf8String tileValue = new("-1");
        Check(
            OpenVinoNative.CoreCreateD3DContext(
                _core,
                gpu.Pointer,
                6,
                out _remoteContext,
                contextTypeKey.Pointer,
                contextTypeValue.Pointer,
                deviceKey.Pointer,
                (nint)device,
                tileKey.Pointer,
                tileValue.Pointer),
            "ov_core_create_context(D3D11)");

        using Utf8String sharedMemoryKey = new("SHARED_MEM_TYPE");
        using Utf8String sharedMemoryValue = new("VA_SURFACE");
        using Utf8String objectHandleKey = new("DEV_OBJECT_HANDLE");
        using Utf8String planeKey = new("VA_PLANE");
        using Utf8String yPlane = new("0");
        using Utf8String uvPlane = new("1");

        long* yDimensions = stackalloc long[] { 1, 416, 416, 1 };
        Check(
            OpenVinoNative.RemoteContextCreateD3DTensor(
                _remoteContext,
                OvElementType.U8,
                new OvShape(4, yDimensions),
                6,
                out _yTensor,
                sharedMemoryKey.Pointer,
                sharedMemoryValue.Pointer,
                objectHandleKey.Pointer,
                (nint)inferenceTexture,
                planeKey.Pointer,
                yPlane.Pointer),
            "ov_remote_context_create_tensor(Y)");

        long* uvDimensions = stackalloc long[] { 1, 208, 208, 2 };
        Check(
            OpenVinoNative.RemoteContextCreateD3DTensor(
                _remoteContext,
                OvElementType.U8,
                new OvShape(4, uvDimensions),
                6,
                out _uvTensor,
                sharedMemoryKey.Pointer,
                sharedMemoryValue.Pointer,
                objectHandleKey.Pointer,
                (nint)inferenceTexture,
                planeKey.Pointer,
                uvPlane.Pointer),
            "ov_remote_context_create_tensor(UV)");
    }

    private void CompileAndBind(string modelPath)
    {
        nint sourceModel = 0;
        nint preprocessor = 0;
        nint inputInfo = 0;
        nint tensorInfo = 0;
        nint preprocessSteps = 0;
        nint modelInfo = 0;
        nint modelLayout = 0;
        nint inferenceModel = 0;
        nint yPort = 0;
        nint uvPort = 0;
        nint yName = 0;
        nint uvName = 0;

        try
        {
            using Utf8String modelPathUtf8 = new(modelPath);
            Check(
                OpenVinoNative.CoreReadModel(
                    _core,
                    modelPathUtf8.Pointer,
                    0,
                    out sourceModel),
                "ov_core_read_model");
            Check(
                OpenVinoNative.PrePostProcessorCreate(
                    sourceModel,
                    out preprocessor),
                "ov_preprocess_prepostprocessor_create");
            Check(
                OpenVinoNative.PrePostProcessorGetInputInfo(
                    preprocessor,
                    out inputInfo),
                "ov_preprocess_prepostprocessor_get_input_info");
            Check(
                OpenVinoNative.PreprocessInputInfoGetTensorInfo(
                    inputInfo,
                    out tensorInfo),
                "ov_preprocess_input_info_get_tensor_info");
            Check(
                OpenVinoNative.PreprocessTensorInfoSetElementType(
                    tensorInfo,
                    OvElementType.U8),
                "ov_preprocess_input_tensor_info_set_element_type");

            using Utf8String ySubname = new("y");
            using Utf8String uvSubname = new("uv");
            Check(
                OpenVinoNative.PreprocessTensorInfoSetNv12TwoPlanes(
                    tensorInfo,
                    OvColorFormat.Nv12TwoPlanes,
                    2,
                    ySubname.Pointer,
                    uvSubname.Pointer),
                "set_color_format_with_subname");

            using Utf8String surfaceMemoryType = new("GPU_SURFACE");
            Check(
                OpenVinoNative.PreprocessTensorInfoSetMemoryType(
                    tensorInfo,
                    surfaceMemoryType.Pointer),
                "set_memory_type");
            Check(
                OpenVinoNative.PreprocessInputInfoGetSteps(
                    inputInfo,
                    out preprocessSteps),
                "get_preprocess_steps");
            Check(
                OpenVinoNative.PreprocessStepsConvertColor(
                    preprocessSteps,
                    OvColorFormat.Bgr),
                "convert_color");
            Check(
                OpenVinoNative.PreprocessInputInfoGetModelInfo(
                    inputInfo,
                    out modelInfo),
                "get_model_info");

            using Utf8String nchw = new("NCHW");
            Check(
                OpenVinoNative.LayoutCreate(
                    nchw.Pointer,
                    out modelLayout),
                "ov_layout_create");
            Check(
                OpenVinoNative.PreprocessModelInfoSetLayout(
                    modelInfo,
                    modelLayout),
                "set_model_layout");
            Check(
                OpenVinoNative.PrePostProcessorBuild(
                    preprocessor,
                    out inferenceModel),
                "ov_preprocess_prepostprocessor_build");
            Check(
                OpenVinoNative.CoreCompileModelWithContext(
                    _core,
                    inferenceModel,
                    _remoteContext,
                    0,
                    out _compiledModel),
                "ov_core_compile_model_with_context");
            Check(
                OpenVinoNative.CompiledModelCreateInferRequest(
                    _compiledModel,
                    out _inferRequest),
                "ov_compiled_model_create_infer_request");

            Check(
                OpenVinoNative.ModelGetInputByIndex(
                    inferenceModel,
                    0,
                    out yPort),
                "ov_model_const_input_by_index(Y)");
            Check(
                OpenVinoNative.PortGetAnyName(yPort, out yName),
                "ov_port_get_any_name(Y)");
            Check(
                OpenVinoNative.ModelGetInputByIndex(
                    inferenceModel,
                    1,
                    out uvPort),
                "ov_model_const_input_by_index(UV)");
            Check(
                OpenVinoNative.PortGetAnyName(uvPort, out uvName),
                "ov_port_get_any_name(UV)");
            Check(
                OpenVinoNative.InferRequestSetTensor(
                    _inferRequest,
                    yName,
                    _yTensor),
                "ov_infer_request_set_tensor(Y)");
            Check(
                OpenVinoNative.InferRequestSetTensor(
                    _inferRequest,
                    uvName,
                    _uvTensor),
                "ov_infer_request_set_tensor(UV)");
        }
        finally
        {
            OpenVinoNative.Free(uvName);
            OpenVinoNative.Free(yName);
            OpenVinoNative.OutputPortFree(uvPort);
            OpenVinoNative.OutputPortFree(yPort);
            OpenVinoNative.ModelFree(inferenceModel);
            OpenVinoNative.LayoutFree(modelLayout);
            OpenVinoNative.PreprocessModelInfoFree(modelInfo);
            OpenVinoNative.PreprocessStepsFree(preprocessSteps);
            OpenVinoNative.PreprocessTensorInfoFree(tensorInfo);
            OpenVinoNative.PreprocessInputInfoFree(inputInfo);
            OpenVinoNative.PrePostProcessorFree(preprocessor);
            OpenVinoNative.ModelFree(sourceModel);
        }
    }

    private static void Check(OvStatus status, string operation)
    {
        if (status == OvStatus.Ok)
        {
            return;
        }

        string message = Marshal.PtrToStringUTF8(
            OpenVinoNative.GetLastErrorMessage()) ??
            status.ToString();
        throw new InvalidOperationException(
            $"{operation} failed with {(int)status}: {message}");
    }
}
