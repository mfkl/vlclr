using LibVLCSharp;

if (args.Length < 2)
{
    Console.Error.WriteLine(
        "Usage: TranslatorIntegrationTest <vlc-path> <video-url> [subtitle-file] [timeout-seconds] " +
        "[expected-rendered:translated|fallback]");
    return 1;
}

string libVlcPath = Path.GetFullPath(args[0]);
string videoUrl = args[1];
string? subtitleFile = args.Length > 2 ? args[2] : null;
int timeoutSeconds = args.Length > 3 ? int.Parse(args[3]) : 20;
string expectedRendered = args.Length > 4 ? args[4].ToLowerInvariant() : "translated";
if (expectedRendered is not ("translated" or "fallback"))
{
    Console.Error.WriteLine("expected-rendered must be 'translated' or 'fallback'.");
    return 1;
}

bool translatorOpenSeen = false;
bool translatorCloseSeen = false;
bool compactRegionSeen = false;
bool expectedOutcomeSeen = false;
var eventLines = new List<string>();
var errorMessages = new List<string>();

try
{
    Core.Initialize(libVlcPath);
    using var libVlc = new LibVLC(
        "--ignore-config",
        "--aout=dummy",
        "--text-renderer=dotnet_subtitle_translator",
        "--no-hw-dec",
        "--no-video-title-show",
        "-vvv");

    libVlc.Log += (_, eventArgs) =>
    {
        string message = eventArgs.FormattedLog;
        translatorOpenSeen |=
            message.Contains("[SubtitleTranslator] Opening translator plugin", StringComparison.Ordinal) ||
            (message.Contains("using text renderer module", StringComparison.Ordinal) &&
             message.Contains("dotnet_subtitle_translator", StringComparison.Ordinal));
        translatorCloseSeen |= message.Contains("[SubtitleTranslator] Closing", StringComparison.Ordinal);
        compactRegionSeen |=
            message.Contains("[SubtitleTranslator] Created compact region", StringComparison.Ordinal);

        if (message.Contains("[SubtitleTranslator] event", StringComparison.Ordinal))
        {
            eventLines.Add(message.Trim());
            expectedOutcomeSeen |= message.Contains($"rendered={expectedRendered}", StringComparison.Ordinal);
            Console.WriteLine($"[event] {message.Trim()}");
        }

        if (message.Contains("[SubtitleTranslator] Initialization failed", StringComparison.Ordinal) ||
            message.Contains("native-load-failed", StringComparison.Ordinal))
        {
            errorMessages.Add(message.Trim());
        }
    };

    using var media = new Media(ToMediaUri(videoUrl));
    if (!string.IsNullOrWhiteSpace(subtitleFile))
        media.AddOption($":sub-file={Path.GetFullPath(subtitleFile)}");

    using var player = new MediaPlayer(libVlc, media);
    using var ended = new ManualResetEventSlim(false);
    using var failed = new ManualResetEventSlim(false);
    player.Stopped += (_, _) => ended.Set();
    player.EncounteredError += (_, _) => failed.Set();

    if (!player.Play())
    {
        Console.Error.WriteLine("VLC refused to start playback.");
        return 2;
    }

    ended.Wait(TimeSpan.FromSeconds(timeoutSeconds));
    player.Stop();
    Thread.Sleep(1000);

    bool passed =
        !failed.IsSet &&
        translatorOpenSeen &&
        compactRegionSeen &&
        expectedOutcomeSeen;

    Console.WriteLine($"Translator opened: {translatorOpenSeen}");
    Console.WriteLine($"Translator close observed: {translatorCloseSeen}");
    Console.WriteLine($"Compact region observed: {compactRegionSeen}");
    Console.WriteLine($"Structured events: {eventLines.Count}");
    Console.WriteLine($"Expected rendered outcome ({expectedRendered}): {expectedOutcomeSeen}");
    Console.WriteLine($"Playback error: {failed.IsSet}");
    if (errorMessages.Count > 0)
        Console.WriteLine($"Initialization errors: {string.Join(" | ", errorMessages)}");

    Console.WriteLine(passed ? "INTEGRATION TEST: PASSED" : "INTEGRATION TEST: FAILED");
    return passed ? 0 : 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Integration test failed: {ex}");
    return 1;
}

static Uri ToMediaUri(string input)
{
    if (Uri.TryCreate(input, UriKind.Absolute, out Uri? uri) &&
        uri.Scheme is "http" or "https" or "file")
    {
        return uri;
    }

    return new Uri(Path.GetFullPath(input));
}
