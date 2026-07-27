namespace LiveAudioTranslator.Runner;

internal enum RunnerMode
{
    Prepared,
    LiveImmediate
}

internal sealed record RunnerOptions
{
    public required RunnerMode Mode { get; init; }
    public required string Media { get; init; }
    public required string VlcRoot { get; init; }
    public required string WorkerPath { get; init; }
    public required string CpuWorkerPath { get; init; }
    public required string CatalogPath { get; init; }
    public required string SpeechModelId { get; init; }
    public required string TranslationModelId { get; init; }
    public required string SpeechProviderId { get; init; }
    public required string SpeechDeviceId { get; init; }
    public required string TranslationProviderId { get; init; }
    public required string SourceLanguage { get; init; }
    public required string TargetLanguage { get; init; }
    public required string BenchmarkProfilePath { get; init; }
    public required IReadOnlyList<string> ExtraVlcArguments { get; init; }
    public int InputDelayMilliseconds { get; init; }
    public int MinimumDelayMilliseconds { get; init; } = 8_000;
    public int MaximumDelayMilliseconds { get; init; } = 60_000;
    public int SafetyMarginMilliseconds { get; init; } = 2_000;
    public bool FakeInference { get; init; }
    public int FakeReadyDelayMilliseconds { get; init; }

    public static RunnerOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var extra = new List<string>();
        string media = "";
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (argument == "--")
            {
                extra.AddRange(args[(index + 1)..]);
                break;
            }
            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                string key = argument[2..];
                if (key == "fake")
                {
                    values[key] = "true";
                    continue;
                }
                if (index + 1 >= args.Length)
                    throw new ArgumentException($"Missing value for '{argument}'.");
                values[key] = args[++index];
            }
            else if (media.Length == 0)
            {
                media = argument;
            }
            else
            {
                extra.Add(argument);
            }
        }
        if (media.Length == 0)
            throw new ArgumentException("A media path or URL is required.");

        string repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        string workerDefault = Path.Combine(
            repositoryRoot,
            "samples",
            "LiveAudioTranslator.Worker",
            "bin",
            "Release",
            "net10.0",
            "win-x64",
            "publish",
            "LiveAudioTranslator.Worker.exe");
        string catalogDefault = Path.Combine(
            Path.GetDirectoryName(workerDefault)!,
            "models",
            "model-profiles.json");
        string mode = values.GetValueOrDefault("mode", "live-immediate");
        return new RunnerOptions
        {
            Mode = mode switch
            {
                "prepared" => RunnerMode.Prepared,
                "live-immediate" => RunnerMode.LiveImmediate,
                _ => throw new ArgumentException($"Unknown mode '{mode}'.")
            },
            Media = NormalizeMedia(media),
            VlcRoot = Path.GetFullPath(values.GetValueOrDefault(
                "vlc-root",
                Path.Combine(repositoryRoot, "vlc-binaries", "vlc-4.0.0-dev"))),
            WorkerPath = Path.GetFullPath(values.GetValueOrDefault("worker", workerDefault)),
            CpuWorkerPath = Path.GetFullPath(values.GetValueOrDefault("cpu-worker", workerDefault)),
            CatalogPath = Path.GetFullPath(values.GetValueOrDefault("catalog", catalogDefault)),
            SpeechModelId = NormalizeIdentifier(
                values.GetValueOrDefault("speech-model", "whisper-tiny-multilingual")),
            TranslationModelId = NormalizeIdentifier(
                values.GetValueOrDefault("translation-model", "opus-mt-en-fr")),
            SpeechProviderId = NormalizeIdentifier(
                values.GetValueOrDefault("speech-provider", "auto")),
            SpeechDeviceId = NormalizeSpeechDevice(
                values.GetValueOrDefault("speech-device", "cpu")),
            TranslationProviderId = NormalizeIdentifier(
                values.GetValueOrDefault("translation-provider", "auto")),
            SourceLanguage = NormalizeIdentifier(values.GetValueOrDefault("source-language", "auto")),
            TargetLanguage = NormalizeIdentifier(values.GetValueOrDefault("target-language", "fr")),
            InputDelayMilliseconds = ParseInt(values, "delay-ms", 15_000, 8_000, 60_000),
            MinimumDelayMilliseconds = ParseInt(values, "minimum-delay-ms", 8_000, 8_000, 60_000),
            MaximumDelayMilliseconds = ParseInt(values, "maximum-delay-ms", 60_000, 8_000, 60_000),
            SafetyMarginMilliseconds = ParseInt(values, "safety-margin-ms", 2_000, 0, 15_000),
            BenchmarkProfilePath = Path.GetFullPath(values.GetValueOrDefault(
                "benchmark-profile",
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VLCLR",
                    "live-translation-provider-profile.json"))),
            ExtraVlcArguments = extra,
            FakeInference = values.ContainsKey("fake"),
            FakeReadyDelayMilliseconds = ParseInt(
                values,
                "fake-ready-delay-ms",
                0,
                0,
                60_000)
        };
    }

    private static int ParseInt(
        IReadOnlyDictionary<string, string> values,
        string key,
        int fallback,
        int minimum,
        int maximum) =>
        values.TryGetValue(key, out string? value) && int.TryParse(value, out int parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : fallback;

    private static string NormalizeMedia(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
            uri.Scheme is "http" or "https" or "rtsp" or "file")
        {
            return uri.AbsoluteUri;
        }
        return new Uri(Path.GetFullPath(value)).AbsoluteUri;
    }

    private static string NormalizeIdentifier(string value)
    {
        string normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length is 0 or > 128 ||
            normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.')))
        {
            throw new ArgumentException($"Invalid identifier '{value}'.");
        }
        return normalized;
    }

    private static string NormalizeSpeechDevice(string value)
    {
        string normalized = NormalizeIdentifier(value);
        return normalized is "cpu" or "gpu" or "auto"
            ? normalized
            : throw new ArgumentException(
                $"Unknown speech device '{value}'. Use cpu, gpu, or auto.");
    }

    private static string FindRepositoryRoot(string start)
    {
        DirectoryInfo? directory = new(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "vlclr.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        return Directory.GetCurrentDirectory();
    }
}

internal static class RunnerLog
{
    public static string Sanitize(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Replace(' ', '-');
}
