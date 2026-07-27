using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using LibVLCSharp;

namespace LiveAudioTranslator.VisualTest;

internal sealed class VisualTestHarness(VisualTestOptions options)
{
    private readonly Dictionary<string, string> _artifacts = [];
    private readonly Dictionary<string, object> _metrics = [];
    private QtWindowMetadata? _qtMetadata;
    private string _launchCommand = "";
    private int _launchPid;
    private Uri _mediaUri = new("about:blank");

    public async Task<VisualTestResult> RunAsync()
    {
        ValidateInputs();
        await using LoopbackRangeMediaServer? mediaServer = options.UseLoopbackHttp
            ? LoopbackRangeMediaServer.Start(options.MediaPath)
            : null;
        _mediaUri = mediaServer?.MediaUri ?? new Uri(options.MediaPath);
        _metrics["mediaTransport"] = options.Transport;
        _metrics["mediaUriScheme"] = _mediaUri.Scheme;
        try
        {
            bool snapshotsPassed = true;
            bool qtPassed = true;
            if (options.CaptureSnapshots)
                snapshotsPassed = await RunSnapshotsAsync();
            if (options.CaptureQt)
                qtPassed = await RunQtCapturesAsync();

            return new VisualTestResult
            {
                Passed = snapshotsPassed && qtPassed,
                Artifacts = _artifacts,
                Metrics = _metrics,
                QtWindow = _qtMetadata,
                LaunchCommand = _launchCommand,
                LaunchProcessId = _launchPid
            };
        }
        finally
        {
            if (mediaServer != null)
            {
                _metrics["loopbackServer"] = mediaServer.Snapshot();
                File.WriteAllLines(Artifact("http-server-trace.log"), mediaServer.GetTrace());
            }
        }
    }

    private async Task<bool> RunSnapshotsAsync()
    {
        string baseline = Artifact("baseline.png");
        string translated = Artifact("translated.png");
        string difference = Artifact("diff.png");
        CaptureLibVlcSnapshot(baseline, worker: null, out _);

        await using WorkerSession worker = await WorkerSession.StartAsync(options);
        CaptureLibVlcSnapshot(translated, worker, out string[] pluginTrace);
        string[] workerTrace = await worker.StopAndGetTraceAsync();
        File.WriteAllLines(Artifact("snapshot-plugin-trace.log"), pluginTrace);
        File.WriteAllLines(Artifact("snapshot-worker-trace.log"), workerTrace);
        AssertTraceAgreement(pluginTrace, workerTrace);

        ImageDifferenceResult result = ImageAssertions.Compare(baseline, translated, difference);
        _metrics["snapshotDifference"] = result;
        return result.Passed;
    }

    private async Task<bool> RunQtCapturesAsync()
    {
        string baseline = Artifact("qt-baseline.png");
        string postSeekBaseline = Artifact("qt-post-seek-baseline.png");
        string translated = Artifact("qt-translated.png");
        string postSeek = Artifact("qt-post-seek.png");
        string difference = Artifact("qt-diff.png");
        string postSeekDifference = Artifact("qt-post-seek-diff.png");

        await using (QtVlcRun normal = await QtVlcRun.StartAsync(
                         options,
                         _mediaUri,
                         worker: null))
        {
            (nint window, _) = await WindowsWindowCapture.WaitForVlcWindowAsync(
                normal.ProcessId,
                TimeSpan.FromSeconds(30));
            await normal.SeekAndPauseAsync(5, waitForTranslatedCue: false);
            _ = await WindowsWindowCapture.CaptureAsync(
                window,
                baseline,
                TimeSpan.FromSeconds(30));
            await normal.SeekWhilePausedAsync(1);
            _ = await WindowsWindowCapture.CaptureAsync(
                window,
                postSeekBaseline,
                TimeSpan.FromSeconds(30));
            await normal.StopAsync();
        }

        await using WorkerSession worker = await WorkerSession.StartAsync(options);
        await using (QtVlcRun translatedRun = await QtVlcRun.StartAsync(
                         options,
                         _mediaUri,
                         worker))
        {
            _launchCommand = translatedRun.Command;
            _launchPid = translatedRun.ProcessId;
            (nint window, QtWindowMetadata metadata) =
                await WindowsWindowCapture.WaitForVlcWindowAsync(
                    translatedRun.ProcessId,
                    TimeSpan.FromSeconds(30));
            await translatedRun.SeekAndPauseAsync(5, waitForTranslatedCue: true);
            _qtMetadata = await WindowsWindowCapture.CaptureAsync(
                window,
                translated,
                TimeSpan.FromSeconds(30));
            await translatedRun.SeekWhilePausedAsync(1);
            _ = await WindowsWindowCapture.CaptureAsync(
                window,
                postSeek,
                TimeSpan.FromSeconds(30));
            await translatedRun.StopAsync();
            _metrics["qtPluginTraceLines"] = translatedRun.PipelineTrace.Count;
        }
        string[] workerTrace = await worker.StopAndGetTraceAsync();

        ImageDifferenceResult active = ImageAssertions.Compare(baseline, translated, difference);
        ImageDifferenceResult afterSeek =
            ImageAssertions.Compare(postSeekBaseline, postSeek, postSeekDifference);
        bool staleAbsent =
            afterSeek.SubtitleBandDifferences < 100 &&
            afterSeek.OutsideDifferenceRatio <= 0.02;
        _metrics["qtActiveDifference"] = active;
        _metrics["qtPostSeekDifference"] = afterSeek;
        _metrics["postSeekStaleCueAbsent"] = staleAbsent;
        if (options.Fake && !workerTrace.Any(line =>
                line.Contains("event=fake_cue", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Fake-worker trace did not record its deterministic cue.");
        }
        return active.Passed && staleAbsent && _qtMetadata is { Unobscured: true };
    }

    private void CaptureLibVlcSnapshot(
        string outputPath,
        WorkerSession? worker,
        out string[] pluginTrace)
    {
        pluginTrace = [];
        Core.Initialize(options.VlcRoot);
        string? isolatedConfiguration = null;
        if (worker != null)
        {
            isolatedConfiguration = Path.Combine(
                Path.GetDirectoryName(outputPath)!,
                "libvlc-visual.conf");
            File.WriteAllText(
                isolatedConfiguration,
                "audio-filter=dotnet_audio_translator" + Environment.NewLine);
        }
        var arguments = new List<string>
        {
            isolatedConfiguration == null
                ? "--ignore-config"
                : $"--config={isolatedConfiguration}",
            isolatedConfiguration == null ? "--ignore-config" : "--no-ignore-config",
            "--no-video-title-show",
            $"--file-caching={options.DelayMilliseconds}",
            $"--network-caching={options.DelayMilliseconds}",
            $"--live-caching={options.DelayMilliseconds}",
            "-vv"
        };
        if (worker != null)
            arguments.AddRange(PluginArguments(worker));
        using var libVlc = new LibVLC(arguments.ToArray());
        var trace = new ConcurrentQueue<string>();
        var fullTrace = new ConcurrentQueue<string>();
        libVlc.Log += (_, eventArgs) =>
        {
            string line = eventArgs.FormattedLog;
            fullTrace.Enqueue(line);
            if (line.Contains("[LiveAudioTranslator]", StringComparison.Ordinal))
                trace.Enqueue(line);
        };
        using var media = new Media(_mediaUri);
        using var player = new MediaPlayer(libVlc, media);
        if (worker != null)
        {
            NativeVlcTestConfiguration.SetMediaPlayerAudioFilter(
                player.NativeReference,
                "dotnet_audio_translator");
        }
        using var failed = new ManualResetEventSlim(false);
        player.EncounteredError += (_, _) => failed.Set();
        try
        {
            if (!player.Play())
                throw new InvalidOperationException("LibVLC refused visual-test playback.");

            var timeout = Stopwatch.StartNew();
            while (player.Time < 5_000 &&
                   timeout.Elapsed < TimeSpan.FromSeconds(45) &&
                   !failed.IsSet)
            {
                Thread.Sleep(25);
            }
            if (failed.IsSet || player.Time < 5_000)
                throw new TimeoutException("LibVLC did not reach the 5-second snapshot point.");
            player.Pause();
            Thread.Sleep(300);
            if (File.Exists(outputPath))
                File.Delete(outputPath);
            if (!player.TakeSnapshot(0, outputPath, 0, 0))
                throw new InvalidOperationException("LibVLC rejected TakeSnapshot.");
            timeout.Restart();
            while (!File.Exists(outputPath) && timeout.Elapsed < TimeSpan.FromSeconds(10))
                Thread.Sleep(25);
            if (!File.Exists(outputPath))
                throw new TimeoutException("LibVLC did not write its PNG snapshot.");
        }
        finally
        {
            player.Stop();
            if (worker != null)
            {
                File.WriteAllLines(
                    Path.Combine(Path.GetDirectoryName(outputPath)!, "libvlc-full-trace.log"),
                    fullTrace);
                pluginTrace = trace.ToArray();
                File.WriteAllLines(
                    Path.Combine(Path.GetDirectoryName(outputPath)!, "snapshot-plugin-trace.log"),
                    pluginTrace);
            }
        }
    }

    private IEnumerable<string> PluginArguments(WorkerSession worker)
    {
        yield return "--live-translator-mode=live-sync";
        yield return $"--live-translator-session={worker.SessionId:D}";
        yield return $"--live-translator-pipe={worker.PipeName}";
        yield return $"--live-translator-input-delay-ms={options.DelayMilliseconds}";
        yield return "--audio-filter=dotnet_audio_translator";
        yield return "--sub-source=dotnet_live_subtitles";
    }

    private void AssertTraceAgreement(string[] pluginTrace, string[] workerTrace)
    {
        bool scheduled = pluginTrace.Any(line =>
            line.Contains("event=subtitle outcome=scheduled", StringComparison.Ordinal));
        bool rendered = pluginTrace.Any(line =>
            line.Contains("event=subtitle outcome=rendered", StringComparison.Ordinal));
        if (!scheduled || !rendered)
            throw new InvalidDataException("Plugin trace did not schedule and render the target cue.");
        if (options.Fake)
        {
            string? workerCue = workerTrace.FirstOrDefault(line =>
                line.Contains("event=fake_cue", StringComparison.Ordinal));
            if (workerCue == null)
                throw new InvalidDataException("Fake worker did not trace its cue.");
            string sequence = ReadField(workerCue, "sequence");
            string generation = ReadField(workerCue, "generation");
            if (!pluginTrace.Any(line =>
                    line.Contains($"sequence={sequence}", StringComparison.Ordinal) &&
                    line.Contains($"generation={generation}", StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    "Plugin and fake-worker trace disagree on cue sequence or generation.");
            }
        }
    }

    private string Artifact(string name)
    {
        string path = Path.Combine(options.ArtifactsDirectory, name);
        _artifacts[Path.GetFileNameWithoutExtension(name)] = path;
        return path;
    }

    private void ValidateInputs()
    {
        foreach (string path in new[]
                 {
                     Path.Combine(options.VlcRoot, "vlc.exe"),
                     options.MediaPath,
                     options.WorkerPath,
                     options.CatalogPath
                 })
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Visual-test input was not found.", path);
        }
    }

    private static string ReadField(string line, string name)
    {
        string prefix = name + "=";
        string? part = line.Split(' ').FirstOrDefault(value =>
            value.StartsWith(prefix, StringComparison.Ordinal));
        return part?[prefix.Length..] ??
            throw new InvalidDataException($"Trace field '{name}' is missing.");
    }
}

internal sealed class QtVlcRun : IAsyncDisposable
{
    private readonly Process _process;
    private readonly int _controlPort;
    private readonly ConcurrentQueue<string> _trace;
    private readonly ConcurrentQueue<string> _allTrace;
    private int _stopped;

    private QtVlcRun(
        Process process,
        int controlPort,
        ConcurrentQueue<string> trace,
        ConcurrentQueue<string> allTrace,
        string command)
    {
        _process = process;
        _controlPort = controlPort;
        _trace = trace;
        _allTrace = allTrace;
        Command = command;
    }

    public int ProcessId => _process.Id;
    public bool HasExited => _process.HasExited;
    public int ExitCode => _process.HasExited ? _process.ExitCode : -1;
    public bool WasForceKilled { get; private set; }
    public string Command { get; }
    public IReadOnlyCollection<string> PipelineTrace => _trace.ToArray();
    public IReadOnlyCollection<string> AllTrace => _allTrace.ToArray();

    public static async Task<QtVlcRun> StartAsync(
        VisualTestOptions options,
        Uri mediaUri,
        WorkerSession? worker)
    {
        int port = ReservePort();
        string gitBash = FindGitBash();
        string vlc = ToGitBashPath(Path.Combine(options.VlcRoot, "vlc.exe"));
        var arguments = new List<string>
        {
            vlc,
            "--intf=qt",
            "--extraintf=rc",
            $"--rc-host=127.0.0.1:{port}",
            $"--file-caching={options.DelayMilliseconds}",
            $"--network-caching={options.DelayMilliseconds}",
            $"--live-caching={options.DelayMilliseconds}",
            "--no-video-title-show",
            "-vv"
        };
        if (worker != null)
        {
            arguments.Add("--live-translator-mode=live-sync");
            arguments.Add($"--live-translator-session={worker.SessionId:D}");
            arguments.Add($"--live-translator-pipe={worker.PipeName}");
            arguments.Add($"--live-translator-input-delay-ms={options.DelayMilliseconds}");
            arguments.Add("--audio-filter=dotnet_audio_translator");
            arguments.Add("--sub-source=dotnet_live_subtitles");
        }
        arguments.Add(mediaUri.AbsoluteUri);
        return await StartCoreAsync(arguments, port);
    }

    public static async Task<QtVlcRun> StartNativeMarqueeAsync(
        string vlcRoot,
        Uri mediaUri,
        string marquee)
    {
        int port = ReservePort();
        string vlc = ToGitBashPath(Path.Combine(vlcRoot, "vlc.exe"));
        var arguments = new List<string>
        {
            vlc,
            "--intf=qt",
            "--extraintf=rc",
            $"--rc-host=127.0.0.1:{port}",
            "--no-video-title-show",
            "--start-time=30",
            "--sub-source=marq",
            $"--marq-marquee={marquee}",
            "--marq-position=8",
            "--marq-size=48",
            "-vvv"
        };
        arguments.Add(mediaUri.AbsoluteUri);
        return await StartCoreAsync(arguments, port);
    }

    public static async Task<QtVlcRun> StartPreparedAsync(
        string vlcRoot,
        Uri mediaUri,
        string cueFile)
    {
        int port = ReservePort();
        string vlc = ToGitBashPath(Path.Combine(vlcRoot, "vlc.exe"));
        var arguments = new List<string>
        {
            vlc,
            "--intf=qt",
            "--extraintf=rc",
            $"--rc-host=127.0.0.1:{port}",
            "--live-translator-mode=sync",
            $"--live-translator-cue-file={ToGitBashPath(cueFile)}",
            "--audio-filter=dotnet_audio_translator",
            "--sub-source=dotnet_live_subtitles",
            "--no-video-title-show",
            "-vvv",
            mediaUri.AbsoluteUri
        };
        return await StartCoreAsync(arguments, port);
    }

    private static async Task<QtVlcRun> StartCoreAsync(
        IReadOnlyCollection<string> arguments,
        int port)
    {
        string gitBash = FindGitBash();
        string command = "exec " + string.Join(' ', arguments.Select(ShellQuote));
        var start = new ProcessStartInfo(gitBash)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-lc");
        start.ArgumentList.Add(command);
        Process process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not launch Qt VLC through Git Bash.");
        var trace = new ConcurrentQueue<string>();
        var allTrace = new ConcurrentQueue<string>();
        process.OutputDataReceived += (_, eventArgs) => Record(eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => Record(eventArgs.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await WaitForControlListenerAsync(process, allTrace, port);
        return new QtVlcRun(process, port, trace, allTrace, command);

        void Record(string? line)
        {
            if (line == null)
                return;
            allTrace.Enqueue(line);
            if (line.Contains("[LiveAudioTranslator]", StringComparison.Ordinal))
                trace.Enqueue(line);
        }
    }

    public async Task SeekAndPauseAsync(int seconds, bool waitForTranslatedCue)
    {
        await SendCommandAsync($"seek {seconds}");
        if (waitForTranslatedCue)
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(30) &&
                   !_trace.Any(line =>
                       line.Contains("event=subtitle outcome=scheduled", StringComparison.Ordinal)))
            {
                if (_process.HasExited)
                    throw new InvalidOperationException($"Qt VLC exited with code {_process.ExitCode}.");
                await Task.Delay(50);
            }
            if (!_trace.Any(line =>
                    line.Contains("event=subtitle outcome=scheduled", StringComparison.Ordinal)))
            {
                throw new TimeoutException("Qt VLC did not schedule the target cue.");
            }
        }
        else
        {
            await Task.Delay(1_000);
        }
        await SendCommandAsync("pause");
        await Task.Delay(400);
    }

    public async Task SeekWhilePausedAsync(int seconds)
    {
        await SendCommandAsync($"seek {seconds}");
        await Task.Delay(800);
    }

    public async Task SeekAsync(int seconds)
    {
        await SendCommandAsync($"seek {seconds}");
        await Task.Delay(250);
    }

    public Task KeepControlAliveAsync() => SendCommandAsync("logout");

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return;
        try
        {
            await SendCommandAsync("quit");
        }
        catch
        {
        }
        Task exit = _process.WaitForExitAsync();
        if (await Task.WhenAny(exit, Task.Delay(TimeSpan.FromSeconds(5))) != exit &&
            !_process.HasExited)
        {
            WasForceKilled = true;
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _process.Dispose();
    }

    private async Task SendCommandAsync(string command)
    {
        using var control = new TcpClient();
        await control.ConnectAsync(IPAddress.Loopback, _controlPort);
        await using var writer = new StreamWriter(control.GetStream())
        {
            AutoFlush = true
        };
        await writer.WriteLineAsync(command);
        await writer.FlushAsync();
        control.Client.Shutdown(SocketShutdown.Send);
    }

    private static async Task WaitForControlListenerAsync(
        Process process,
        ConcurrentQueue<string> trace,
        int port)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(20))
        {
            if (process.HasExited)
                throw new InvalidOperationException($"Qt VLC exited with code {process.ExitCode}.");
            if (trace.Any(line =>
                    line.Contains($"net: listening to 127.0.0.1 port {port}", StringComparison.Ordinal)))
            {
                return;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException("VLC RC control interface did not start.");
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FindGitBash()
    {
        string? configured = Environment.GetEnvironmentVariable("GIT_BASH_PATH");
        string[] candidates =
        [
            configured ?? "",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "usr", "bin", "bash.exe")
        ];
        return candidates.FirstOrDefault(File.Exists) ??
            throw new FileNotFoundException("Git Bash was not found.");
    }

    private static string ToGitBashPath(string path)
    {
        string full = Path.GetFullPath(path).Replace('\\', '/');
        return full.Length >= 3 && full[1] == ':' && full[2] == '/'
            ? $"/{char.ToLowerInvariant(full[0])}/{full[3..]}"
            : full;
    }

    private static string ShellQuote(string value) => $"'{value.Replace("'", "'\\''")}'";
}
