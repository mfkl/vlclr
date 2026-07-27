using LiveAudioTranslator.VisualTest;
using System.Diagnostics;
using System.Text.Json;

if (args.Length >= 9 &&
    string.Equals(args[0], "--prepared-acceptance", StringComparison.Ordinal))
{
    string output = "";
    QtVlcRun? acceptanceRun = null;
    try
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 1; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) ||
                index + 1 >= args.Length)
            {
                throw new ArgumentException(
                    $"Invalid prepared-acceptance argument '{args[index]}'.");
            }
            values[args[index][2..]] = args[index + 1];
        }
        string vlcRoot = Path.GetFullPath(values["vlc-root"]);
        string mediaPath = Path.GetFullPath(values["media"]);
        string cueFile = Path.GetFullPath(values["cue-file"]);
        output = Path.GetFullPath(values["output"]);
        int backwardSeekSeconds = values.TryGetValue("backward-seconds", out string? backwardValue)
            ? int.Parse(backwardValue, System.Globalization.CultureInfo.InvariantCulture)
            : 28;
        int forwardSeekSeconds = values.TryGetValue("forward-seconds", out string? forwardValue)
            ? int.Parse(forwardValue, System.Globalization.CultureInfo.InvariantCulture)
            : 65;
        Directory.CreateDirectory(output);
        foreach (string staleArtifact in new[]
                 {
                     "prepared-acceptance.error.txt",
                     "prepared-acceptance.failure.vlc.log",
                     "prepared-acceptance.json",
                     "prepared-acceptance.vlc.log",
                     "prepared-qt.png"
                 })
        {
            File.Delete(Path.Combine(output, staleArtifact));
        }

        var timer = new Stopwatch();
        int backwardGeneration;
        int forwardGeneration;
        bool forcedTermination;
        int exitCode;
        QtWindowMetadata metadata;
        string command;
        string[] trace;
        await using (QtVlcRun run = await QtVlcRun.StartPreparedAsync(
                         vlcRoot,
                         new Uri(mediaPath),
                         cueFile))
        {
            acceptanceRun = run;
            command = run.Command;
            (nint window, metadata) = await WindowsWindowCapture.WaitForVlcWindowAsync(
                run.ProcessId,
                TimeSpan.FromSeconds(30));
            timer.Start();
            await WaitUntilAsync(
                () => timer.Elapsed >= TimeSpan.FromSeconds(60) &&
                    Rendered(run.PipelineTrace).Count >= 5,
                run,
                TimeSpan.FromSeconds(75),
                "five rendered cues during 60 seconds of visible playback");
            metadata = await WindowsWindowCapture.CaptureAsync(
                window,
                Path.Combine(output, "prepared-qt.png"),
                TimeSpan.FromSeconds(30));

            int initialGeneration = MaximumGeneration(Rendered(run.PipelineTrace));
            int backwardMarker = run.PipelineTrace.Count;
            await run.SeekAsync(backwardSeekSeconds);
            backwardGeneration = await WaitForNewRenderedGenerationAsync(
                run,
                backwardMarker,
                initialGeneration,
                TimeSpan.FromSeconds(15));

            int forwardMarker = run.PipelineTrace.Count;
            await run.SeekAsync(forwardSeekSeconds);
            forwardGeneration = await WaitForNewRenderedGenerationAsync(
                run,
                forwardMarker,
                backwardGeneration,
                TimeSpan.FromSeconds(15));
            await Task.Delay(TimeSpan.FromSeconds(35));

            await run.StopAsync();
            forcedTermination = run.WasForceKilled;
            exitCode = run.ExitCode;
            trace = run.AllTrace.ToArray();
        }
        timer.Stop();

        string[] pluginTrace = trace
            .Where(line => line.Contains("[LiveAudioTranslator]", StringComparison.Ordinal))
            .ToArray();
        string[] rendered = Rendered(pluginTrace).ToArray();
        string[] scheduled = pluginTrace
            .Where(line => line.Contains(
                "event=subtitle outcome=scheduled",
                StringComparison.Ordinal))
            .ToArray();
        double schedulerP95Milliseconds = NearestRankP95(
            scheduled
                .Where(line => line.Contains(
                    "scheduler_sample=steady-state",
                    StringComparison.Ordinal))
                .Select(line => Math.Abs(ReadDoubleField(line, "scheduler_error_ms")))
                .ToArray());
        double maximumResumeAgeMilliseconds = scheduled
            .Where(line => line.Contains(
                "scheduler_sample=resume-age",
                StringComparison.Ordinal))
            .Select(line => Math.Abs(ReadDoubleField(line, "scheduler_error_ms")))
            .DefaultIfEmpty(0)
            .Max();
        int latePictures = trace.Count(line =>
            line.Contains("picture displayed late", StringComparison.OrdinalIgnoreCase));
        int droppedFrames = trace.Count(line =>
            line.Contains("dropped frame", StringComparison.OrdinalIgnoreCase));
        bool hardwareDecode = trace.Any(line =>
            line.Contains("d3d11va generic debug: CreateDevice succeed", StringComparison.Ordinal) ||
            line.Contains("pix_fmt: d3d11va_vld", StringComparison.Ordinal));
        bool direct3d11Output = trace.Any(line =>
            line.Contains("direct3d11 vout display", StringComparison.Ordinal));
        bool cleanModuleClose =
            pluginTrace.Any(line => line.Contains("Audio capture closed", StringComparison.Ordinal)) &&
            pluginTrace.Any(line => line.Contains("Live subtitle source closed", StringComparison.Ordinal));
        bool leadUnderrun = pluginTrace.Any(line =>
            line.Contains("event=lead_underrun", StringComparison.Ordinal));
        bool nativeFailure = trace.Any(line =>
            line.Contains("assertion failed", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("segmentation fault", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("access violation", StringComparison.OrdinalIgnoreCase));
        bool generationsMonotonic = RenderedGenerations(rendered)
            .Zip(RenderedGenerations(rendered).Skip(1))
            .All(pair => pair.First <= pair.Second);
        bool passed =
            timer.Elapsed >= TimeSpan.FromSeconds(60) &&
            rendered.Length >= 5 &&
            backwardGeneration > 0 &&
            forwardGeneration > backwardGeneration &&
            generationsMonotonic &&
            schedulerP95Milliseconds <= 150 &&
            !leadUnderrun &&
            hardwareDecode &&
            direct3d11Output &&
            droppedFrames == 0 &&
            !nativeFailure &&
            cleanModuleClose &&
            !forcedTermination &&
            exitCode == 0 &&
            metadata.Visible &&
            !metadata.Minimized &&
            metadata.Unobscured;
        var report = new
        {
            passed,
            visiblePlaybackSeconds = timer.Elapsed.TotalSeconds,
            renderedCueCount = rendered.Length,
            firstGeneration = RenderedGenerations(rendered).FirstOrDefault(),
            backwardGeneration,
            forwardGeneration,
            backwardSeekSeconds,
            forwardSeekSeconds,
            generationsMonotonic,
            schedulerP95Milliseconds,
            maximumResumeAgeMilliseconds,
            leadUnderrun,
            hardwareDecode,
            direct3d11Output,
            latePictures,
            droppedFrames,
            nativeFailure,
            cleanModuleClose,
            forcedTermination,
            exitCode,
            qtWindow = metadata,
            launchCommand = command
        };
        await File.WriteAllTextAsync(
            Path.Combine(output, "prepared-acceptance.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllLinesAsync(
            Path.Combine(output, "prepared-acceptance.vlc.log"),
            trace);
        Console.WriteLine(
            passed
                ? "PREPARED QT ACCEPTANCE: PASSED"
                : "PREPARED QT ACCEPTANCE: FAILED");
        return passed ? 0 : 1;
    }
    catch (Exception ex)
    {
        if (output.Length > 0)
        {
            await File.WriteAllTextAsync(
                Path.Combine(output, "prepared-acceptance.error.txt"),
                $"{ex.GetType().Name}: {ex.Message}");
            if (acceptanceRun != null)
            {
                await File.WriteAllLinesAsync(
                    Path.Combine(output, "prepared-acceptance.failure.vlc.log"),
                    acceptanceRun.AllTrace);
            }
        }
        Console.Error.WriteLine($"PREPARED QT ACCEPTANCE: FAILED: {ex}");
        return 1;
    }
}

if (args.Length >= 7 &&
    string.Equals(args[0], "--native-marq-control", StringComparison.Ordinal))
{
    try
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 1; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) ||
                index + 1 >= args.Length)
            {
                throw new ArgumentException(
                    $"Invalid native-marquee control argument '{args[index]}'.");
            }
            values[args[index][2..]] = args[index + 1];
        }
        string vlcRoot = Path.GetFullPath(values["vlc-root"]);
        string mediaPath = Path.GetFullPath(values["media"]);
        string output = Path.GetFullPath(values["output"]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        await using QtVlcRun run = await QtVlcRun.StartNativeMarqueeAsync(
            vlcRoot,
            new Uri(mediaPath),
            "VISIBLE_NATIVE_MARQ");
        (nint window, QtWindowMetadata metadata) =
            await WindowsWindowCapture.WaitForVlcWindowAsync(
                run.ProcessId,
                TimeSpan.FromSeconds(30));
        await Task.Delay(1_000);
        metadata = WindowsWindowCapture.CaptureWithPrintWindow(window, output);
        await run.KeepControlAliveAsync();
        await Task.Delay(250);
        await run.SeekAsync(28);
        await Task.Delay(1_000);
        await File.WriteAllTextAsync(
            output + ".json",
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        await run.StopAsync();
        await File.WriteAllLinesAsync(output + ".vlc.log", run.AllTrace);
        Console.WriteLine($"VLC NATIVE MARQUEE CONTROL: PASSED: {output}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"VLC NATIVE MARQUEE CONTROL: FAILED: {ex}");
        return 1;
    }
}

if (args.Length >= 4 &&
    string.Equals(args[0], "--attach-pid", StringComparison.Ordinal))
{
    try
    {
        int processId = int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
        if (!string.Equals(args[2], "--output", StringComparison.Ordinal))
            throw new ArgumentException("Expected --output after the process ID.");
        string output = Path.GetFullPath(args[3]);
        int timeoutSeconds = args.Length >= 6 &&
            string.Equals(args[4], "--timeout-seconds", StringComparison.Ordinal)
                ? int.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture)
                : 30;
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        (nint window, QtWindowMetadata metadata) =
            await WindowsWindowCapture.WaitForVlcWindowAsync(
                processId,
                TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 120)));
        metadata = await WindowsWindowCapture.CaptureAsync(
            window,
            output,
            TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 120)));
        await File.WriteAllTextAsync(
            output + ".json",
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"VLC QT WINDOW CAPTURE: PASSED: {output}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"VLC QT WINDOW CAPTURE: FAILED: {ex.Message}");
        return 1;
    }
}

VisualTestOptions options;
try
{
    options = VisualTestOptions.Parse(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

try
{
    Directory.CreateDirectory(options.ArtifactsDirectory);
    var harness = new VisualTestHarness(options);
    VisualTestResult result = await harness.RunAsync();
    await result.WriteAsync(Path.Combine(options.ArtifactsDirectory, "visual-result.json"));
    Console.WriteLine(result.Passed ? "LIVE SYNC VISUAL TEST: PASSED" : "LIVE SYNC VISUAL TEST: FAILED");
    return result.Passed ? 0 : 1;
}
catch (Exception ex)
{
    var failed = VisualTestResult.Failure(ex);
    await failed.WriteAsync(Path.Combine(options.ArtifactsDirectory, "visual-result.json"));
    Console.Error.WriteLine($"LIVE SYNC VISUAL TEST: FAILED: {ex.Message}");
    return 1;
}

static IReadOnlyCollection<string> Rendered(
    IReadOnlyCollection<string> lines) =>
    lines.Where(line =>
        line.Contains("event=subtitle outcome=rendered", StringComparison.Ordinal)).ToArray();

static IEnumerable<int> RenderedGenerations(
    IEnumerable<string> lines) =>
    lines.Select(ReadGeneration);

static int MaximumGeneration(
    IReadOnlyCollection<string> lines) =>
    lines.Count == 0 ? 0 : RenderedGenerations(lines).Max();

static int ReadGeneration(string line)
{
    const string prefix = "generation=";
    string part = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .First(value => value.StartsWith(prefix, StringComparison.Ordinal));
    return int.Parse(
        part[prefix.Length..],
        System.Globalization.CultureInfo.InvariantCulture);
}

static double ReadDoubleField(string line, string name)
{
    string prefix = name + "=";
    string part = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .First(value => value.StartsWith(prefix, StringComparison.Ordinal));
    return double.Parse(
        part[prefix.Length..],
        System.Globalization.CultureInfo.InvariantCulture);
}

static double NearestRankP95(IReadOnlyCollection<double> values)
{
    if (values.Count == 0)
        return double.PositiveInfinity;
    double[] ordered = values.Order().ToArray();
    int index = Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1);
    return ordered[index];
}

static async Task WaitUntilAsync(
    Func<bool> condition,
    QtVlcRun run,
    TimeSpan timeout,
    string description)
{
    var timer = Stopwatch.StartNew();
    var keepAlive = Stopwatch.StartNew();
    while (!condition() && timer.Elapsed < timeout)
    {
        if (run.HasExited)
            throw new InvalidOperationException($"VLC exited with code {run.ExitCode}.");
        if (keepAlive.Elapsed >= TimeSpan.FromSeconds(10))
        {
            await run.KeepControlAliveAsync();
            keepAlive.Restart();
        }
        await Task.Delay(100);
    }
    if (!condition())
        throw new TimeoutException($"Timed out waiting for {description}.");
}

static async Task<int> WaitForNewRenderedGenerationAsync(
    QtVlcRun run,
    int marker,
    int previousGeneration,
    TimeSpan timeout)
{
    int generation = 0;
    await WaitUntilAsync(
        () =>
        {
            string[] lines = run.PipelineTrace.Skip(marker).ToArray();
            generation = MaximumGeneration(Rendered(lines));
            return generation > previousGeneration;
        },
        run,
        timeout,
        $"a rendered cue after generation {previousGeneration}");
    return generation;
}
