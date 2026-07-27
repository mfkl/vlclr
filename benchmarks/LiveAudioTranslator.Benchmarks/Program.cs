using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("The live translation playback benchmark requires Windows.");
    return 2;
}

BenchmarkOptions options = BenchmarkOptions.Parse(args);
Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)!);
var results = new List<PlaybackBenchmarkResult>();
foreach (BenchmarkSample sample in options.Samples)
{
    Console.WriteLine(
        $"event=benchmark_sample name={sample.Name} duration_seconds={sample.DurationSeconds}");
    results.Add(await RunSampleAsync(options, sample));
}

var report = new
{
    formatVersion = 1,
    createdUtc = DateTimeOffset.UtcNow,
    machine = new
    {
        os = Environment.OSVersion.VersionString,
        cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
        logicalProcessors = Environment.ProcessorCount,
        gpuCountersAvailable = false
    },
    configuration = new
    {
        mode = "live-immediate",
        speechModel = "whisper-tiny-multilingual",
        translationModel = "opus-mt-en-fr",
        speechProvider = options.SpeechProvider,
        speechDevice = options.SpeechDevice,
        translationProvider = options.TranslationProvider,
        inputDelayMilliseconds = 0,
        hardwareVideoDecoding = options.SpeechDevice == "cpu"
    },
    samples = results,
    thresholds = new
    {
        maximumTotalRealTimeFactor = 0.75,
        maximumSchedulerP95Milliseconds = 150,
        minimumAcceleratedImprovement = 0.20,
        qualityCorpusRequired = true
    }
};
await File.WriteAllTextAsync(
    options.OutputPath,
    JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"event=benchmark_complete output={options.OutputPath}");
return results.All(result => result.ExitCode == 0) ? 0 : 1;

static async Task<PlaybackBenchmarkResult> RunSampleAsync(
    BenchmarkOptions options,
    BenchmarkSample sample)
{
    var start = new ProcessStartInfo(options.RunnerPath)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    foreach (string argument in new[]
             {
                 "--mode", "live-immediate",
                 "--vlc-root", options.VlcRoot,
                 "--worker", options.WorkerPath,
                 "--cpu-worker", options.CpuWorkerPath,
                 "--catalog", options.CatalogPath,
                 "--speech-provider", options.SpeechProvider,
                 "--speech-device", options.SpeechDevice,
                 "--translation-provider", options.TranslationProvider,
                 sample.MediaPath,
                 "--",
                 "--play-and-exit",
                 $"--stop-time={sample.DurationSeconds}",
                 "-vvv"
             })
    {
        start.ArgumentList.Add(argument);
    }

    using Process process = Process.Start(start)
        ?? throw new InvalidOperationException("Could not start live translation runner.");
    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
    Task<string> stderr = process.StandardError.ReadToEndAsync();
    var timer = Stopwatch.StartNew();
    TimeSpan previousCpu = TimeSpan.Zero;
    DateTime previousSample = DateTime.UtcNow;
    double peakCpuPercent = 0;
    long peakPrivateBytes = 0;
    while (!process.HasExited)
    {
        await Task.Delay(1_000);
        Process[] tree = ProcessTree.Get(process.Id);
        TimeSpan cpu = TimeSpan.Zero;
        foreach (Process child in tree)
        {
            try
            {
                cpu += child.TotalProcessorTime;
                peakPrivateBytes = Math.Max(peakPrivateBytes, child.PrivateMemorySize64);
            }
            catch
            {
            }
            finally
            {
                child.Dispose();
            }
        }
        DateTime now = DateTime.UtcNow;
        double wallSeconds = Math.Max(0.001, (now - previousSample).TotalSeconds);
        double cpuPercent =
            (cpu - previousCpu).TotalSeconds / (wallSeconds * Environment.ProcessorCount) * 100;
        peakCpuPercent = Math.Max(peakCpuPercent, Math.Max(0, cpuPercent));
        previousCpu = cpu;
        previousSample = now;
    }
    await process.WaitForExitAsync();
    timer.Stop();
    string logs = await stdout + Environment.NewLine + await stderr;
    string safeMetrics = string.Join(
        '\n',
        logs.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line =>
                line.Contains("event=worker_metrics", StringComparison.Ordinal) ||
                line.Contains("event=clock_", StringComparison.Ordinal) ||
                line.Contains("late picture", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("dropped", StringComparison.OrdinalIgnoreCase)));
    string hash = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(safeMetrics))).ToLowerInvariant();
    return new PlaybackBenchmarkResult
    {
        Name = sample.Name,
        RequestedDurationSeconds = sample.DurationSeconds,
        WallDurationSeconds = timer.Elapsed.TotalSeconds,
        ExitCode = process.ExitCode,
        WorkerStartupMilliseconds = ReadLastLong(logs, "init_ms"),
        WorkerWarmupMilliseconds = ReadLastLong(logs, "warmup_ms"),
        TotalRealTimeFactor = ReadLastDouble(logs, "total_rtf"),
        RollingRealTimeFactor = ReadLastDouble(logs, "rolling_rtf"),
        CueLatencyP50Ticks = ReadLastLong(logs, "cue_p50"),
        CueLatencyP95Ticks = ReadLastLong(logs, "cue_p95"),
        CueLatencyP99Ticks = ReadLastLong(logs, "cue_p99"),
        DecodeLeadTicks = ReadLastLong(logs, "decode_lead"),
        QueueDrops = ReadLastLong(logs, "dropped_audio"),
        StaleCompletions = ReadLastLong(logs, "stale_completions"),
        WorkerRestarts = ReadLastLong(logs, "worker_restarts"),
        VlcLatePictureLogCount = Regex.Matches(logs, "late picture", RegexOptions.IgnoreCase).Count,
        VlcDroppedFrameLogCount = Regex.Matches(logs, "dropped frame", RegexOptions.IgnoreCase).Count,
        PeakCpuPercent = peakCpuPercent,
        PeakPrivateBytes = peakPrivateBytes,
        GpuUtilizationPercent = null,
        PeakGpuMemoryBytes = null,
        OutputMetricsHash = hash,
        QualityCorpusScore = null,
        QualityAccepted = false
    };
}

static long ReadLastLong(string logs, string name)
{
    MatchCollection matches = Regex.Matches(logs, $@"\b{Regex.Escape(name)}=(-?\d+)");
    return matches.Count == 0 ? 0 : long.Parse(matches[^1].Groups[1].Value);
}

static double ReadLastDouble(string logs, string name)
{
    MatchCollection matches = Regex.Matches(
        logs,
        $@"\b{Regex.Escape(name)}=(-?\d+(?:\.\d+)?)");
    return matches.Count == 0
        ? 0
        : double.Parse(
            matches[^1].Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture);
}

internal sealed record BenchmarkOptions
{
    public required string VlcRoot { get; init; }
    public required string RunnerPath { get; init; }
    public required string WorkerPath { get; init; }
    public required string CpuWorkerPath { get; init; }
    public required string CatalogPath { get; init; }
    public required string OutputPath { get; init; }
    public required string SpeechProvider { get; init; }
    public required string SpeechDevice { get; init; }
    public required string TranslationProvider { get; init; }
    public required IReadOnlyList<BenchmarkSample> Samples { get; init; }
    public int DelayMilliseconds { get; init; }

    public static BenchmarkOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
                throw new ArgumentException($"Invalid benchmark argument '{args[index]}'.");
            values[args[index][2..]] = args[++index];
        }
        string root = FindRepositoryRoot();
        string worker = values.GetValueOrDefault(
            "worker",
            Path.Combine(
                root,
                "samples",
                "LiveAudioTranslator.Worker",
                "bin",
                "Release",
                "net10.0",
                "win-x64",
                "publish",
                "LiveAudioTranslator.Worker.exe"));
        string runner = values.GetValueOrDefault(
            "runner",
            Path.Combine(
                root,
                "samples",
                "LiveAudioTranslator.Runner",
                "bin",
                "Release",
                "net10.0",
                "win-x64",
                "LiveAudioTranslator.Runner.exe"));
        string startup = Require(values, "startup-media");
        string sustained = values.GetValueOrDefault("sustained-media", startup);
        string stability = values.GetValueOrDefault("stability-media", sustained);
        string stress = values.GetValueOrDefault("stress-media", startup);
        return new BenchmarkOptions
        {
            VlcRoot = Path.GetFullPath(Require(values, "vlc-root")),
            RunnerPath = Path.GetFullPath(runner),
            WorkerPath = Path.GetFullPath(worker),
            CpuWorkerPath = Path.GetFullPath(values.GetValueOrDefault("cpu-worker", worker)),
            CatalogPath = Path.GetFullPath(values.GetValueOrDefault(
                "catalog",
                Path.Combine(Path.GetDirectoryName(worker)!, "models", "model-profiles.json"))),
            OutputPath = Path.GetFullPath(values.GetValueOrDefault(
                "output",
                Path.Combine(root, "artifacts", "live-immediate", "performance.json"))),
            SpeechProvider = values.GetValueOrDefault("speech-provider", "auto"),
            SpeechDevice = ParseSpeechDevice(values.GetValueOrDefault("speech-device", "cpu")),
            TranslationProvider = values.GetValueOrDefault("translation-provider", "auto"),
            DelayMilliseconds = values.TryGetValue("delay-ms", out string? delay)
                ? Math.Clamp(int.Parse(delay), 8_000, 60_000)
                : 15_000,
            Samples =
            [
                new(
                    "startup-latency",
                    Path.GetFullPath(startup),
                    ParseSeconds(values, "startup-seconds", 120)),
                new(
                    "sustained",
                    Path.GetFullPath(sustained),
                    ParseSeconds(values, "sustained-seconds", 600)),
                new(
                    "stability",
                    Path.GetFullPath(stability),
                    ParseSeconds(values, "stability-seconds", 1_800)),
                new(
                    "continuous-speech-stress",
                    Path.GetFullPath(stress),
                    ParseSeconds(values, "stress-seconds", 120))
            ]
        };
    }

    private static int ParseSeconds(
        IReadOnlyDictionary<string, string> values,
        string key,
        int fallback) =>
        values.TryGetValue(key, out string? value)
            ? Math.Clamp(int.Parse(value), 5, 7_200)
            : fallback;

    private static string ParseSpeechDevice(string value)
    {
        string normalized = value.Trim().ToLowerInvariant();
        return normalized is "cpu" or "gpu" or "auto"
            ? normalized
            : throw new ArgumentException(
                "--speech-device must be cpu, gpu, or auto.");
    }

    private static string Require(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"--{key} is required.");

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "vlclr.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate vlclr.sln.");
    }
}

internal sealed record BenchmarkSample(string Name, string MediaPath, int DurationSeconds);

internal sealed record PlaybackBenchmarkResult
{
    public required string Name { get; init; }
    public int RequestedDurationSeconds { get; init; }
    public double WallDurationSeconds { get; init; }
    public int ExitCode { get; init; }
    public long WorkerStartupMilliseconds { get; init; }
    public long WorkerWarmupMilliseconds { get; init; }
    public double TotalRealTimeFactor { get; init; }
    public double RollingRealTimeFactor { get; init; }
    public long CueLatencyP50Ticks { get; init; }
    public long CueLatencyP95Ticks { get; init; }
    public long CueLatencyP99Ticks { get; init; }
    public long DecodeLeadTicks { get; init; }
    public long QueueDrops { get; init; }
    public long StaleCompletions { get; init; }
    public long WorkerRestarts { get; init; }
    public int VlcLatePictureLogCount { get; init; }
    public int VlcDroppedFrameLogCount { get; init; }
    public double PeakCpuPercent { get; init; }
    public long PeakPrivateBytes { get; init; }
    public double? GpuUtilizationPercent { get; init; }
    public long? PeakGpuMemoryBytes { get; init; }
    public required string OutputMetricsHash { get; init; }
    public double? QualityCorpusScore { get; init; }
    public bool QualityAccepted { get; init; }
}

internal static class ProcessTree
{
    public static Process[] Get(int root)
    {
        var parents = new Dictionary<int, int>();
        nint snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot != -1)
        {
            try
            {
                var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
                if (Process32First(snapshot, ref entry))
                {
                    do
                    {
                        parents[(int)entry.ProcessId] = (int)entry.ParentProcessId;
                        entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
                    }
                    while (Process32Next(snapshot, ref entry));
                }
            }
            finally
            {
                _ = CloseHandle(snapshot);
            }
        }
        var ids = new HashSet<int> { root };
        bool changed;
        do
        {
            changed = false;
            foreach ((int child, int parent) in parents)
            {
                if (ids.Contains(parent) && ids.Add(child))
                    changed = true;
            }
        }
        while (changed);
        return ids.Select(id =>
        {
            try { return Process.GetProcessById(id); }
            catch { return null; }
        }).OfType<Process>().ToArray();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size, Usage, ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId, Threads, ParentProcessId;
        public int PriorityClass;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [DllImport("kernel32")] private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern bool Process32First(nint snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern bool Process32Next(nint snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32")] private static extern bool CloseHandle(nint handle);
}
