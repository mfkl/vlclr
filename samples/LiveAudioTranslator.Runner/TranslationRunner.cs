using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using VLCLR.LiveTranslation.Protocol;

namespace LiveAudioTranslator.Runner;

internal static class TranslationRunner
{
    public static async Task<int> RunAsync(RunnerOptions options)
    {
        string vlcExecutable = Path.Combine(options.VlcRoot, "vlc.exe");
        if (!File.Exists(vlcExecutable))
            throw new FileNotFoundException("VLC executable was not found.", vlcExecutable);

        if (options.Mode == RunnerMode.Prepared)
            return await RunPreparedAsync(options).ConfigureAwait(false);

        BenchmarkDecision benchmark = BenchmarkDecision.Read(options.BenchmarkProfilePath);
        string initialWorkerPath =
            benchmark.Qualified ||
            string.Equals(options.WorkerPath, options.CpuWorkerPath, StringComparison.OrdinalIgnoreCase)
                ? options.WorkerPath
                : options.CpuWorkerPath;
        if (!string.Equals(initialWorkerPath, options.WorkerPath, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(
                "event=provider_fallback reason=no-qualified-accelerated-profile fallback=cpu");
        }

        var startup = Stopwatch.StartNew();
        Guid session = Guid.NewGuid();
        string pipeName = $"vlclr-live-{session:N}";
        bool initialIsFallbackCpu = !string.Equals(
            initialWorkerPath,
            options.WorkerPath,
            StringComparison.OrdinalIgnoreCase);
        Process? worker = null;
        Task<string>? workerOutput = null;
        Task<string>? workerError = null;
        Task<LiveReadyMessage>? readiness = null;
        bool workerStartupFailed = false;
        var configurationSent = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            if (!File.Exists(initialWorkerPath))
            {
                workerStartupFailed = true;
                Console.Error.WriteLine(
                    $"event=worker_failure outcome=no-new-subtitles reason=worker-not-found " +
                    $"path={RunnerLog.Sanitize(initialWorkerPath)}");
            }
            else if (!File.Exists(options.CatalogPath))
            {
                workerStartupFailed = true;
                Console.Error.WriteLine(
                    $"event=worker_failure outcome=no-new-subtitles reason=catalog-not-found " +
                    $"path={RunnerLog.Sanitize(options.CatalogPath)}");
            }
            else
            {
                worker = StartWorker(initialWorkerPath, options, session, pipeName);
                workerOutput = worker.StandardOutput.ReadToEndAsync();
                workerError = worker.StandardError.ReadToEndAsync();
                readiness = ConfigureWorkerAsync(
                    options,
                    session,
                    pipeName,
                    delay: 0,
                    forceCpu: initialIsFallbackCpu,
                    configurationSent);
                Console.WriteLine(
                    $"event=runner stage=worker-started elapsed_ms={startup.ElapsedMilliseconds} " +
                    $"session={session:N}");

                Task configurationGate = await Task.WhenAny(
                    configurationSent.Task,
                    Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
                if (configurationGate == configurationSent.Task)
                {
                    try
                    {
                        await configurationSent.Task.ConfigureAwait(false);
                        Console.WriteLine(
                            $"event=runner stage=worker-configured elapsed_ms={startup.ElapsedMilliseconds}");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(
                            $"event=worker_failure outcome=no-new-subtitles stage=configure " +
                            $"error={RunnerLog.Sanitize($"{ex.GetType().Name}:{ex.Message}")}");
                    }
                }
                else
                {
                    Console.WriteLine(
                        $"event=runner stage=worker-configure-pending elapsed_ms={startup.ElapsedMilliseconds}");
                }
            }

            using Process vlc = StartVlc(options, session, pipeName);
            Console.WriteLine(
                $"event=runner stage=vlc-started elapsed_ms={startup.ElapsedMilliseconds} pid={vlc.Id}");
            Task vlcExit = vlc.WaitForExitAsync();
            bool workerFailedDuringPlayback = workerStartupFailed;

            if (worker != null && readiness != null)
            {
                Task first = await Task.WhenAny(readiness, vlcExit).ConfigureAwait(false);
                if (first == readiness)
                {
                    try
                    {
                        LiveReadyMessage ready = await readiness.ConfigureAwait(false);
                        Console.WriteLine(
                            $"event=worker_ready elapsed_ms={startup.ElapsedMilliseconds} " +
                            $"speech_model={ready.SpeechModelId} " +
                            $"translation_model={ready.TranslationModelId} " +
                            $"speech_provider={ready.SpeechProviderId} " +
                            $"speech_device={options.SpeechDeviceId} " +
                            $"translation_provider={ready.TranslationProviderId} " +
                            $"init_ms={ready.InitializationMilliseconds} " +
                            $"warmup_ms={ready.WarmupMilliseconds} " +
                            $"fallback={RunnerLog.Sanitize(ready.ProviderFallbackReason)}");
                    }
                    catch (Exception ex)
                    {
                        workerFailedDuringPlayback = true;
                        Console.Error.WriteLine(
                            $"event=worker_failure outcome=no-new-subtitles stage=ready " +
                            $"error={RunnerLog.Sanitize($"{ex.GetType().Name}:{ex.Message}")}");
                    }
                }
                else
                {
                    Console.WriteLine(
                        "event=worker_ready outcome=cancelled reason=playback-ended-before-ready");
                }

                if (!workerFailedDuringPlayback && !vlc.HasExited)
                {
                    Task workerExit = worker.WaitForExitAsync();
                    if (await Task.WhenAny(workerExit, vlcExit).ConfigureAwait(false) == workerExit &&
                        !vlc.HasExited)
                    {
                        workerFailedDuringPlayback = true;
                        Console.Error.WriteLine(
                            "event=worker_failure outcome=no-new-subtitles reason=worker-exited");
                    }
                }
            }

            await vlcExit.ConfigureAwait(false);
            if (worker != null && !worker.HasExited)
            {
                Task cleanExit = worker.WaitForExitAsync();
                if (await Task.WhenAny(
                        cleanExit,
                        Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false) != cleanExit)
                {
                    TryTerminate(worker);
                }
            }

            int workerExitCode = 0;
            if (worker != null)
            {
                if (!worker.HasExited)
                    await worker.WaitForExitAsync().ConfigureAwait(false);
                workerExitCode = worker.ExitCode;
                foreach (string line in SplitSafeWorkerLines(
                             await workerOutput!.ConfigureAwait(false),
                             await workerError!.ConfigureAwait(false)))
                {
                    Console.WriteLine($"[worker] {line}");
                }
            }
            Console.WriteLine(
                $"event=runner outcome=stopped vlc_exit={vlc.ExitCode} worker_exit={workerExitCode} " +
                $"worker_failed={workerFailedDuringPlayback}");
            return vlc.ExitCode == 0 && workerExitCode == 0 && !workerFailedDuringPlayback ? 0 : 1;
        }
        finally
        {
            if (worker != null && !worker.HasExited)
                TryTerminate(worker);
            worker?.Dispose();
        }
    }

    private static async Task<LiveReadyMessage> ConfigureWorkerAsync(
        RunnerOptions options,
        Guid session,
        string pipeName,
        int delay,
        bool forceCpu,
        TaskCompletionSource<bool>? configurationSent = null)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            await LiveProtocolStream.WriteAsync(
                pipe,
                LiveProtocol.Create(
                    LiveMessageType.Hello,
                    session,
                    0,
                    0,
                    LiveProtocol.EncodeHello(LivePeerRole.Runner)),
                timeout.Token).ConfigureAwait(false);
            LiveProtocolMessage hello = await ReadRequiredAsync(pipe, timeout.Token).ConfigureAwait(false);
            ValidateSession(hello, session);
            if (hello.Header.MessageType != LiveMessageType.Hello ||
                LiveProtocol.DecodeHello(hello.Payload) != LivePeerRole.Worker)
            {
                throw new InvalidDataException("Worker hello was invalid.");
            }

            var configuration = new LiveConfigureMessage
            {
                SpeechModelId = options.SpeechModelId,
                TranslationModelId = options.TranslationModelId,
                SpeechProviderId = forceCpu ? "cpu" : options.SpeechProviderId,
                SpeechDeviceId = forceCpu ? "cpu" : options.SpeechDeviceId,
                TranslationProviderId = forceCpu ? "cpu" : options.TranslationProviderId,
                SourceLanguage = options.SourceLanguage,
                TargetLanguage = options.TargetLanguage,
                SpeechThreads = 2,
                TranslationThreads = 1,
                InputDelayMilliseconds = delay,
                VadSilenceMilliseconds = 450,
                MaximumUtteranceMilliseconds = 2_500,
                EnergyVadThreshold = 0.012f,
                FakeInference = options.FakeInference
            };
            await LiveProtocolStream.WriteAsync(
                pipe,
                LiveProtocol.Create(
                    LiveMessageType.Configure,
                    session,
                    0,
                    0,
                    LiveProtocol.EncodeConfigure(configuration)),
                timeout.Token).ConfigureAwait(false);
            configurationSent?.TrySetResult(true);
            LiveProtocolMessage response = await ReadRequiredAsync(pipe, timeout.Token).ConfigureAwait(false);
            ValidateSession(response, session);
            if (response.Header.MessageType == LiveMessageType.Error)
            {
                LiveErrorMessage error = LiveProtocol.DecodeError(response.Payload);
                throw new InvalidOperationException(
                    $"Worker configuration failed: {error.Code}: {error.Message}");
            }
            if (response.Header.MessageType != LiveMessageType.Ready)
                throw new InvalidDataException($"Expected READY, received {response.Header.MessageType}.");
            return LiveProtocol.DecodeReady(response.Payload);
        }
        catch (Exception exception)
        {
            configurationSent?.TrySetException(exception);
            throw;
        }
    }

    private static Process StartWorker(
        string workerPath,
        RunnerOptions options,
        Guid session,
        string pipeName)
    {
        var start = new ProcessStartInfo(workerPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("--pipe");
        start.ArgumentList.Add(pipeName);
        start.ArgumentList.Add("--session");
        start.ArgumentList.Add(session.ToString("D"));
        start.ArgumentList.Add("--catalog");
        start.ArgumentList.Add(options.CatalogPath);
        if (options.FakeReadyDelayMilliseconds > 0)
        {
            start.ArgumentList.Add("--fake-ready-delay-ms");
            start.ArgumentList.Add(options.FakeReadyDelayMilliseconds.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        }
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start worker.");
    }

    private static Process StartVlc(
        RunnerOptions options,
        Guid session,
        string pipeName)
    {
        string gitBash = FindGitBash();
        const string mode = "live-immediate";
        var arguments = new List<string>
        {
            ToGitBashPath(Path.Combine(options.VlcRoot, "vlc.exe")),
            $"--live-translator-mode={mode}",
            $"--live-translator-session={session:D}",
            $"--live-translator-pipe={pipeName}",
            $"--live-translator-speech-model={options.SpeechModelId}",
            $"--live-translator-translation-model={options.TranslationModelId}",
            $"--live-translator-speech-provider={options.SpeechProviderId}",
            $"--live-translator-translation-provider={options.TranslationProviderId}",
            $"--live-translator-source-language={options.SourceLanguage}",
            $"--live-translator-target-language={options.TargetLanguage}",
            "--audio-filter=dotnet_audio_translator",
            "--sub-source=dotnet_live_subtitles",
            "--no-video-title-show"
        };
        if (options.SpeechDeviceId is "gpu" or "auto" &&
            !options.ExtraVlcArguments.Contains("--no-hw-dec", StringComparer.Ordinal))
        {
            arguments.Add("--no-hw-dec");
            Console.WriteLine(
                $"event=video_decode policy=software reason=speech-device-{options.SpeechDeviceId}");
        }
        arguments.AddRange(options.ExtraVlcArguments);
        arguments.Add(options.Media);
        string command = "exec " + string.Join(' ', arguments.Select(ShellQuote));
        var start = new ProcessStartInfo(gitBash)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-lc");
        start.ArgumentList.Add(command);
        Process process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start VLC.");
        process.OutputDataReceived += (_, eventArgs) => ForwardVlcLine(eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => ForwardVlcLine(eventArgs.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;

        static void ForwardVlcLine(string? line)
        {
            if (line == null ||
                (!line.Contains("[LiveAudioTranslator]", StringComparison.Ordinal) &&
                 !line.Contains("assertion failed", StringComparison.OrdinalIgnoreCase) &&
                 !line.Contains("segmentation fault", StringComparison.OrdinalIgnoreCase) &&
                 !line.Contains("access violation", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
            Console.WriteLine(line);
        }
    }

    private static async Task<int> RunPreparedAsync(RunnerOptions options)
    {
        Uri media = new(options.Media);
        if (!media.IsFile)
            throw new InvalidOperationException("Prepared mode requires a local file.");
        string repositoryRoot = FindRepositoryRoot(options.VlcRoot);
        string script = Path.Combine(
            repositoryRoot,
            "samples",
            "LiveAudioTranslator",
            "prepare-and-run.ps1");
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add("-VideoPath");
        start.ArgumentList.Add(media.LocalPath);
        start.ArgumentList.Add("-VlcDirectory");
        start.ArgumentList.Add(options.VlcRoot);
        start.Environment["VLCLR_EXTRA_VLC_ARGUMENT_COUNT"] =
            options.ExtraVlcArguments.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (int index = 0; index < options.ExtraVlcArguments.Count; index++)
            start.Environment[$"VLCLR_EXTRA_VLC_ARGUMENT_{index}"] = options.ExtraVlcArguments[index];
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start prepared-mode runner.");
        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode;
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private static IEnumerable<string> SplitSafeWorkerLines(params string[] values) =>
        values.SelectMany(value =>
                value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            .Where(line =>
                line.StartsWith("event=", StringComparison.Ordinal) &&
                !line.Contains(" text=", StringComparison.OrdinalIgnoreCase));

    private static async Task<LiveProtocolMessage> ReadRequiredAsync(
        Stream stream,
        CancellationToken cancellationToken) =>
        await LiveProtocolStream.ReadAsync(stream, cancellationToken).ConfigureAwait(false)
            ?? throw new EndOfStreamException("Worker disconnected.");

    private static void ValidateSession(LiveProtocolMessage message, Guid session)
    {
        if (message.Header.SessionId != session)
            throw new InvalidDataException("Worker returned the wrong session ID.");
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
            throw new FileNotFoundException("Git Bash was not found. Set GIT_BASH_PATH.");
    }

    private static string ToGitBashPath(string path)
    {
        string full = Path.GetFullPath(path).Replace('\\', '/');
        return full.Length >= 3 && full[1] == ':' && full[2] == '/'
            ? $"/{char.ToLowerInvariant(full[0])}/{full[3..]}"
            : full;
    }

    private static string ShellQuote(string value) => $"'{value.Replace("'", "'\\''")}'";

    private static string FindRepositoryRoot(string start)
    {
        DirectoryInfo? directory = new(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "vlclr.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}

internal sealed record BenchmarkDecision(
    double RealTimeFactor,
    long CueLatencyP99Milliseconds,
    bool Qualified)
{
    public static BenchmarkDecision Read(string path)
    {
        if (!File.Exists(path))
            return new BenchmarkDecision(0, 0, false);
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement root = document.RootElement;
            double rtf = root.TryGetProperty("totalRealTimeFactor", out JsonElement rtfElement)
                ? rtfElement.GetDouble()
                : 0;
            long p99 = root.TryGetProperty("cueLatencyP99Milliseconds", out JsonElement p99Element)
                ? p99Element.GetInt64()
                : 0;
            bool qualified = root.TryGetProperty("qualified", out JsonElement qualifiedElement) &&
                qualifiedElement.GetBoolean();
            return new BenchmarkDecision(rtf, p99, qualified);
        }
        catch
        {
            return new BenchmarkDecision(0, 0, false);
        }
    }

    public int SelectDelay(RunnerOptions options)
    {
        long requested = CueLatencyP99Milliseconds > 0
            ? CueLatencyP99Milliseconds + options.SafetyMarginMilliseconds
            : options.InputDelayMilliseconds;
        return (int)Math.Clamp(
            requested,
            options.MinimumDelayMilliseconds,
            options.MaximumDelayMilliseconds);
    }
}
