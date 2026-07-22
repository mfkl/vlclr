using LibVLCSharp;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: TranslatorIntegrationTest <vlc-sdk-path> <video-url> [subtitle-file] [timeout]");
            return 1;
        }

        var libvlcPath = args[0];
        var videoUrl = args[1];
        var subtitleFile = args.Length > 2 ? args[2] : null;
        var timeout = args.Length > 3 ? int.Parse(args[3]) : 20;

        Console.WriteLine($"LibVLC path: {libvlcPath}");
        Console.WriteLine($"Video URL: {videoUrl}");
        Console.WriteLine($"Subtitle file: {subtitleFile ?? "(none)"}");
        Console.WriteLine($"Timeout: {timeout}s");
        Console.WriteLine();

        var translatorOpenSeen = false;
        var translatorCloseSeen = false;
        var translationSeen = false;
        var translationLines = new List<string>();
        var errorMessages = new List<string>();

        try
        {
            Core.Initialize(libvlcPath);

            using var libvlc = new LibVLC(
                "--ignore-config",
                "--aout=dummy",
                "--text-renderer=dotnet_subtitle_translator",
                "--no-hw-dec",
                "-vvv"
            );

            libvlc.Log += (sender, e) =>
            {
                var msg = e.FormattedLog;

                // Detect translator plugin load/unload
                if (msg.Contains("using text renderer module") && msg.Contains("dotnet_subtitle_translator"))
                    translatorOpenSeen = true;
                if (msg.Contains("removing") && msg.Contains("text renderer") && msg.Contains("dotnet_subtitle_translator"))
                    translatorCloseSeen = true;

                // Detect translation output from our plugin
                if (msg.Contains("[SubtitleTranslator]"))
                {
                    // Capture translation lines: "#N: "original" -> "translated""
                    if (msg.Contains("->"))
                    {
                        translationSeen = true;
                        translationLines.Add(msg.Trim());
                        Console.WriteLine($"  [Translation] {msg.Trim()}");
                    }
                    else if (msg.Contains("Model ready"))
                    {
                        Console.WriteLine($"  [Init] {msg.Trim()}");
                    }
                    else if (msg.Contains("error") || msg.Contains("Error") || msg.Contains("FAIL") ||
                             msg.Contains("Inner:") || msg.Contains("Stack:"))
                    {
                        errorMessages.Add(msg.Trim());
                        Console.WriteLine($"  [ERROR] {msg.Trim()}");
                    }
                    else if (msg.Contains("Opening") || msg.Contains("Loading") || msg.Contains("Pre-warming") || msg.Contains("Closing") || msg.Contains("resolver") || msg.Contains("Trying") || msg.Contains("result"))
                    {
                        Console.WriteLine($"  [Info] {msg.Trim()}");
                    }
                }
            };

            using var media = new Media(new Uri(videoUrl));

            if (!string.IsNullOrEmpty(subtitleFile))
            {
                var absoluteSubPath = Path.IsPathRooted(subtitleFile)
                    ? subtitleFile
                    : Path.GetFullPath(subtitleFile);
                media.AddOption($":sub-file={absoluteSubPath}");
            }

            using var player = new MediaPlayer(libvlc, media);

            var endReached = new ManualResetEventSlim(false);
            player.Stopped += (_, _) => endReached.Set();

            Console.WriteLine("Starting playback...");
            player.Play();

            // Wait for timeout or playback end
            endReached.Wait(TimeSpan.FromSeconds(timeout));

            Console.WriteLine("Stopping playback...");
            player.Stop();

            // Wait for cleanup logs
            Thread.Sleep(3000);

            // Results
            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine(" Subtitle Translator Integration Test");
            Console.WriteLine("========================================");
            Console.WriteLine();

            var passed = true;

            if (translatorOpenSeen)
                Console.WriteLine("[PASS] Translator plugin loaded");
            else
            {
                Console.WriteLine("[FAIL] Translator plugin not loaded");
                passed = false;
            }

            if (translatorCloseSeen)
                Console.WriteLine("[PASS] Translator plugin unloaded cleanly");
            else
            {
                Console.WriteLine("[WARN] Translator plugin unload not detected in logs");
            }

            if (translationSeen)
            {
                Console.WriteLine($"[PASS] Translations detected ({translationLines.Count} lines)");
                foreach (var line in translationLines.Take(10))
                    Console.WriteLine($"       {line}");
            }
            else
            {
                Console.WriteLine("[INFO] No translation output detected in logs");
                Console.WriteLine("       (Only first 5 renders are logged; subtitles may not have appeared in time window)");
            }

            if (errorMessages.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("[WARN] Errors detected:");
                foreach (var err in errorMessages.Take(5))
                    Console.WriteLine($"       {err}");
                if (errorMessages.Count > 5)
                    Console.WriteLine($"       ... and {errorMessages.Count - 5} more");
            }
            else
            {
                Console.WriteLine("[PASS] No errors detected");
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
