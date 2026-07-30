using System.Runtime.InteropServices;
using System.Diagnostics;
using TerraFX.Interop;
using VLCLR.ObjectDetection;
using static TerraFX.Interop.Windows;

namespace YoloObjectSearch.CSharpProbe;

internal static unsafe class Program
{
    private const uint IntelVendorId = 0x8086;

    public static int Main(string[] args)
    {
        IDXGIFactory1* factory = null;
        IDXGIAdapter1* adapter = null;
        ID3D11Device* device = null;
        ID3D11DeviceContext* deviceContext = null;
        ID3D11Texture2D* sourceTexture = null;
        D3D11Nv12Scaler? scaler = null;
        nint core = 0;
        nint remoteContext = 0;
        nint yTensor = 0;
        nint uvTensor = 0;

        try
        {
            factory = CreateFactory();
            adapter = FindIntelAdapter(factory);
            CreateDevice(adapter, &device, &deviceContext);
            sourceTexture = CreateDecoderLikeNv12Texture(device);
            scaler = new D3D11Nv12Scaler(
                device,
                deviceContext,
                sourceTexture,
                sourceArraySlice: 3,
                sourceWidth: 1920,
                sourceHeight: 1080,
                outputWidth: 416,
                outputHeight: 416);
            scaler.Blit();

            CheckOpenVino(OpenVinoNative.CoreCreate(out core), "ov_core_create");

            using Utf8String gpu = new("GPU");
            using Utf8String contextTypeKey = new("CONTEXT_TYPE");
            using Utf8String contextTypeValue = new("VA_SHARED");
            using Utf8String deviceKey = new("VA_DEVICE");
            using Utf8String tileKey = new("TILE_ID");
            using Utf8String tileValue = new("-1");

            CheckOpenVino(
                OpenVinoNative.CoreCreateD3DContext(
                    core,
                    gpu.Pointer,
                    6,
                    out remoteContext,
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
            OvShape yShape = new(4, yDimensions);
            CheckOpenVino(
                OpenVinoNative.RemoteContextCreateD3DTensor(
                    remoteContext,
                    OvElementType.U8,
                    yShape,
                    6,
                    out yTensor,
                    sharedMemoryKey.Pointer,
                    sharedMemoryValue.Pointer,
                    objectHandleKey.Pointer,
                    (nint)scaler.OutputTexture,
                    planeKey.Pointer,
                    yPlane.Pointer),
                "ov_remote_context_create_tensor(Y)");

            long* uvDimensions = stackalloc long[] { 1, 208, 208, 2 };
            OvShape uvShape = new(4, uvDimensions);
            CheckOpenVino(
                OpenVinoNative.RemoteContextCreateD3DTensor(
                    remoteContext,
                    OvElementType.U8,
                    uvShape,
                    6,
                    out uvTensor,
                    sharedMemoryKey.Pointer,
                    sharedMemoryValue.Pointer,
                    objectHandleKey.Pointer,
                    (nint)scaler.OutputTexture,
                    planeKey.Pointer,
                    uvPlane.Pointer),
                "ov_remote_context_create_tensor(UV)");

            Console.WriteLine("implementation_language=CSharp");
            Console.WriteLine("authored_cpp=0");
            Console.WriteLine("d3d11_device=Intel");
            Console.WriteLine("source_texture=NV12,1920x1080,array_slice=3");
            Console.WriteLine("inference_texture=NV12,416x416");
            Console.WriteLine("gpu_resize_and_letterbox=passed");
            PrintTensor("y", yTensor);
            PrintTensor("uv", uvTensor);

            if (args.Length > 0)
            {
                RunYoloInference(
                    core,
                    remoteContext,
                    yTensor,
                    uvTensor,
                    Path.GetFullPath(args[0]));
            }

            Console.WriteLine("pure_csharp_remote_tensor_probe=passed");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error={exception.Message}");
            return 1;
        }
        finally
        {
            if (uvTensor != 0)
            {
                OpenVinoNative.TensorFree(uvTensor);
            }
            if (yTensor != 0)
            {
                OpenVinoNative.TensorFree(yTensor);
            }
            if (remoteContext != 0)
            {
                OpenVinoNative.RemoteContextFree(remoteContext);
            }
            if (core != 0)
            {
                OpenVinoNative.CoreFree(core);
            }
            scaler?.Dispose();
            if (sourceTexture is not null)
            {
                sourceTexture->Release();
            }
            if (deviceContext is not null)
            {
                deviceContext->Release();
            }
            if (device is not null)
            {
                device->Release();
            }
            if (adapter is not null)
            {
                adapter->Release();
            }
            if (factory is not null)
            {
                factory->Release();
            }
        }
    }

    private static IDXGIFactory1* CreateFactory()
    {
        IDXGIFactory1* factory = null;
        Guid iid = IID_IDXGIFactory1;
        CheckHResult(
            CreateDXGIFactory1(&iid, (void**)&factory),
            "CreateDXGIFactory1");
        return factory;
    }

    private static IDXGIAdapter1* FindIntelAdapter(IDXGIFactory1* factory)
    {
        for (uint index = 0; ; index++)
        {
            IDXGIAdapter1* candidate = null;
            int result = factory->EnumAdapters1(index, &candidate);
            if (result == DXGI_ERROR_NOT_FOUND)
            {
                break;
            }

            CheckHResult(result, "IDXGIFactory1.EnumAdapters1");
            DXGI_ADAPTER_DESC1 description;
            CheckHResult(
                candidate->GetDesc1(&description),
                "IDXGIAdapter1.GetDesc1");
            if (description.VendorId == IntelVendorId)
            {
                return candidate;
            }

            candidate->Release();
        }

        throw new InvalidOperationException("No Intel DXGI adapter was found.");
    }

    private static void CreateDevice(
        IDXGIAdapter1* adapter,
        ID3D11Device** device,
        ID3D11DeviceContext** context)
    {
        D3D_FEATURE_LEVEL* requestedLevels = stackalloc D3D_FEATURE_LEVEL[]
        {
            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1,
            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0
        };
        D3D_FEATURE_LEVEL selectedLevel;
        CheckHResult(
            D3D11CreateDevice(
                (IDXGIAdapter*)adapter,
                D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_UNKNOWN,
                0,
                (uint)(
                    D3D11_CREATE_DEVICE_FLAG
                        .D3D11_CREATE_DEVICE_BGRA_SUPPORT |
                    D3D11_CREATE_DEVICE_FLAG
                        .D3D11_CREATE_DEVICE_VIDEO_SUPPORT),
                requestedLevels,
                2,
                D3D11_SDK_VERSION,
                device,
                &selectedLevel,
                context),
            "D3D11CreateDevice");
    }

    private static ID3D11Texture2D* CreateDecoderLikeNv12Texture(
        ID3D11Device* device)
    {
        D3D11_TEXTURE2D_DESC description = new()
        {
            Width = 1920,
            Height = 1080,
            MipLevels = 1,
            ArraySize = 8,
            Format = DXGI_FORMAT.DXGI_FORMAT_NV12,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
            BindFlags = (uint)(
                D3D11_BIND_FLAG.D3D11_BIND_DECODER |
                D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE),
            CPUAccessFlags = 0,
            MiscFlags = 0
        };
        ID3D11Texture2D* texture = null;
        CheckHResult(
            device->CreateTexture2D(&description, null, &texture),
            "ID3D11Device.CreateTexture2D(decoder-like NV12 array)");
        return texture;
    }

    private static void RunYoloInference(
        nint core,
        nint remoteContext,
        nint yTensor,
        nint uvTensor,
        string modelPath)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("YOLOX model was not found.", modelPath);
        }

        nint sourceModel = 0;
        nint preprocessor = 0;
        nint inputInfo = 0;
        nint tensorInfo = 0;
        nint preprocessSteps = 0;
        nint modelInfo = 0;
        nint modelLayout = 0;
        nint inferenceModel = 0;
        nint compiledModel = 0;
        nint inferRequest = 0;
        nint outputTensor = 0;
        nint yPort = 0;
        nint uvPort = 0;
        nint yName = 0;
        nint uvName = 0;

        try
        {
            using Utf8String modelPathUtf8 = new(modelPath);
            CheckOpenVino(
                OpenVinoNative.CoreReadModel(
                    core,
                    modelPathUtf8.Pointer,
                    0,
                    out sourceModel),
                "ov_core_read_model");

            CheckOpenVino(
                OpenVinoNative.PrePostProcessorCreate(
                    sourceModel,
                    out preprocessor),
                "ov_preprocess_prepostprocessor_create");
            CheckOpenVino(
                OpenVinoNative.PrePostProcessorGetInputInfo(
                    preprocessor,
                    out inputInfo),
                "ov_preprocess_prepostprocessor_get_input_info");
            CheckOpenVino(
                OpenVinoNative.PreprocessInputInfoGetTensorInfo(
                    inputInfo,
                    out tensorInfo),
                "ov_preprocess_input_info_get_tensor_info");
            CheckOpenVino(
                OpenVinoNative.PreprocessTensorInfoSetElementType(
                    tensorInfo,
                    OvElementType.U8),
                "ov_preprocess_input_tensor_info_set_element_type");

            using Utf8String ySubname = new("y");
            using Utf8String uvSubname = new("uv");
            CheckOpenVino(
                OpenVinoNative.PreprocessTensorInfoSetNv12TwoPlanes(
                    tensorInfo,
                    OvColorFormat.Nv12TwoPlanes,
                    2,
                    ySubname.Pointer,
                    uvSubname.Pointer),
                "ov_preprocess_input_tensor_info_set_color_format_with_subname");

            using Utf8String surfaceMemoryType = new("GPU_SURFACE");
            CheckOpenVino(
                OpenVinoNative.PreprocessTensorInfoSetMemoryType(
                    tensorInfo,
                    surfaceMemoryType.Pointer),
                "ov_preprocess_input_tensor_info_set_memory_type");

            CheckOpenVino(
                OpenVinoNative.PreprocessInputInfoGetSteps(
                    inputInfo,
                    out preprocessSteps),
                "ov_preprocess_input_info_get_preprocess_steps");
            CheckOpenVino(
                OpenVinoNative.PreprocessStepsConvertColor(
                    preprocessSteps,
                    OvColorFormat.Bgr),
                "ov_preprocess_preprocess_steps_convert_color");

            CheckOpenVino(
                OpenVinoNative.PreprocessInputInfoGetModelInfo(
                    inputInfo,
                    out modelInfo),
                "ov_preprocess_input_info_get_model_info");
            using Utf8String nchw = new("NCHW");
            CheckOpenVino(
                OpenVinoNative.LayoutCreate(nchw.Pointer, out modelLayout),
                "ov_layout_create");
            CheckOpenVino(
                OpenVinoNative.PreprocessModelInfoSetLayout(
                    modelInfo,
                    modelLayout),
                "ov_preprocess_input_model_info_set_layout");

            CheckOpenVino(
                OpenVinoNative.PrePostProcessorBuild(
                    preprocessor,
                    out inferenceModel),
                "ov_preprocess_prepostprocessor_build");

            Stopwatch compileTimer = Stopwatch.StartNew();
            CheckOpenVino(
                OpenVinoNative.CoreCompileModelWithContext(
                    core,
                    inferenceModel,
                    remoteContext,
                    0,
                    out compiledModel),
                "ov_core_compile_model_with_context");
            compileTimer.Stop();

            CheckOpenVino(
                OpenVinoNative.CompiledModelCreateInferRequest(
                    compiledModel,
                    out inferRequest),
                "ov_compiled_model_create_infer_request");

            CheckOpenVino(
                OpenVinoNative.ModelGetInputByIndex(
                    inferenceModel,
                    0,
                    out yPort),
                "ov_model_const_input_by_index(Y)");
            CheckOpenVino(
                OpenVinoNative.PortGetAnyName(yPort, out yName),
                "ov_port_get_any_name(Y)");
            CheckOpenVino(
                OpenVinoNative.ModelGetInputByIndex(
                    inferenceModel,
                    1,
                    out uvPort),
                "ov_model_const_input_by_index(UV)");
            CheckOpenVino(
                OpenVinoNative.PortGetAnyName(uvPort, out uvName),
                "ov_port_get_any_name(UV)");

            CheckOpenVino(
                OpenVinoNative.InferRequestSetTensor(
                    inferRequest,
                    yName,
                    yTensor),
                "ov_infer_request_set_tensor(Y)");
            CheckOpenVino(
                OpenVinoNative.InferRequestSetTensor(
                    inferRequest,
                    uvName,
                    uvTensor),
                "ov_infer_request_set_tensor(UV)");

            Stopwatch inferenceTimer = Stopwatch.StartNew();
            CheckOpenVino(
                OpenVinoNative.InferRequestInfer(inferRequest),
                "ov_infer_request_infer");
            inferenceTimer.Stop();

            CheckOpenVino(
                OpenVinoNative.InferRequestGetOutputByIndex(
                    inferRequest,
                    0,
                    out outputTensor),
                "ov_infer_request_get_output_tensor_by_index");
            CheckOpenVino(
                OpenVinoNative.TensorGetSize(
                    outputTensor,
                    out nuint outputElementCount),
                "ov_tensor_get_size");
            CheckOpenVino(
                OpenVinoNative.TensorGetData(outputTensor, out nint outputData),
                "ov_tensor_data");

            float firstOutput = outputData == 0
                ? float.NaN
                : *(float*)outputData;
            var decoder = new YoloXOutputDecoder(
                Coco80ObjectCatalog.Create());
            Stopwatch postprocessTimer = Stopwatch.StartNew();
            IReadOnlyList<ObjectDetection> detections = decoder.Decode(
                new ReadOnlySpan<float>(
                    (void*)outputData,
                    checked((int)outputElementCount)),
                416,
                416);
            postprocessTimer.Stop();

            const int postprocessIterations = 101;
            var postprocessSamples = new double[postprocessIterations];
            for (int iteration = 0;
                 iteration < postprocessIterations;
                 iteration++)
            {
                Stopwatch sampleTimer = Stopwatch.StartNew();
                decoder.Decode(
                    new ReadOnlySpan<float>(
                        (void*)outputData,
                        checked((int)outputElementCount)),
                    416,
                    416);
                postprocessSamples[iteration] =
                    sampleTimer.Elapsed.TotalMilliseconds;
            }
            Array.Sort(postprocessSamples);

            Console.WriteLine($"model={Path.GetFileName(modelPath)}");
            Console.WriteLine($"model_compile_ms={compileTimer.Elapsed.TotalMilliseconds:F3}");
            Console.WriteLine($"inference_ms={inferenceTimer.Elapsed.TotalMilliseconds:F3}");
            Console.WriteLine($"output_elements={outputElementCount}");
            Console.WriteLine($"output_first_value={firstOutput:R}");
            Console.WriteLine($"postprocess_cold_ms={postprocessTimer.Elapsed.TotalMilliseconds:F3}");
            Console.WriteLine(
                $"postprocess_median_ms=" +
                $"{postprocessSamples[postprocessIterations / 2]:F3}");
            Console.WriteLine(
                $"postprocess_p95_ms=" +
                $"{postprocessSamples[(int)(postprocessIterations * 0.95)]:F3}");
            Console.WriteLine($"detections={detections.Count}");
            if (detections.Count > 0)
            {
                ObjectDetection top = detections[0];
                Console.WriteLine(
                    $"top_detection={top.Label},{top.Confidence:F4}," +
                    $"{top.Box.X:F1},{top.Box.Y:F1}," +
                    $"{top.Box.Width:F1},{top.Box.Height:F1}");
            }
            Console.WriteLine("pure_csharp_yolox_inference=passed");
        }
        finally
        {
            OpenVinoNative.Free(uvName);
            OpenVinoNative.Free(yName);
            OpenVinoNative.OutputPortFree(uvPort);
            OpenVinoNative.OutputPortFree(yPort);
            OpenVinoNative.TensorFree(outputTensor);
            OpenVinoNative.InferRequestFree(inferRequest);
            OpenVinoNative.CompiledModelFree(compiledModel);
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

    private static void PrintTensor(string plane, nint tensor)
    {
        CheckOpenVino(
            OpenVinoNative.RemoteTensorGetParams(
                tensor,
                out nuint parameterCount,
                out nint parameters),
            $"ov_remote_tensor_get_params({plane})");
        try
        {
            string value = Marshal.PtrToStringUTF8(parameters) ?? "";
            Console.WriteLine($"{plane}_parameter_count={parameterCount}");
            Console.WriteLine($"{plane}_remote_tensor={value}");
        }
        finally
        {
            OpenVinoNative.Free(parameters);
        }
    }

    internal static void CheckHResult(int result, string operation)
    {
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    private static void CheckOpenVino(OvStatus status, string operation)
    {
        if (status == OvStatus.Ok)
        {
            return;
        }

        string message = Marshal.PtrToStringUTF8(
            OpenVinoNative.GetLastErrorMessage()) ?? status.ToString();
        throw new InvalidOperationException(
            $"{operation} failed with {(int)status}: {message}");
    }
}

internal sealed unsafe class D3D11Nv12Scaler : IDisposable
{
    private ID3D11DeviceContext* _deviceContext;
    private ID3D11VideoDevice* _videoDevice;
    private ID3D11VideoContext* _videoContext;
    private ID3D11VideoProcessorEnumerator* _enumerator;
    private ID3D11VideoProcessor* _processor;
    private ID3D11VideoProcessorInputView* _inputView;
    private ID3D11VideoProcessorOutputView* _outputView;
    private ID3D11Texture2D* _outputTexture;

    public D3D11Nv12Scaler(
        ID3D11Device* device,
        ID3D11DeviceContext* deviceContext,
        ID3D11Texture2D* sourceTexture,
        uint sourceArraySlice,
        uint sourceWidth,
        uint sourceHeight,
        uint outputWidth,
        uint outputHeight)
    {
        if (device is null ||
            deviceContext is null ||
            sourceTexture is null)
        {
            throw new ArgumentNullException(
                nameof(device),
                "D3D11 device, context, and source texture are required.");
        }

        _deviceContext = deviceContext;
        _deviceContext->AddRef();

        try
        {
            Guid videoDeviceIid = IID_ID3D11VideoDevice;
            ID3D11VideoDevice* videoDevice = null;
            Program.CheckHResult(
                device->QueryInterface(
                    &videoDeviceIid,
                    (void**)&videoDevice),
                "ID3D11Device.QueryInterface(ID3D11VideoDevice)");
            _videoDevice = videoDevice;

            Guid videoContextIid = IID_ID3D11VideoContext;
            ID3D11VideoContext* videoContext = null;
            Program.CheckHResult(
                deviceContext->QueryInterface(
                    &videoContextIid,
                    (void**)&videoContext),
                "ID3D11DeviceContext.QueryInterface(ID3D11VideoContext)");
            _videoContext = videoContext;

            D3D11_VIDEO_PROCESSOR_CONTENT_DESC content = new()
            {
                InputFrameFormat =
                    D3D11_VIDEO_FRAME_FORMAT
                        .D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE,
                InputFrameRate = new DXGI_RATIONAL
                {
                    Numerator = 30,
                    Denominator = 1
                },
                InputWidth = sourceWidth,
                InputHeight = sourceHeight,
                OutputFrameRate = new DXGI_RATIONAL
                {
                    Numerator = 30,
                    Denominator = 1
                },
                OutputWidth = outputWidth,
                OutputHeight = outputHeight,
                Usage = D3D11_VIDEO_USAGE.D3D11_VIDEO_USAGE_PLAYBACK_NORMAL
            };
            ID3D11VideoProcessorEnumerator* enumerator = null;
            Program.CheckHResult(
                _videoDevice->CreateVideoProcessorEnumerator(
                    &content,
                    &enumerator),
                "ID3D11VideoDevice.CreateVideoProcessorEnumerator");
            _enumerator = enumerator;

            D3D11_TEXTURE2D_DESC sourceDescription;
            sourceTexture->GetDesc(&sourceDescription);
            uint sourceFormatSupport;
            Program.CheckHResult(
                _enumerator->CheckVideoProcessorFormat(
                    sourceDescription.Format,
                    &sourceFormatSupport),
                "CheckVideoProcessorFormat(source)");
            if ((sourceFormatSupport &
                    (uint)D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT
                        .D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_INPUT) == 0)
            {
                throw new NotSupportedException(
                    $"D3D11 video processor cannot read " +
                    $"{sourceDescription.Format}.");
            }

            uint outputFormatSupport;
            Program.CheckHResult(
                _enumerator->CheckVideoProcessorFormat(
                    DXGI_FORMAT.DXGI_FORMAT_NV12,
                    &outputFormatSupport),
                "CheckVideoProcessorFormat(NV12 output)");
            if ((outputFormatSupport &
                    (uint)D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT
                        .D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_OUTPUT) == 0)
            {
                throw new NotSupportedException(
                    "D3D11 video processor cannot output NV12.");
            }

            D3D11_TEXTURE2D_DESC outputDescription = new()
            {
                Width = outputWidth,
                Height = outputHeight,
                MipLevels = 1,
                ArraySize = 1,
                Format = DXGI_FORMAT.DXGI_FORMAT_NV12,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
                Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
                BindFlags = (uint)(
                    D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET |
                    D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE)
            };
            ID3D11Texture2D* outputTexture = null;
            Program.CheckHResult(
                device->CreateTexture2D(
                    &outputDescription,
                    null,
                    &outputTexture),
                "ID3D11Device.CreateTexture2D(inference NV12)");
            _outputTexture = outputTexture;

            D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC inputViewDescription =
                new()
                {
                    ViewDimension = D3D11_VPIV_DIMENSION
                        .D3D11_VPIV_DIMENSION_TEXTURE2D
                };
            inputViewDescription.Texture2D.MipSlice = 0;
            inputViewDescription.Texture2D.ArraySlice =
                sourceArraySlice;
            ID3D11VideoProcessorInputView* inputView = null;
            Program.CheckHResult(
                _videoDevice->CreateVideoProcessorInputView(
                    (ID3D11Resource*)sourceTexture,
                    _enumerator,
                    &inputViewDescription,
                    &inputView),
                "ID3D11VideoDevice.CreateVideoProcessorInputView");
            _inputView = inputView;

            D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC outputViewDescription =
                new()
                {
                    ViewDimension = D3D11_VPOV_DIMENSION
                        .D3D11_VPOV_DIMENSION_TEXTURE2D
                };
            outputViewDescription.Texture2D.MipSlice = 0;
            ID3D11VideoProcessorOutputView* outputView = null;
            Program.CheckHResult(
                _videoDevice->CreateVideoProcessorOutputView(
                    (ID3D11Resource*)_outputTexture,
                    _enumerator,
                    &outputViewDescription,
                    &outputView),
                "ID3D11VideoDevice.CreateVideoProcessorOutputView");
            _outputView = outputView;

            ID3D11VideoProcessor* processor = null;
            Program.CheckHResult(
                _videoDevice->CreateVideoProcessor(
                    _enumerator,
                    0,
                    &processor),
                "ID3D11VideoDevice.CreateVideoProcessor");
            _processor = processor;

            RECT sourceRectangle = new(
                0,
                0,
                checked((int)sourceWidth),
                checked((int)sourceHeight));
            float scale = MathF.Min(
                (float)outputWidth / sourceWidth,
                (float)outputHeight / sourceHeight);
            int contentWidth = checked((int)MathF.Round(
                sourceWidth * scale));
            int contentHeight = checked((int)MathF.Round(
                sourceHeight * scale));
            int contentX =
                (checked((int)outputWidth) - contentWidth) / 2;
            int contentY =
                (checked((int)outputHeight) - contentHeight) / 2;
            RECT destinationRectangle = new(
                contentX,
                contentY,
                contentX + contentWidth,
                contentY + contentHeight);
            RECT targetRectangle = new(
                0,
                0,
                checked((int)outputWidth),
                checked((int)outputHeight));

            _videoContext->VideoProcessorSetStreamSourceRect(
                _processor,
                0,
                1,
                &sourceRectangle);
            _videoContext->VideoProcessorSetStreamDestRect(
                _processor,
                0,
                1,
                &destinationRectangle);
            _videoContext->VideoProcessorSetOutputTargetRect(
                _processor,
                1,
                &targetRectangle);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public ID3D11Texture2D* OutputTexture => _outputTexture;

    public void Blit()
    {
        ObjectDisposedException.ThrowIf(
            _processor is null,
            this);

        D3D11_VIDEO_PROCESSOR_STREAM stream = new()
        {
            Enable = 1,
            pInputSurface = _inputView
        };
        Program.CheckHResult(
            _videoContext->VideoProcessorBlt(
                _processor,
                _outputView,
                0,
                1,
                &stream),
            "ID3D11VideoContext.VideoProcessorBlt");
        _deviceContext->Flush();
    }

    public void Dispose()
    {
        if (_outputView is not null)
        {
            _outputView->Release();
            _outputView = null;
        }
        if (_inputView is not null)
        {
            _inputView->Release();
            _inputView = null;
        }
        if (_processor is not null)
        {
            _processor->Release();
            _processor = null;
        }
        if (_enumerator is not null)
        {
            _enumerator->Release();
            _enumerator = null;
        }
        if (_outputTexture is not null)
        {
            _outputTexture->Release();
            _outputTexture = null;
        }
        if (_videoContext is not null)
        {
            _videoContext->Release();
            _videoContext = null;
        }
        if (_videoDevice is not null)
        {
            _videoDevice->Release();
            _videoDevice = null;
        }
        if (_deviceContext is not null)
        {
            _deviceContext->Release();
            _deviceContext = null;
        }
    }
}

internal enum OvStatus
{
    Ok = 0
}

internal enum OvElementType : uint
{
    U8 = 16
}

internal enum OvColorFormat : uint
{
    Nv12TwoPlanes = 2,
    Bgr = 6
}

[StructLayout(LayoutKind.Sequential)]
internal readonly unsafe struct OvShape
{
    public OvShape(long rank, long* dimensions)
    {
        Rank = rank;
        Dimensions = dimensions;
    }

    public readonly long Rank;
    public readonly long* Dimensions;
}

internal sealed class Utf8String : IDisposable
{
    public Utf8String(string value)
    {
        Pointer = Marshal.StringToCoTaskMemUTF8(value);
    }

    public nint Pointer { get; private set; }

    public void Dispose()
    {
        nint pointer = Pointer;
        Pointer = 0;
        Marshal.FreeCoTaskMem(pointer);
    }
}

internal static unsafe partial class OpenVinoNative
{
    private const string LibraryName = "openvino_c";

    static OpenVinoNative()
    {
        NativeLibrary.SetDllImportResolver(
            typeof(OpenVinoNative).Assembly,
            ResolveLibrary);
    }

    [DllImport(LibraryName, EntryPoint = "ov_core_create", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus CoreCreate(out nint core);

    [DllImport(LibraryName, EntryPoint = "ov_core_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CoreFree(nint core);

    [DllImport(LibraryName, EntryPoint = "ov_core_read_model", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus CoreReadModel(
        nint core,
        nint modelPath,
        nint weightsPath,
        out nint model);

    [DllImport(LibraryName, EntryPoint = "ov_core_create_context", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus CoreCreateD3DContext(
        nint core,
        nint deviceName,
        nuint argumentCount,
        out nint context,
        nint key1,
        nint value1,
        nint key2,
        nint value2,
        nint key3,
        nint value3);

    [DllImport(LibraryName, EntryPoint = "ov_remote_context_create_tensor", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus RemoteContextCreateD3DTensor(
        nint context,
        OvElementType elementType,
        OvShape shape,
        nuint argumentCount,
        out nint tensor,
        nint key1,
        nint value1,
        nint key2,
        nint value2,
        nint key3,
        nint value3);

    [DllImport(LibraryName, EntryPoint = "ov_remote_tensor_get_params", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus RemoteTensorGetParams(
        nint tensor,
        out nuint parameterCount,
        out nint parameters);

    [DllImport(LibraryName, EntryPoint = "ov_preprocess_prepostprocessor_create", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus PrePostProcessorCreate(
        nint model,
        out nint preprocessor);

    [DllImport(LibraryName, EntryPoint = "ov_preprocess_prepostprocessor_get_input_info", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus PrePostProcessorGetInputInfo(
        nint preprocessor,
        out nint inputInfo);

    [DllImport(LibraryName, EntryPoint = "ov_preprocess_input_info_get_tensor_info", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus PreprocessInputInfoGetTensorInfo(
        nint inputInfo,
        out nint tensorInfo);

    [DllImport(LibraryName, EntryPoint = "ov_preprocess_input_tensor_info_set_element_type", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus PreprocessTensorInfoSetElementType(
        nint tensorInfo,
        OvElementType elementType);

    [DllImport(LibraryName, EntryPoint = "ov_preprocess_input_tensor_info_set_color_format_with_subname", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus PreprocessTensorInfoSetNv12TwoPlanes(
        nint tensorInfo,
        OvColorFormat colorFormat,
        nuint subnameCount,
        nint firstSubname,
        nint secondSubname);

    [DllImport(LibraryName, EntryPoint = "ov_preprocess_input_tensor_info_set_memory_type", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus PreprocessTensorInfoSetMemoryType(
        nint tensorInfo,
        nint memoryType);

    [DllImport(LibraryName, EntryPoint = "ov_preprocess_input_info_get_preprocess_steps", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus PreprocessInputInfoGetSteps(
        nint inputInfo,
        out nint preprocessSteps);

    [DllImport(LibraryName, EntryPoint = "ov_preprocess_preprocess_steps_convert_color", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus PreprocessStepsConvertColor(
        nint preprocessSteps,
        OvColorFormat colorFormat);

    [DllImport(LibraryName, EntryPoint = "ov_preprocess_input_info_get_model_info", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus PreprocessInputInfoGetModelInfo(
        nint inputInfo,
        out nint modelInfo);

    [DllImport(LibraryName, EntryPoint = "ov_layout_create", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus LayoutCreate(
        nint description,
        out nint layout);

    [DllImport(LibraryName, EntryPoint = "ov_preprocess_input_model_info_set_layout", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus PreprocessModelInfoSetLayout(
        nint modelInfo,
        nint layout);

    [DllImport(LibraryName, EntryPoint = "ov_preprocess_prepostprocessor_build", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus PrePostProcessorBuild(
        nint preprocessor,
        out nint model);

    [DllImport(LibraryName, EntryPoint = "ov_core_compile_model_with_context", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus CoreCompileModelWithContext(
        nint core,
        nint model,
        nint context,
        nuint argumentCount,
        out nint compiledModel);

    [DllImport(LibraryName, EntryPoint = "ov_compiled_model_create_infer_request", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus CompiledModelCreateInferRequest(
        nint compiledModel,
        out nint inferRequest);

    [DllImport(LibraryName, EntryPoint = "ov_model_const_input_by_index", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus ModelGetInputByIndex(
        nint model,
        nuint index,
        out nint port);

    [DllImport(LibraryName, EntryPoint = "ov_port_get_any_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus PortGetAnyName(
        nint port,
        out nint name);

    [DllImport(LibraryName, EntryPoint = "ov_infer_request_set_tensor", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus InferRequestSetTensor(
        nint inferRequest,
        nint tensorName,
        nint tensor);

    [DllImport(LibraryName, EntryPoint = "ov_infer_request_infer", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus InferRequestInfer(nint inferRequest);

    [DllImport(LibraryName, EntryPoint = "ov_infer_request_get_output_tensor_by_index", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus InferRequestGetOutputByIndex(
        nint inferRequest,
        nuint index,
        out nint tensor);

    [DllImport(LibraryName, EntryPoint = "ov_tensor_get_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus TensorGetSize(
        nint tensor,
        out nuint elementCount);

    [DllImport(LibraryName, EntryPoint = "ov_tensor_data", CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus TensorGetData(
        nint tensor,
        out nint data);

    [DllImport(LibraryName, EntryPoint = "ov_tensor_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void TensorFree(nint tensor);

    [DllImport(LibraryName, EntryPoint = "ov_remote_context_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void RemoteContextFree(nint context);

    [DllImport(LibraryName, EntryPoint = "ov_get_last_err_msg", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint GetLastErrorMessage();

    [DllImport(LibraryName, EntryPoint = "ov_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Free(nint value);

    [DllImport(LibraryName, EntryPoint = "ov_output_const_port_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void OutputPortFree(nint port);

    [DllImport(LibraryName, EntryPoint = "ov_infer_request_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void InferRequestFree(nint inferRequest);

    [DllImport(LibraryName, EntryPoint = "ov_compiled_model_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CompiledModelFree(nint compiledModel);

    [DllImport(LibraryName, EntryPoint = "ov_model_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ModelFree(nint model);

    [DllImport(LibraryName, EntryPoint = "ov_layout_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void LayoutFree(nint layout);

    [DllImport(LibraryName, EntryPoint = "ov_preprocess_input_model_info_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void PreprocessModelInfoFree(nint modelInfo);

    [DllImport(LibraryName, EntryPoint = "ov_preprocess_preprocess_steps_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void PreprocessStepsFree(nint preprocessSteps);

    [DllImport(LibraryName, EntryPoint = "ov_preprocess_input_tensor_info_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void PreprocessTensorInfoFree(nint tensorInfo);

    [DllImport(LibraryName, EntryPoint = "ov_preprocess_input_info_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void PreprocessInputInfoFree(nint inputInfo);

    [DllImport(LibraryName, EntryPoint = "ov_preprocess_prepostprocessor_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void PrePostProcessorFree(nint preprocessor);

    private static nint ResolveLibrary(
        string libraryName,
        System.Reflection.Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        if (!string.Equals(
                libraryName,
                LibraryName,
                StringComparison.Ordinal))
        {
            return 0;
        }

        string? runtimeDirectory =
            Environment.GetEnvironmentVariable("OPENVINO_RUNTIME_DIR");
        if (string.IsNullOrWhiteSpace(runtimeDirectory))
        {
            return 0;
        }

        string tbbPath = Path.GetFullPath(
            Path.Combine(
                runtimeDirectory,
                "..",
                "..",
                "..",
                "3rdparty",
                "tbb",
                "bin",
                "tbb12.dll"));
        NativeLibrary.Load(tbbPath);

        string openVinoPath = Path.Combine(
            runtimeDirectory,
            "openvino.dll");
        NativeLibrary.Load(openVinoPath);

        string libraryPath = Path.Combine(runtimeDirectory, "openvino_c.dll");
        return NativeLibrary.Load(libraryPath);
    }
}
