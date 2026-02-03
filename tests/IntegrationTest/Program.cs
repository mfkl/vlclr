using LibVLCSharp;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: IntegrationTest <vlc-sdk-path> <video-url>");
            Console.WriteLine("Example: IntegrationTest ./vlc-sdk https://example.com/video.mp4");
            return 1;
        }

        var libvlcPath = args[0];
        var videoUrl = args[1];
        var timeout = args.Length > 2 ? int.Parse(args[2]) : 10;

        Console.WriteLine($"LibVLC path: {libvlcPath}");
        Console.WriteLine($"Video URL: {videoUrl}");
        Console.WriteLine($"Timeout: {timeout}s");
        Console.WriteLine();

        // Track filter activity through logs
        var filterOpenSeen = false;
        var filterCloseSeen = false;

        try
        {
            // Initialize libvlc from the specified path
            Core.Initialize(libvlcPath);

            using var libvlc = new LibVLC(
                "--ignore-config",
                "--aout=dummy",
                "--video-filter=dotnet_overlay",
                "--no-hw-dec",
                "-vvv"
            );

            var pluginErrors = new List<string>();

            // Set up log handler to capture filter output
            libvlc.Log += (sender, e) =>
            {
                var msg = e.FormattedLog;

                // VLC logs when using/removing our filter module
                if (msg.Contains("using video filter module") && msg.Contains("dotnet_overlay"))
                    filterOpenSeen = true;
                if (msg.Contains("removing") && msg.Contains("video filter") && msg.Contains("dotnet_overlay"))
                    filterCloseSeen = true;

                // Capture any errors related to our plugin or filters
                if (msg.Contains("dotnet") || msg.Contains("error") || msg.Contains("cannot"))
                {
                    pluginErrors.Add(msg);
                }
            };

            using var media = new Media(new Uri(videoUrl));
            media.AddOption(":run-time=5");
            media.AddOption(":play-and-exit");

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
            Console.WriteLine(" Integration Test Results");
            Console.WriteLine("========================================");
            Console.WriteLine();

            var passed = true;

            if (filterOpenSeen)
                Console.WriteLine("[PASS] Filter loaded");
            else
            {
                Console.WriteLine("[FAIL] Filter not loaded");
                passed = false;
            }

            if (filterCloseSeen)
                Console.WriteLine("[PASS] Filter unloaded (ran successfully)");
            else
            {
                Console.WriteLine("[FAIL] Filter not unloaded");
                passed = false;
            }

            // Show any plugin-related errors/messages
            if (pluginErrors.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("--- Plugin-related log messages ---");
                foreach (var err in pluginErrors.Take(20))
                {
                    Console.WriteLine(err);
                }
                if (pluginErrors.Count > 20)
                {
                    Console.WriteLine($"... and {pluginErrors.Count - 20} more");
                }
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
