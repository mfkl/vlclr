using System.Diagnostics;

if (args.Length < 2)
{
    Console.Error.WriteLine(
        "Usage: LiveAudioTranslatorIntegrationTest <vlc-path> <video-url> [timeout-seconds] " +
        "[worker-path] [catalog-path]");
    return 1;
}

string repositoryRoot = FindRepositoryRoot();
string configuration = AppContext.BaseDirectory.Contains(
    $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
    StringComparison.OrdinalIgnoreCase)
    ? "Release"
    : "Debug";
string vlcRoot = Path.GetFullPath(args[0]);
string media = args[1];
int timeoutSeconds = args.Length > 2 ? int.Parse(args[2]) : 45;
string workerPath = args.Length > 3
    ? Path.GetFullPath(args[3])
    : Path.Combine(
        repositoryRoot,
        "samples",
        "LiveAudioTranslator.Worker",
        "bin",
        configuration,
        "net10.0",
        "win-x64",
        "LiveAudioTranslator.Worker.exe");
string catalogPath = args.Length > 4
    ? Path.GetFullPath(args[4])
    : Path.Combine(
        repositoryRoot,
        "samples",
        "LiveAudioTranslator.Worker",
        "bin",
        configuration,
        "net10.0",
        "win-x64",
        "models",
        "model-profiles.json");
string runnerPath = Path.Combine(
    repositoryRoot,
    "samples",
    "LiveAudioTranslator.Runner",
    "bin",
    configuration,
    "net10.0",
    "win-x64",
    "LiveAudioTranslator.Runner.exe");
foreach (string required in new[]
         {
             Path.Combine(vlcRoot, "vlc.exe"),
             runnerPath,
             workerPath,
             catalogPath
         })
{
    if (!File.Exists(required))
    {
        Console.Error.WriteLine($"Required integration-test file not found: {required}");
        return 2;
    }
}

var start = new ProcessStartInfo(runnerPath)
{
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false,
    CreateNoWindow = true
};
foreach (string argument in new[]
         {
             "--mode", "live-immediate",
             "--fake",
             "--fake-ready-delay-ms", "8000",
             "--vlc-root", vlcRoot,
             "--worker", workerPath,
             "--cpu-worker", workerPath,
             "--catalog", catalogPath,
             "--speech-device", "gpu",
             media,
             "--",
             "-I", "dummy",
             "--play-and-exit",
             $"--stop-time={timeoutSeconds}",
             "--aout=dummy",
             "--vout=dummy",
             "-vvv"
         })
{
    start.ArgumentList.Add(argument);
}

using Process process = Process.Start(start)
    ?? throw new InvalidOperationException("Could not start live translator runner.");
Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
Task<string> standardError = process.StandardError.ReadToEndAsync();
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds + 30));
try
{
    await process.WaitForExitAsync(timeout.Token);
}
catch (OperationCanceledException)
{
    if (!process.HasExited)
        process.Kill(entireProcessTree: true);
    Console.Error.WriteLine("Integration test timed out.");
    return 3;
}

string output = (await standardOutput) + Environment.NewLine + (await standardError);
string[] pipelineLines = output
    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
    .Where(line =>
        line.Contains("[LiveAudioTranslator]", StringComparison.Ordinal) ||
        line.Contains("event=worker_", StringComparison.Ordinal) ||
        line.Contains("event=video_decode", StringComparison.Ordinal) ||
        line.Contains("event=runner", StringComparison.Ordinal))
    .Distinct(StringComparer.Ordinal)
    .ToArray();

bool audioOpenSeen = Contains("Audio capture opened");
bool subtitleOpenSeen = Contains("Live subtitle source opened");
bool readySeen = Contains("event=worker_ready");
bool vlcStartedSeen = Contains("event=runner stage=vlc-started");
bool transportSeen = Contains("event=transport outcome=ready");
bool safeGpuVideoPolicySeen =
    Contains("event=video_decode policy=software reason=speech-device-gpu");
bool warmupDiscardSeen =
    pipelineLines.Any(line =>
        line.Contains("event=audio_gate outcome=ready", StringComparison.Ordinal) &&
        !line.Contains("discarded_blocks=0 ", StringComparison.Ordinal));
bool firstAcceptedSeen = Contains("event=audio_accept outcome=first");
bool translationSeen = Contains("event=translated");
bool renderedSeen = Contains("event=subtitle outcome=rendered");
bool cleanStop = Contains("event=runner outcome=stopped") && process.ExitCode == 0;
bool failure = output.Contains("outcome=failed", StringComparison.Ordinal);
int vlcStartedIndex = FindIndex("event=runner stage=vlc-started");
int audioOpenIndex = FindIndex("Audio capture opened");
int readyIndex = FindIndex("event=worker_ready");
bool playbackBeforeReady =
    vlcStartedIndex >= 0 &&
    audioOpenIndex >= 0 &&
    readyIndex >= 0 &&
    vlcStartedIndex < readyIndex &&
    audioOpenIndex < readyIndex;
bool passed =
    !failure &&
    audioOpenSeen &&
    subtitleOpenSeen &&
    readySeen &&
    vlcStartedSeen &&
    playbackBeforeReady &&
    transportSeen &&
    safeGpuVideoPolicySeen &&
    warmupDiscardSeen &&
    firstAcceptedSeen &&
    translationSeen &&
    renderedSeen &&
    cleanStop;

Console.WriteLine($"Audio filter opened: {audioOpenSeen}");
Console.WriteLine($"Subtitle source opened: {subtitleOpenSeen}");
Console.WriteLine($"VLC started before worker READY: {playbackBeforeReady}");
Console.WriteLine($"Worker became ready: {readySeen}");
Console.WriteLine($"Transport ready: {transportSeen}");
Console.WriteLine($"GPU speech avoids shared hardware video decode: {safeGpuVideoPolicySeen}");
Console.WriteLine($"Warm-up audio discarded: {warmupDiscardSeen}");
Console.WriteLine($"First post-ready audio accepted: {firstAcceptedSeen}");
Console.WriteLine($"Translation queued: {translationSeen}");
Console.WriteLine($"Subtitle rendered: {renderedSeen}");
Console.WriteLine($"Clean process lifecycle: {cleanStop}");
foreach (string line in pipelineLines)
    Console.WriteLine($"[pipeline] {line}");
Console.WriteLine(passed ? "INTEGRATION TEST: PASSED" : "INTEGRATION TEST: FAILED");
return passed ? 0 : 1;

bool Contains(string value) =>
    pipelineLines.Any(line => line.Contains(value, StringComparison.Ordinal));

int FindIndex(string value) =>
    Array.FindIndex(
        pipelineLines,
        line => line.Contains(value, StringComparison.Ordinal));

static string FindRepositoryRoot()
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory != null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "vlclr.sln")))
            return directory.FullName;
        directory = directory.Parent;
    }
    throw new DirectoryNotFoundException("Could not locate vlclr.sln.");
}
