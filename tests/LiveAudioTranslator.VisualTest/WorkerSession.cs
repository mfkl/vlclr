using System.Diagnostics;
using System.IO.Pipes;
using VLCLR.LiveTranslation.Protocol;

namespace LiveAudioTranslator.VisualTest;

internal sealed class WorkerSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task<string> _stdout;
    private readonly Task<string> _stderr;

    private WorkerSession(
        Guid sessionId,
        string pipeName,
        Process process,
        Task<string> stdout,
        Task<string> stderr,
        LiveReadyMessage ready)
    {
        SessionId = sessionId;
        PipeName = pipeName;
        _process = process;
        _stdout = stdout;
        _stderr = stderr;
        Ready = ready;
    }

    public Guid SessionId { get; }
    public string PipeName { get; }
    public LiveReadyMessage Ready { get; }

    public static async Task<WorkerSession> StartAsync(VisualTestOptions options)
    {
        Guid session = Guid.NewGuid();
        string pipeName = $"vlclr-visual-{session:N}";
        var start = new ProcessStartInfo(options.WorkerPath)
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
        Process process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start visual-test worker.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await pipe.ConnectAsync(timeout.Token);
            await LiveProtocolStream.WriteAsync(
                pipe,
                LiveProtocol.Create(
                    LiveMessageType.Hello,
                    session,
                    0,
                    0,
                    LiveProtocol.EncodeHello(LivePeerRole.Runner)),
                timeout.Token);
            LiveProtocolMessage hello = await Required(pipe, timeout.Token);
            Validate(hello, session);
            if (hello.Header.MessageType != LiveMessageType.Hello)
                throw new InvalidDataException("Visual worker hello was not returned.");
            var configuration = new LiveConfigureMessage
            {
                SpeechModelId = "whisper-tiny-multilingual",
                TranslationModelId = "opus-mt-en-fr",
                SpeechProviderId = "auto",
                SpeechDeviceId = "cpu",
                TranslationProviderId = "auto",
                SourceLanguage = "auto",
                TargetLanguage = "fr",
                SpeechThreads = 2,
                TranslationThreads = 1,
                InputDelayMilliseconds = options.DelayMilliseconds,
                VadSilenceMilliseconds = 500,
                MaximumUtteranceMilliseconds = 6_000,
                EnergyVadThreshold = 0.012f,
                FakeInference = options.Fake
            };
            await LiveProtocolStream.WriteAsync(
                pipe,
                LiveProtocol.Create(
                    LiveMessageType.Configure,
                    session,
                    0,
                    0,
                    LiveProtocol.EncodeConfigure(configuration)),
                timeout.Token);
            LiveProtocolMessage response = await Required(pipe, timeout.Token);
            Validate(response, session);
            if (response.Header.MessageType != LiveMessageType.Ready)
                throw new InvalidDataException($"Worker returned {response.Header.MessageType}, not READY.");
            return new WorkerSession(
                session,
                pipeName,
                process,
                stdout,
                stderr,
                LiveProtocol.DecodeReady(response.Payload));
        }
        catch
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.Dispose();
            throw;
        }
    }

    public async Task<string[]> StopAndGetTraceAsync()
    {
        if (!_process.HasExited)
        {
            Task exit = _process.WaitForExitAsync();
            if (await Task.WhenAny(exit, Task.Delay(TimeSpan.FromSeconds(5))) != exit)
                _process.Kill(entireProcessTree: true);
        }
        if (!_process.HasExited)
            await _process.WaitForExitAsync();
        string combined = await _stdout + Environment.NewLine + await _stderr;
        return combined.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _ = await StopAndGetTraceAsync();
        }
        finally
        {
            _process.Dispose();
        }
    }

    private static async Task<LiveProtocolMessage> Required(
        Stream stream,
        CancellationToken cancellationToken) =>
        await LiveProtocolStream.ReadAsync(stream, cancellationToken)
            ?? throw new EndOfStreamException("Visual worker disconnected.");

    private static void Validate(LiveProtocolMessage message, Guid session)
    {
        if (message.Header.SessionId != session)
            throw new InvalidDataException("Visual worker returned another session.");
    }
}
