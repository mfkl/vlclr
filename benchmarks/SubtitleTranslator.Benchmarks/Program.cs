using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SubtitleTranslator;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine(
        "Usage: SubtitleTranslator.Benchmarks <model-dir> [output-dir] " +
        "[--iterations N] [--threads 1,2,4,6,8] [--decoders cached,uncached]");
    return args.Length == 0 ? 1 : 0;
}

string modelDirectory = Path.GetFullPath(args[0]);
string outputDirectory = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal)
    ? Path.GetFullPath(args[1])
    : Path.GetFullPath(Path.Combine("benchmarks", "results"));
int iterations = ParseIntOption(args, "--iterations", 3, 1, 100);
int[] threadCounts = ParseListOption(args, "--threads", "1,2,4,6,8")
    .Select(value => int.Parse(value))
    .Where(value => value > 0)
    .Distinct()
    .ToArray();
string[] decoders = ParseListOption(args, "--decoders", "cached,hybrid,uncached")
    .Select(value => value.ToLowerInvariant())
    .Where(value => value is "cached" or "hybrid" or "uncached")
    .Distinct()
    .ToArray();
if (threadCounts.Length == 0 || decoders.Length == 0)
{
    Console.Error.WriteLine("At least one valid thread count and decoder mode is required.");
    return 1;
}

string pairDirectory = ModelManifest.ResolveModelDirectory(modelDirectory, "en", "fr");
ModelManifest manifest = ModelManifest.LoadAndValidate(pairDirectory, "en", "fr");
var report = new BenchmarkReport
{
    TimestampUtc = DateTimeOffset.UtcNow,
    MachineName = Environment.MachineName,
    OperatingSystem = Environment.OSVersion.ToString(),
    Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    Processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unknown",
    LogicalProcessors = Environment.ProcessorCount,
    PowerMode = "not-recorded",
    OnnxRuntimeVersion = typeof(Microsoft.ML.OnnxRuntime.InferenceSession)
        .Assembly.GetName().Version?.ToString() ?? "unknown",
    ModelFamily = manifest.ModelFamily,
    ModelFiles = manifest.Files.Select(file => new BenchmarkModelFile
    {
        Role = file.Role,
        FileName = file.FileName,
        Size = file.Size,
        Sha256 = file.Sha256
    }).ToList(),
    Iterations = iterations,
    RenderingBaseline = "benchmarks/results/imagesharp-optimized-38b06db.json"
};

BenchmarkCue[] corpus =
[
    new("very-short", "Hello"),
    new("very-short", "Wait!"),
    new("typical", "The cat is on the table"),
    new("typical", "Thank you very much"),
    new("typical", "I can't find my keys anywhere."),
    new("multiline", "SPEAKER:\nWhere are you going?"),
    new("punctuation", "Numbers: 12, 34.5, and 2026."),
    new("unicode", "Café déjà vu — voilà!"),
    new("unicode", "Emoji 😀 and unknown symbols ∑."),
    new("long", "This is a longer subtitle cue designed to measure translation latency when a complete sentence contains several clauses and more context."),
    new("long", "Although the weather changed unexpectedly, everyone stayed until the final scene because they wanted to know how the story would end."),
    new("adversarial", string.Join(' ', Enumerable.Repeat("subtitle", 48)))
];

foreach (int threads in threadCounts)
{
    foreach (string decoder in decoders)
    {
        bool useCache = decoder != "uncached";
        Console.WriteLine($"Running decoder={decoder}, threads={threads}, iterations={iterations}...");
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        long initializationStarted = Stopwatch.GetTimestamp();
        using var engine = new OnnxTranslator(
            pairDirectory,
            "en",
            "fr",
            new OnnxTranslatorOptions
            {
                IntraOpThreads = threads,
                UseDecoderCache = useCache,
                CacheActivationTokenCount = decoder == "cached" ? 1 : 32,
                MaximumSourceTokens = manifest.MaximumSourceTokens,
                MaximumOutputTokens = manifest.MaximumOutputTokens,
                VerifyModelHashes = true
            });
        double initializationMilliseconds = Stopwatch.GetElapsedTime(initializationStarted).TotalMilliseconds;

        _ = engine.Translate("Hello");
        var samples = new List<BenchmarkSample>(iterations * corpus.Length);
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            foreach (BenchmarkCue cue in corpus)
            {
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                long started = Stopwatch.GetTimestamp();
                TranslationResult result = engine.TranslateDetailed(cue.Text);
                double totalMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                samples.Add(new BenchmarkSample
                {
                    Iteration = iteration + 1,
                    Group = cue.Group,
                    CueHash = TranslationTextNormalizer.ComputeCueHash(cue.Text),
                    SourceTokens = result.SourceTokenCount,
                    OutputTokens = result.OutputTokenCount,
                    TokenizeMilliseconds = result.TokenizeDuration.TotalMilliseconds,
                    EncoderMilliseconds = result.EncoderDuration.TotalMilliseconds,
                    DecoderMilliseconds = result.DecoderDuration.TotalMilliseconds,
                    DetokenizeMilliseconds = result.DetokenizeDuration.TotalMilliseconds,
                    TotalMilliseconds = totalMilliseconds,
                    ManagedAllocatedBytes = allocatedBytes,
                    OutputHash = TranslationTextNormalizer.ComputeCueHash(result.Text)
                });
            }
        }

        var cache = new TranslationCache(8);
        cache.Set(corpus[0].Text, engine.Translate(corpus[0].Text));
        const int cacheIterations = 1000;
        long cacheStarted = Stopwatch.GetTimestamp();
        for (int index = 0; index < cacheIterations; index++)
        {
            if (!cache.TryGet(corpus[0].Text, out _))
                throw new InvalidOperationException("Expected benchmark cache hit.");
        }
        double cacheHitMicroseconds =
            Stopwatch.GetElapsedTime(cacheStarted).TotalMicroseconds / cacheIterations;

        double[] elapsed = samples.Select(sample => sample.TotalMilliseconds).Order().ToArray();
        long[] allocated = samples.Select(sample => sample.ManagedAllocatedBytes).Order().ToArray();
        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        report.Scenarios.Add(new BenchmarkScenario
        {
            Provider = "cpu",
            Api = "OrtValue",
            Decoder = decoder,
            IntraOpThreads = threads,
            InitializationMilliseconds = initializationMilliseconds,
            CacheHitMicroseconds = cacheHitMicroseconds,
            P50Milliseconds = Percentile(elapsed, 0.50),
            P90Milliseconds = Percentile(elapsed, 0.90),
            P95Milliseconds = Percentile(elapsed, 0.95),
            P99Milliseconds = Percentile(elapsed, 0.99),
            MaximumMilliseconds = elapsed[^1],
            MedianManagedAllocatedBytes = (long)Percentile(allocated.Select(value => (double)value).ToArray(), 0.50),
            WorkingSetBytes = process.WorkingSet64,
            PrivateBytes = process.PrivateMemorySize64,
            Groups = samples
                .GroupBy(sample => sample.Group, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group =>
                {
                    double[] groupElapsed = group.Select(sample => sample.TotalMilliseconds).Order().ToArray();
                    return new BenchmarkGroupSummary
                    {
                        Group = group.Key,
                        SampleCount = groupElapsed.Length,
                        AverageMilliseconds = groupElapsed.Average(),
                        P50Milliseconds = Percentile(groupElapsed, 0.50),
                        P95Milliseconds = Percentile(groupElapsed, 0.95),
                        MaximumMilliseconds = groupElapsed[^1]
                    };
                })
                .ToList(),
            Samples = samples
        });
    }
}

Directory.CreateDirectory(outputDirectory);
string stamp = report.TimestampUtc.ToString("yyyyMMdd-HHmmss");
string jsonPath = Path.Combine(outputDirectory, $"subtitle-translator-{stamp}.json");
string markdownPath = Path.Combine(outputDirectory, $"subtitle-translator-{stamp}.md");
string json = JsonSerializer.Serialize(report, BenchmarkJsonContext.Default.BenchmarkReport);
File.WriteAllText(jsonPath, json);
File.WriteAllText(markdownPath, BuildMarkdown(report));
Console.WriteLine($"JSON: {jsonPath}");
Console.WriteLine($"Markdown: {markdownPath}");
return 0;

static int ParseIntOption(string[] arguments, string option, int fallback, int minimum, int maximum)
{
    int index = Array.IndexOf(arguments, option);
    if (index < 0)
        return fallback;
    if (index + 1 >= arguments.Length || !int.TryParse(arguments[index + 1], out int value))
        throw new ArgumentException($"{option} requires an integer value.");
    return Math.Clamp(value, minimum, maximum);
}

static string[] ParseListOption(string[] arguments, string option, string fallback)
{
    int index = Array.IndexOf(arguments, option);
    string value = index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : fallback;
    return value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

static double Percentile(double[] sortedValues, double percentile)
{
    if (sortedValues.Length == 0)
        return 0;
    int index = Math.Clamp((int)Math.Ceiling(percentile * sortedValues.Length) - 1, 0, sortedValues.Length - 1);
    return sortedValues[index];
}

static string BuildMarkdown(BenchmarkReport report)
{
    var builder = new StringBuilder();
    builder.AppendLine("# Subtitle translator benchmark");
    builder.AppendLine();
    builder.AppendLine($"- Timestamp (UTC): {report.TimestampUtc:O}");
    builder.AppendLine($"- Processor: {report.Processor}");
    builder.AppendLine($"- Logical processors: {report.LogicalProcessors}");
    builder.AppendLine($"- OS: {report.OperatingSystem}");
    builder.AppendLine($"- Framework: {report.Framework}");
    builder.AppendLine($"- ONNX Runtime: {report.OnnxRuntimeVersion}");
    builder.AppendLine($"- Iterations per cue: {report.Iterations}");
    builder.AppendLine($"- Rendering baseline (separate): `{report.RenderingBaseline}`");
    builder.AppendLine();
    builder.AppendLine("| Decoder | Threads | Init ms | p50 ms | p90 ms | p95 ms | p99 ms | Max ms | Median alloc | Cache hit | Working set |");
    builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
    foreach (BenchmarkScenario scenario in report.Scenarios)
    {
        builder.AppendLine(
            $"| {scenario.Decoder} | {scenario.IntraOpThreads} | {scenario.InitializationMilliseconds:F1} | " +
            $"{scenario.P50Milliseconds:F1} | {scenario.P90Milliseconds:F1} | {scenario.P95Milliseconds:F1} | " +
            $"{scenario.P99Milliseconds:F1} | {scenario.MaximumMilliseconds:F1} | " +
            $"{scenario.MedianManagedAllocatedBytes / 1024.0:F1} KiB | {scenario.CacheHitMicroseconds:F2} µs | " +
            $"{scenario.WorkingSetBytes / 1024.0 / 1024.0:F1} MiB |");
    }
    builder.AppendLine();
    builder.AppendLine("## Corpus groups");
    builder.AppendLine();
    builder.AppendLine("| Decoder | Threads | Group | Samples | Average ms | p50 ms | p95 ms | Max ms |");
    builder.AppendLine("|---|---:|---|---:|---:|---:|---:|---:|");
    foreach (BenchmarkScenario scenario in report.Scenarios)
    {
        foreach (BenchmarkGroupSummary group in scenario.Groups)
        {
            builder.AppendLine(
                $"| {scenario.Decoder} | {scenario.IntraOpThreads} | {group.Group} | {group.SampleCount} | " +
                $"{group.AverageMilliseconds:F1} | {group.P50Milliseconds:F1} | " +
                $"{group.P95Milliseconds:F1} | {group.MaximumMilliseconds:F1} |");
        }
    }
    return builder.ToString();
}

internal sealed record BenchmarkCue(string Group, string Text);

public sealed class BenchmarkReport
{
    public DateTimeOffset TimestampUtc { get; init; }
    public string MachineName { get; init; } = "";
    public string OperatingSystem { get; init; } = "";
    public string Framework { get; init; } = "";
    public string Processor { get; init; } = "";
    public int LogicalProcessors { get; init; }
    public string PowerMode { get; init; } = "";
    public string OnnxRuntimeVersion { get; init; } = "";
    public string ModelFamily { get; init; } = "";
    public List<BenchmarkModelFile> ModelFiles { get; init; } = [];
    public int Iterations { get; init; }
    public string RenderingBaseline { get; init; } = "";
    public List<BenchmarkScenario> Scenarios { get; init; } = [];
}

public sealed class BenchmarkModelFile
{
    public string Role { get; init; } = "";
    public string FileName { get; init; } = "";
    public long Size { get; init; }
    public string Sha256 { get; init; } = "";
}

public sealed class BenchmarkScenario
{
    public string Provider { get; init; } = "";
    public string Api { get; init; } = "";
    public string Decoder { get; init; } = "";
    public int IntraOpThreads { get; init; }
    public double InitializationMilliseconds { get; init; }
    public double CacheHitMicroseconds { get; init; }
    public double P50Milliseconds { get; init; }
    public double P90Milliseconds { get; init; }
    public double P95Milliseconds { get; init; }
    public double P99Milliseconds { get; init; }
    public double MaximumMilliseconds { get; init; }
    public long MedianManagedAllocatedBytes { get; init; }
    public long WorkingSetBytes { get; init; }
    public long PrivateBytes { get; init; }
    public List<BenchmarkGroupSummary> Groups { get; init; } = [];
    public List<BenchmarkSample> Samples { get; init; } = [];
}

public sealed class BenchmarkGroupSummary
{
    public string Group { get; init; } = "";
    public int SampleCount { get; init; }
    public double AverageMilliseconds { get; init; }
    public double P50Milliseconds { get; init; }
    public double P95Milliseconds { get; init; }
    public double MaximumMilliseconds { get; init; }
}

public sealed class BenchmarkSample
{
    public int Iteration { get; init; }
    public string Group { get; init; } = "";
    public string CueHash { get; init; } = "";
    public int SourceTokens { get; init; }
    public int OutputTokens { get; init; }
    public double TokenizeMilliseconds { get; init; }
    public double EncoderMilliseconds { get; init; }
    public double DecoderMilliseconds { get; init; }
    public double DetokenizeMilliseconds { get; init; }
    public double TotalMilliseconds { get; init; }
    public long ManagedAllocatedBytes { get; init; }
    public string OutputHash { get; init; } = "";
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(BenchmarkReport))]
internal partial class BenchmarkJsonContext : JsonSerializerContext;
