using System.Diagnostics;
using LibVLCSharp;

if (args.Length < 4)
{
    Console.Error.WriteLine(
        "Usage: SubtitleFrameCapture <vlc-path> <video-url-or-path> <subtitle-file> <output.png> [capture-ms]");
    return 1;
}

string vlcPath = Path.GetFullPath(args[0]);
string video = args[1];
string subtitlePath = Path.GetFullPath(args[2]);
string outputPath = Path.GetFullPath(args[3]);
long captureMilliseconds = args.Length > 4 ? long.Parse(args[4]) : 2000;

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
if (File.Exists(outputPath))
{
    File.Delete(outputPath);
}

Core.Initialize(vlcPath);
using var libVlc = new LibVLC(
    "--ignore-config",
    "--aout=dummy",
    "--text-renderer=dotnet_subtitle",
    "--no-hw-dec",
    "--no-video-title-show",
    "-vv");

bool rendererOpened = false;
bool compactRegionCreated = false;
libVlc.Log += (_, eventArgs) =>
{
    string message = eventArgs.FormattedLog;
    rendererOpened |= message.Contains("[SubtitleTextRenderer] Opening text renderer", StringComparison.Ordinal);
    compactRegionCreated |= message.Contains("Created compact region", StringComparison.Ordinal);
};

using var media = new Media(ToMediaUri(video));
media.AddOption($":sub-file={subtitlePath}");
using var player = new MediaPlayer(libVlc, media);
using var failed = new ManualResetEventSlim(false);
player.EncounteredError += (_, _) => failed.Set();

if (!player.Play())
{
    Console.Error.WriteLine("VLC refused to start playback.");
    return 2;
}

Console.WriteLine($"Waiting {captureMilliseconds:N0} ms for the subtitle cue...");
Thread.Sleep(checked((int)captureMilliseconds + 250));
if (failed.IsSet)
{
    Console.Error.WriteLine("VLC reported a playback error.");
    return 4;
}

Console.WriteLine("Requesting snapshot...");
bool snapshotRequested = player.TakeSnapshot(0, outputPath, 0, 0);
var snapshotTimeout = Stopwatch.StartNew();
while (!File.Exists(outputPath) && snapshotTimeout.Elapsed < TimeSpan.FromSeconds(10))
{
    Thread.Sleep(50);
}

if (!snapshotRequested || !File.Exists(outputPath))
{
    Console.Error.WriteLine("VLC did not produce the requested snapshot.");
    return 5;
}

var output = new FileInfo(outputPath);
Console.WriteLine($"Snapshot: {output.FullName} ({output.Length:N0} bytes)");
Console.WriteLine($"Renderer opened: {rendererOpened}");
Console.WriteLine($"Compact region logged: {compactRegionCreated}");

player.Stop();

return rendererOpened && compactRegionCreated ? 0 : 6;

static Uri ToMediaUri(string input)
{
    if (Uri.TryCreate(input, UriKind.Absolute, out Uri? uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeFile))
    {
        return uri;
    }

    return new Uri(Path.GetFullPath(input));
}
