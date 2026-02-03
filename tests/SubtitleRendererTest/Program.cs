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
        var renderCount = 0;
        var errorMessages = new List<string>();
        var renderLogTimestamps = new List<DateTime>();

        try
        {
            // Initialize libvlc from the specified path
            Core.Initialize(libvlcPath);

            // Set up environment for debug output if requested
            var debugOutputPath = Environment.GetEnvironmentVariable("DOTNET_SUBTITLE_DEBUG_PATH");
            if (!string.IsNullOrEmpty(debugOutputPath))
            {
                Console.WriteLine($"Debug output will be saved to: {debugOutputPath}");
            }

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
                    renderCallbackInvoked = true;
                    renderLogTimestamps.Add(DateTime.Now);
                    
                    // Count render calls from "Render #N:" messages
                    if (msg.Contains("Render #"))
                    {
                        renderCount++;
                        Console.WriteLine($"  [Render {renderCount}] {msg.Trim()}");
                    }
                    
                    // Also detect cleanup message which shows total renders
                    // Extract the count from "RendererState cleanup, rendered N times"
                    if (msg.Contains("RendererState cleanup, rendered"))
                    {
                        Console.WriteLine($"  [Cleanup] {msg.Trim()}");
                        var match = System.Text.RegularExpressions.Regex.Match(msg, @"rendered (\d+) times");
                        if (match.Success)
                        {
                            var cleanupCount = int.Parse(match.Groups[1].Value);
                            if (cleanupCount > 0)
                            {
                                renderCallbackInvoked = true;
                                // Use the cleanup count if we missed individual renders
                                if (renderCount == 0)
                                {
                                    renderCount = cleanupCount;
                                }
                            }
                        }
                    }
                }

                // Capture error messages from our plugin
                if (msg.Contains("[.NET Subtitle]") && (msg.Contains("error") || msg.Contains("Error") || msg.Contains("FAIL")))
                {
                    errorMessages.Add(msg.Trim());
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

            // Wait longer for all logs to flush, including cleanup message
            // The cleanup message comes after VLC releases the renderer
            Thread.Sleep(2000);

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

            // Render callback verification - required when subtitle file is provided
            if (!string.IsNullOrEmpty(subtitleFile))
            {
                Console.WriteLine();
                Console.WriteLine("--- Render Verification ---");
                
                if (renderCallbackInvoked)
                {
                    Console.WriteLine($"[PASS] Render callback was invoked ({renderCount} render calls detected)");
                    
                    // Check if we got at least some renders
                    if (renderCount == 0)
                    {
                        Console.WriteLine("[WARN] Render callbacks detected but no render count extracted from logs");
                    }
                    else if (renderCount < 2)
                    {
                        Console.WriteLine("[INFO] Only 1 render detected - subtitles may not be appearing at test timecodes");
                    }
                    else
                    {
                        Console.WriteLine($"[INFO] Rendered {renderCount} subtitle frames");
                    }
                }
                else
                {
                    Console.WriteLine("[FAIL] Render callback not detected - subtitles were not rendered");
                    passed = false;
                }

                // Check for errors
                if (errorMessages.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("[WARN] Errors detected during rendering:");
                    foreach (var err in errorMessages.Take(5))
                    {
                        Console.WriteLine($"       {err}");
                    }
                    if (errorMessages.Count > 5)
                    {
                        Console.WriteLine($"       ... and {errorMessages.Count - 5} more");
                    }
                }
                else
                {
                    Console.WriteLine("[PASS] No errors detected in logs");
                }

                // Check for debug output file
                if (!string.IsNullOrEmpty(debugOutputPath))
                {
                    Console.WriteLine();
                    if (File.Exists(debugOutputPath))
                    {
                        var fileInfo = new FileInfo(debugOutputPath);
                        Console.WriteLine($"[PASS] Debug image created: {debugOutputPath} ({fileInfo.Length} bytes)");
                    }
                    else
                    {
                        Console.WriteLine($"[WARN] Debug image not found at: {debugOutputPath}");
                        Console.WriteLine($"       (Check that DOTNET_SUBTITLE_DEBUG_PATH is set correctly)");
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("========================================");
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
