using LibVLCSharp;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: SubtitleRendererTest <vlc-sdk-path> <video-url> [subtitle-file] [timeout]");
            Console.WriteLine("Example: SubtitleRendererTest ./vlc-sdk https://example.com/video.mp4 test.srt 10");
            return 1;
        }

        var libvlcPath = args[0];
        var videoUrl = args[1];
        var subtitleFile = args.Length > 2 ? args[2] : null;
        var timeout = args.Length > 3 ? int.Parse(args[3]) : 10;

        Console.WriteLine($"LibVLC path: {libvlcPath}");
        Console.WriteLine($"Video URL: {videoUrl}");
        Console.WriteLine($"Subtitle file: {subtitleFile ?? "(none)"}");
        Console.WriteLine($"Timeout: {timeout}s");
        Console.WriteLine();

        // Track renderer activity through logs
        var rendererOpenSeen = false;
        var rendererCloseSeen = false;
        var renderCallbackInvoked = false;

        try
        {
            // Initialize libvlc from the specified path
            Core.Initialize(libvlcPath);

            using var libvlc = new LibVLC(
                "--ignore-config",
                "--aout=dummy",
                "--text-renderer=dotnet_subtitle",
                "--no-hw-dec",
                "-vvv"
            );

            // Set up log handler to capture renderer output
            libvlc.Log += (sender, e) =>
            {
                var msg = e.FormattedLog;

                // VLC logs when using/removing our text renderer module
                if (msg.Contains("using text renderer module") && msg.Contains("dotnet_subtitle"))
                    rendererOpenSeen = true;
                if (msg.Contains("removing") && msg.Contains("text renderer") && msg.Contains("dotnet_subtitle"))
                    rendererCloseSeen = true;

                // Our plugin logs when render callback is invoked
                if (msg.Contains("[.NET Subtitle]"))
                {
                    if (msg.Contains("Render called") || msg.Contains("RenderCount"))
                        renderCallbackInvoked = true;
                }
            };

            using var media = new Media(new Uri(videoUrl));
            media.AddOption($":run-time={timeout}");
            media.AddOption(":play-and-exit");

            // Add subtitle file if provided
            if (!string.IsNullOrEmpty(subtitleFile))
            {
                // Convert to absolute path if relative
                var absoluteSubPath = Path.IsPathRooted(subtitleFile)
                    ? subtitleFile
                    : Path.GetFullPath(subtitleFile);
                media.AddOption($":sub-file={absoluteSubPath}");
            }

            using var player = new MediaPlayer(libvlc, media);

            Console.WriteLine("Starting playback...");
            player.Play();

            // Wait for run-time to complete
            Thread.Sleep(timeout * 1000);

            player.Stop();

            // Give logs time to flush
            Thread.Sleep(500);

            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine(" Subtitle Renderer Integration Test Results");
            Console.WriteLine("========================================");
            Console.WriteLine();

            var passed = true;

            if (rendererOpenSeen)
                Console.WriteLine("[PASS] Text renderer loaded");
            else
            {
                Console.WriteLine("[FAIL] Text renderer not loaded");
                passed = false;
            }

            if (rendererCloseSeen)
                Console.WriteLine("[PASS] Text renderer unloaded (ran successfully)");
            else
            {
                Console.WriteLine("[FAIL] Text renderer not unloaded");
                passed = false;
            }

            // Render callback is optional - only check if subtitle file was provided
            if (!string.IsNullOrEmpty(subtitleFile))
            {
                if (renderCallbackInvoked)
                    Console.WriteLine("[PASS] Render callback was invoked");
                else
                    Console.WriteLine("[INFO] Render callback not detected (may be expected if subtitles don't appear in first few seconds)");
            }

            Console.WriteLine();
            if (passed)
            {
                Console.WriteLine("INTEGRATION TEST: PASSED");
                return 0;
            }
            else
            {
                Console.WriteLine("INTEGRATION TEST: FAILED");
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }
}
