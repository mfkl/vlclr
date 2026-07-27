namespace LiveAudioTranslator.Worker;

internal sealed record WorkerCommandLine
{
    public required string PipeName { get; init; }
    public required Guid SessionId { get; init; }
    public required string CatalogPath { get; init; }
    public bool Benchmark { get; init; }
    public string BenchmarkOutputPath { get; init; } = "";
    public int FakeReadyDelayMilliseconds { get; init; }

    public static WorkerCommandLine Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool benchmark = false;
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (argument == "--benchmark")
            {
                benchmark = true;
                continue;
            }
            if (!argument.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
                throw new ArgumentException($"Invalid worker argument '{argument}'.");
            values[argument[2..]] = args[++index];
        }

        string catalog = values.GetValueOrDefault(
            "catalog",
            Path.Combine(AppContext.BaseDirectory, "models", "model-profiles.json"));
        if (benchmark)
        {
            return new WorkerCommandLine
            {
                PipeName = "benchmark",
                SessionId = Guid.NewGuid(),
                CatalogPath = Path.GetFullPath(catalog),
                Benchmark = true,
                BenchmarkOutputPath = Path.GetFullPath(
                    values.GetValueOrDefault("output", "provider-benchmark.json"))
            };
        }

        string pipe = values.GetValueOrDefault("pipe", "");
        if (!IsSafeIdentifier(pipe))
            throw new ArgumentException("A safe --pipe value is required.");
        if (!Guid.TryParse(values.GetValueOrDefault("session", ""), out Guid session) ||
            session == Guid.Empty)
        {
            throw new ArgumentException("A non-empty --session GUID is required.");
        }
        return new WorkerCommandLine
        {
            PipeName = pipe,
            SessionId = session,
            CatalogPath = Path.GetFullPath(catalog),
            Benchmark = false,
            FakeReadyDelayMilliseconds = ParseInt(
                values.GetValueOrDefault("fake-ready-delay-ms", "0"),
                0,
                60_000,
                "--fake-ready-delay-ms")
        };
    }

    private static int ParseInt(string value, int minimum, int maximum, string argument)
    {
        if (!int.TryParse(
                value,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out int parsed) ||
            parsed < minimum ||
            parsed > maximum)
        {
            throw new ArgumentException(
                $"{argument} must be between {minimum} and {maximum} milliseconds.");
        }
        return parsed;
    }

    private static bool IsSafeIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 128 &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}

internal static class WorkerLog
{
    public static string Sanitize(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Replace(' ', '-');
}
