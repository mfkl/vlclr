using System.Diagnostics;

if (args.Length < 2)
{
    Console.Error.WriteLine(
        "Usage: LiveAudioTranslatorIntegrationTest <vlc-path> <video-url> [timeout-seconds] " +
        "[whisper-model-path] [translation-model-directory]");
    return 1;
}

string vlcRoot = Path.GetFullPath(args[0]);
string vlcExecutable = Path.Combine(vlcRoot, "vlc.exe");
string videoUri = ToMediaUri(args[1]).AbsoluteUri;
int timeoutSeconds = args.Length > 2 ? int.Parse(args[2]) : 45;
string? whisperModelPath = args.Length > 3 ? Path.GetFullPath(args[3]) : null;
string? translationModelPath = args.Length > 4 ? Path.GetFullPath(args[4]) : null;
string gitBash = FindGitBash();

if (!File.Exists(vlcExecutable))
{
    Console.Error.WriteLine($"VLC executable not found: {vlcExecutable}");
    return 2;
}

var vlcArguments = new List<string>
{
    ToGitBashPath(vlcExecutable),
    "-I", "dummy",
    "--play-and-exit",
    "--aout=dummy",
    "--vout=dummy",
    "--live-translator-mode=live",
    "--audio-filter=dotnet_audio_translator",
    "--sub-source=dotnet_live_subtitles",
    "--no-video-title-show",
    "-vvv"
};
if (whisperModelPath != null)
    vlcArguments.Add($"--live-translator-whisper-model={whisperModelPath}");
if (translationModelPath != null)
    vlcArguments.Add($"--live-translator-translation-model={translationModelPath}");
vlcArguments.Add(videoUri);

string command = $"timeout {timeoutSeconds}s " + string.Join(' ', vlcArguments.Select(ShellQuote));
var startInfo = new ProcessStartInfo(gitBash)
{
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false,
    CreateNoWindow = true
};
startInfo.ArgumentList.Add("-lc");
startInfo.ArgumentList.Add(command);

using var process = Process.Start(startInfo);
if (process == null)
{
    Console.Error.WriteLine("Could not start Git Bash.");
    return 3;
}

Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
Task<string> standardError = process.StandardError.ReadToEndAsync();
await process.WaitForExitAsync();
string output = (await standardOutput) + Environment.NewLine + (await standardError);
string[] pipelineLines = output
    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
    .Where(line => line.Contains("[LiveAudioTranslator]", StringComparison.Ordinal))
    .Distinct(StringComparer.Ordinal)
    .ToArray();

bool audioOpenSeen = Contains("Audio capture opened");
bool subtitleOpenSeen = Contains("Live subtitle source opened");
bool readySeen = Contains("event=ready");
bool clockSeen = Contains("event=clock_anchor") && Contains("generation=");
bool translationSeen = Contains("event=translated");
bool renderedSeen = Contains("event=subtitle outcome=rendered");
string[] failures = pipelineLines
    .Where(line => line.Contains("event=failed", StringComparison.Ordinal))
    .ToArray();
bool playbackError =
    output.Contains("cannot open input", StringComparison.OrdinalIgnoreCase) ||
    output.Contains("Your input can't be opened", StringComparison.OrdinalIgnoreCase);

bool passed =
    !playbackError &&
    failures.Length == 0 &&
    audioOpenSeen &&
    subtitleOpenSeen &&
    readySeen &&
    clockSeen &&
    translationSeen &&
    renderedSeen;

Console.WriteLine($"Audio filter opened: {audioOpenSeen}");
Console.WriteLine($"Subtitle source opened: {subtitleOpenSeen}");
Console.WriteLine($"Models ready: {readySeen}");
Console.WriteLine($"PTS/generation metric seen: {clockSeen}");
Console.WriteLine($"Translation queued: {translationSeen}");
Console.WriteLine($"Subtitle rendered: {renderedSeen}");
Console.WriteLine($"Playback error: {playbackError}");
Console.WriteLine($"VLC exit code: {process.ExitCode}");
foreach (string line in pipelineLines)
    Console.WriteLine($"[pipeline] {line}");
Console.WriteLine(passed ? "INTEGRATION TEST: PASSED" : "INTEGRATION TEST: FAILED");
return passed ? 0 : 1;

bool Contains(string value) =>
    pipelineLines.Any(line => line.Contains(value, StringComparison.Ordinal));

static string FindGitBash()
{
    string? configured = Environment.GetEnvironmentVariable("GIT_BASH_PATH");
    string[] candidates =
    [
        configured ?? "",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "usr", "bin", "bash.exe")
    ];
    return candidates.FirstOrDefault(File.Exists) ??
        throw new FileNotFoundException("Git Bash not found. Set GIT_BASH_PATH to bash.exe.");
}

static string ToGitBashPath(string path)
{
    string fullPath = Path.GetFullPath(path).Replace('\\', '/');
    if (fullPath.Length >= 3 && fullPath[1] == ':' && fullPath[2] == '/')
        return $"/{char.ToLowerInvariant(fullPath[0])}/{fullPath[3..]}";
    return fullPath;
}

static string ShellQuote(string value) => $"'{value.Replace("'", "'\\''")}'";

static Uri ToMediaUri(string input)
{
    if (Uri.TryCreate(input, UriKind.Absolute, out Uri? uri) &&
        uri.Scheme is "http" or "https" or "file")
    {
        return uri;
    }

    return new Uri(Path.GetFullPath(input));
}
