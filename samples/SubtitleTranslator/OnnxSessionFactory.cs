using Microsoft.ML.OnnxRuntime;

namespace SubtitleTranslator;

public static class OnnxSessionFactory
{
    public static SessionOptions Create(string providerId, int intraOpThreads)
    {
        if (intraOpThreads <= 0)
            throw new ArgumentOutOfRangeException(nameof(intraOpThreads));

        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            InterOpNumThreads = 1,
            IntraOpNumThreads = Math.Clamp(intraOpThreads, 1, Environment.ProcessorCount)
        };
        switch (providerId.Trim().ToLowerInvariant())
        {
            case "cpu":
                return options;
            case "directml":
#if ORT_DIRECTML
                // DirectML requires sequential execution and memory-pattern
                // optimization disabled.
                options.EnableMemoryPattern = false;
                options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                options.AppendExecutionProvider_DML(0);
                return options;
#else
                options.Dispose();
                throw new InvalidOperationException(
                    "The DirectML provider requires the DirectML worker package.");
#endif
            case "openvino":
#if ORT_OPENVINO
                options.AppendExecutionProvider_OpenVINO("GPU_FP32");
                return options;
#else
                options.Dispose();
                throw new InvalidOperationException(
                    "The OpenVINO provider requires the OpenVINO worker package.");
#endif
            default:
                options.Dispose();
                throw new InvalidOperationException($"Unsupported ONNX provider '{providerId}'.");
        }
    }

    public static SessionOptions CreateCpuOptions(int intraOpThreads) =>
        Create("cpu", intraOpThreads);
}
