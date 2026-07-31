using System.Runtime.InteropServices;

namespace YoloObjectSearch;

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

internal enum OvPreprocessResizeAlgorithm : uint
{
    Linear = 0
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

    [DllImport(
        LibraryName,
        EntryPoint = "ov_core_create",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus CoreCreate(out nint core);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_core_free",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CoreFree(nint core);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_core_read_model",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus CoreReadModel(
        nint core,
        nint modelPath,
        nint weightsPath,
        out nint model);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_core_create_context",
        CallingConvention = CallingConvention.Cdecl)]
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

    [DllImport(
        LibraryName,
        EntryPoint = "ov_remote_context_create_tensor",
        CallingConvention = CallingConvention.Cdecl)]
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

    [DllImport(
        LibraryName,
        EntryPoint = "ov_preprocess_prepostprocessor_create",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus PrePostProcessorCreate(
        nint model,
        out nint preprocessor);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_preprocess_prepostprocessor_get_input_info",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus PrePostProcessorGetInputInfo(
        nint preprocessor,
        out nint inputInfo);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_preprocess_input_info_get_tensor_info",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus PreprocessInputInfoGetTensorInfo(
        nint inputInfo,
        out nint tensorInfo);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_preprocess_input_tensor_info_set_element_type",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus PreprocessTensorInfoSetElementType(
        nint tensorInfo,
        OvElementType elementType);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_preprocess_input_tensor_info_set_color_format_with_subname",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus PreprocessTensorInfoSetNv12TwoPlanes(
        nint tensorInfo,
        OvColorFormat colorFormat,
        nuint subnameCount,
        nint firstSubname,
        nint secondSubname);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_preprocess_input_tensor_info_set_memory_type",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus PreprocessTensorInfoSetMemoryType(
        nint tensorInfo,
        nint memoryType);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_preprocess_input_tensor_info_set_spatial_static_shape",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus
        PreprocessTensorInfoSetSpatialStaticShape(
            nint tensorInfo,
            nuint height,
            nuint width);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_preprocess_input_info_get_preprocess_steps",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus PreprocessInputInfoGetSteps(
        nint inputInfo,
        out nint preprocessSteps);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_preprocess_preprocess_steps_convert_color",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus PreprocessStepsConvertColor(
        nint preprocessSteps,
        OvColorFormat colorFormat);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_preprocess_preprocess_steps_resize",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus PreprocessStepsResize(
        nint preprocessSteps,
        OvPreprocessResizeAlgorithm algorithm);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_preprocess_input_info_get_model_info",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus PreprocessInputInfoGetModelInfo(
        nint inputInfo,
        out nint modelInfo);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_layout_create",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus LayoutCreate(
        nint description,
        out nint layout);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_preprocess_input_model_info_set_layout",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus PreprocessModelInfoSetLayout(
        nint modelInfo,
        nint layout);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_preprocess_prepostprocessor_build",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus PrePostProcessorBuild(
        nint preprocessor,
        out nint model);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_core_compile_model_with_context",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus CoreCompileModelWithContext(
        nint core,
        nint model,
        nint context,
        nuint argumentCount,
        out nint compiledModel);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_compiled_model_create_infer_request",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus CompiledModelCreateInferRequest(
        nint compiledModel,
        out nint inferRequest);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_model_const_input_by_index",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus ModelGetInputByIndex(
        nint model,
        nuint index,
        out nint port);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_port_get_any_name",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus PortGetAnyName(
        nint port,
        out nint name);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_infer_request_set_tensor",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus InferRequestSetTensor(
        nint inferRequest,
        nint tensorName,
        nint tensor);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_infer_request_infer",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus InferRequestInfer(nint inferRequest);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_infer_request_get_output_tensor_by_index",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus InferRequestGetOutputByIndex(
        nint inferRequest,
        nuint index,
        out nint tensor);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_tensor_get_size",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus TensorGetSize(
        nint tensor,
        out nuint elementCount);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_tensor_data",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern OvStatus TensorGetData(
        nint tensor,
        out nint data);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_tensor_free",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void TensorFree(nint tensor);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_remote_context_free",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void RemoteContextFree(nint context);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_get_last_err_msg",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint GetLastErrorMessage();

    [DllImport(
        LibraryName,
        EntryPoint = "ov_free",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Free(nint value);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_output_const_port_free",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void OutputPortFree(nint port);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_infer_request_free",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void InferRequestFree(nint inferRequest);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_compiled_model_free",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CompiledModelFree(nint compiledModel);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_model_free",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ModelFree(nint model);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_layout_free",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void LayoutFree(nint layout);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_preprocess_input_model_info_free",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void PreprocessModelInfoFree(nint modelInfo);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_preprocess_preprocess_steps_free",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void PreprocessStepsFree(nint preprocessSteps);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_preprocess_input_tensor_info_free",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void PreprocessTensorInfoFree(nint tensorInfo);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_preprocess_input_info_free",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void PreprocessInputInfoFree(nint inputInfo);

    [DllImport(
        LibraryName,
        EntryPoint = "ov_preprocess_prepostprocessor_free",
        CallingConvention = CallingConvention.Cdecl)]
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

        string flatTbbPath = Path.Combine(
            runtimeDirectory,
            "tbb12.dll");
        string tbbPath = File.Exists(flatTbbPath)
            ? flatTbbPath
            : Path.GetFullPath(
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
        NativeLibrary.Load(Path.Combine(
            runtimeDirectory,
            "openvino.dll"));
        return NativeLibrary.Load(Path.Combine(
            runtimeDirectory,
            "openvino_c.dll"));
    }
}
