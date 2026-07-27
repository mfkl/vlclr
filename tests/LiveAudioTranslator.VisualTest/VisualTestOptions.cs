namespace LiveAudioTranslator.VisualTest;

internal sealed record VisualTestOptions
{
    public required string VlcRoot { get; init; }
    public required string MediaPath { get; init; }
    public required string WorkerPath { get; init; }
    public required string CatalogPath { get; init; }
    public required string ArtifactsDirectory { get; init; }
    public required string Mode { get; init; }
    public required string Capture { get; init; }
    public required string Transport { get; init; }
    public int DelayMilliseconds { get; init; } = 15_000;

    public bool CaptureSnapshots => Capture is "snapshot" or "both";
    public bool CaptureQt => Capture is "qt" or "both";
    public bool Fake => Mode == "fake";
    public bool UseLoopbackHttp => Transport == "http";

    public static VisualTestOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
                throw new ArgumentException($"Invalid visual-test argument '{args[index]}'.");
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
        string catalog = values.GetValueOrDefault(
            "catalog",
            Path.Combine(Path.GetDirectoryName(worker)!, "models", "model-profiles.json"));
        string mode = values.GetValueOrDefault("mode", "fake").ToLowerInvariant();
        string capture = values.GetValueOrDefault("capture", "both").ToLowerInvariant();
        string transport = values.GetValueOrDefault("transport", "http").ToLowerInvariant();
        if (mode is not ("fake" or "real"))
            throw new ArgumentException("--mode must be fake or real.");
        if (capture is not ("snapshot" or "qt" or "both"))
            throw new ArgumentException("--capture must be snapshot, qt, or both.");
        if (transport is not ("http" or "file"))
            throw new ArgumentException("--transport must be http or file.");
        return new VisualTestOptions
        {
            VlcRoot = Path.GetFullPath(Require(values, "vlc-root")),
            MediaPath = Path.GetFullPath(Require(values, "media")),
            WorkerPath = Path.GetFullPath(worker),
            CatalogPath = Path.GetFullPath(catalog),
            ArtifactsDirectory = Path.GetFullPath(values.GetValueOrDefault(
                "artifacts",
                Path.Combine(root, "artifacts", "live-sync"))),
            Mode = mode,
            Capture = capture,
            Transport = transport,
            DelayMilliseconds = values.TryGetValue("delay-ms", out string? delay) &&
                int.TryParse(delay, out int parsed)
                ? Math.Clamp(parsed, 8_000, 60_000)
                : 15_000
        };
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
