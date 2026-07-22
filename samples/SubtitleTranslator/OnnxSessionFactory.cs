using Microsoft.ML.OnnxRuntime;

namespace SubtitleTranslator;

public static class OnnxSessionFactory
{
    public static SessionOptions CreateCpuOptions(int intraOpThreads)
    {
        if (intraOpThreads <= 0)
            throw new ArgumentOutOfRangeException(nameof(intraOpThreads));

        return new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            InterOpNumThreads = 1,
            IntraOpNumThreads = Math.Clamp(intraOpThreads, 1, Environment.ProcessorCount)
        };
    }
}
