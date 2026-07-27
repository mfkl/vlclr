using System.Diagnostics;
using System.IO.Pipes;
using VLCLR.LiveTranslation.Protocol;

namespace LiveAudioTranslator.ProtocolTests;

public sealed class WorkerLifecycleTests
{
    [Fact]
    public async Task FakeWorkerPrewarmsTransportsCueAndExitsOnPluginEof()
    {
        string repository = FindRepositoryRoot();
        string configuration = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        string workerPath = Path.Combine(
            repository,
            "samples",
            "LiveAudioTranslator.Worker",
            "bin",
            configuration,
            "net10.0",
            "win-x64",
            "LiveAudioTranslator.Worker.exe");
        Assert.True(File.Exists(workerPath), $"Worker was not built: {workerPath}");

        Guid session = Guid.NewGuid();
        string pipeName = $"vlclr-test-{session:N}";
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
        start.ArgumentList.Add(Path.Combine(repository, "missing-is-okay-in-fake-mode.json"));
        using Process worker = Process.Start(start)!;
        Task<string> output = worker.StandardOutput.ReadToEndAsync();
        Task<string> error = worker.StandardError.ReadToEndAsync();
        try
        {
            using (NamedPipeClientStream runner = await ConnectAsync(pipeName))
            {
                await SendHelloAsync(runner, session, LivePeerRole.Runner);
                await ExpectWorkerHelloAsync(runner, session);
                var configure = new LiveConfigureMessage
                {
                    SpeechModelId = "whisper-tiny-multilingual",
                    TranslationModelId = "opus-mt-en-fr",
                    SpeechProviderId = "auto",
                    TranslationProviderId = "auto",
                    SourceLanguage = "auto",
                    TargetLanguage = "fr",
                    SpeechThreads = 2,
                    TranslationThreads = 1,
                    InputDelayMilliseconds = 15_000,
                    VadSilenceMilliseconds = 500,
                    MaximumUtteranceMilliseconds = 6_000,
                    EnergyVadThreshold = 0.012f,
                    FakeInference = true
                };
                await LiveProtocolStream.WriteAsync(
                    runner,
                    LiveProtocol.Create(
                        LiveMessageType.Configure,
                        session,
                        0,
                        0,
                        LiveProtocol.EncodeConfigure(configure)));
                LiveProtocolMessage ready = (await LiveProtocolStream.ReadAsync(runner))!;
                Assert.Equal(LiveMessageType.Ready, ready.Header.MessageType);
                Assert.Equal("fake", LiveProtocol.DecodeReady(ready.Payload).SpeechProviderId);
            }

            using (NamedPipeClientStream plugin = await ConnectAsync(pipeName))
            {
                await SendHelloAsync(plugin, session, LivePeerRole.Plugin);
                await ExpectWorkerHelloAsync(plugin, session);
                await LiveProtocolStream.WriteAsync(
                    plugin,
                    LiveProtocol.Create(LiveMessageType.Flush, session, 3, 0));
                var audio = new LiveAudioMessage
                {
                    Format = LiveAudioSampleFormat.Pcm16LittleEndian,
                    SampleRate = 16_000,
                    Channels = 1,
                    SourcePts = 4_000_000,
                    DurationTicks = 20_000,
                    AudioBytes = [0, 0]
                };
                await LiveProtocolStream.WriteAsync(
                    plugin,
                    LiveProtocol.Create(
                        LiveMessageType.Audio,
                        session,
                        3,
                        7,
                        LiveProtocol.EncodeAudio(audio)));
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                LiveProtocolMessage cue = (await LiveProtocolStream.ReadAsync(plugin, timeout.Token))!;
                Assert.Equal(LiveMessageType.Cue, cue.Header.MessageType);
                Assert.Equal(3, cue.Header.PlaybackGeneration);
                Assert.Equal("VLCLR LIVE SYNC 0042", LiveProtocol.DecodeCue(cue.Payload).Text);
            }

            Task exit = worker.WaitForExitAsync();
            Assert.Same(exit, await Task.WhenAny(exit, Task.Delay(TimeSpan.FromSeconds(5))));
            Assert.Equal(0, worker.ExitCode);
            Assert.Contains("event=worker_stop outcome=clean", await output);
            Assert.DoesNotContain("outcome=failed", await error);
        }
        finally
        {
            if (!worker.HasExited)
                worker.Kill(entireProcessTree: true);
        }
    }

    private static async Task<NamedPipeClientStream> ConnectAsync(string pipeName)
    {
        var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await pipe.ConnectAsync(timeout.Token);
        return pipe;
    }

    private static Task SendHelloAsync(Stream pipe, Guid session, LivePeerRole role) =>
        LiveProtocolStream.WriteAsync(
            pipe,
            LiveProtocol.Create(
                LiveMessageType.Hello,
                session,
                0,
                0,
                LiveProtocol.EncodeHello(role))).AsTask();

    private static async Task ExpectWorkerHelloAsync(Stream pipe, Guid session)
    {
        LiveProtocolMessage hello = (await LiveProtocolStream.ReadAsync(pipe))!;
        Assert.Equal(session, hello.Header.SessionId);
        Assert.Equal(LiveMessageType.Hello, hello.Header.MessageType);
        Assert.Equal(LivePeerRole.Worker, LiveProtocol.DecodeHello(hello.Payload));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "vlclr.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate vlclr.sln.");
    }
}
