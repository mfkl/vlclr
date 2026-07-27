using System.IO.Pipes;
using VLCLR.LiveTranslation.Protocol;

namespace LiveAudioTranslator.Worker;

internal sealed class WorkerHost(WorkerCommandLine options)
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task RunAsync()
    {
        Console.WriteLine($"event=worker_start session={options.SessionId:N}");
        LiveConfigureMessage configuration;
        WorkerPipeline pipeline;
        LiveReadyMessage ready;

        await using (NamedPipeServerStream runnerPipe = CreatePipe())
        {
            await runnerPipe.WaitForConnectionAsync().ConfigureAwait(false);
            await ExpectHelloAsync(runnerPipe, LivePeerRole.Runner).ConfigureAwait(false);
            await SendAsync(
                runnerPipe,
                LiveProtocol.Create(
                    LiveMessageType.Hello,
                    options.SessionId,
                    0,
                    0,
                    LiveProtocol.EncodeHello(LivePeerRole.Worker)),
                CancellationToken.None).ConfigureAwait(false);
            LiveProtocolMessage configureMessage =
                await ReadRequiredAsync(runnerPipe, CancellationToken.None).ConfigureAwait(false);
            ValidateSession(configureMessage);
            if (configureMessage.Header.MessageType != LiveMessageType.Configure)
                throw new InvalidDataException("Runner did not send configure.");
            configuration = LiveProtocol.DecodeConfigure(configureMessage.Payload);
            try
            {
                if (configuration.FakeInference && options.FakeReadyDelayMilliseconds > 0)
                {
                    Console.WriteLine(
                        $"event=worker_init stage=fake-delay " +
                        $"delay_ms={options.FakeReadyDelayMilliseconds}");
                    await Task.Delay(options.FakeReadyDelayMilliseconds).ConfigureAwait(false);
                }
                (pipeline, ready) = await WorkerPipeline.CreateAsync(
                    options.CatalogPath,
                    configuration,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await SendAsync(
                    runnerPipe,
                    LiveProtocol.Create(
                        LiveMessageType.Error,
                        options.SessionId,
                        0,
                        0,
                        LiveProtocol.EncodeError(new LiveErrorMessage
                        {
                            Code = "initialization-failed",
                            Message = WorkerLog.Sanitize($"{ex.GetType().Name}:{ex.Message}"),
                            Fatal = true
                        })),
                    CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            await SendAsync(
                runnerPipe,
                LiveProtocol.Create(
                    LiveMessageType.Ready,
                    options.SessionId,
                    0,
                    0,
                    LiveProtocol.EncodeReady(ready)),
                CancellationToken.None).ConfigureAwait(false);
        }

        Console.WriteLine(
            $"event=ready speech_model={ready.SpeechModelId} translation_model={ready.TranslationModelId} " +
            $"speech_provider={ready.SpeechProviderId} translation_provider={ready.TranslationProviderId} " +
            $"model_init_ms={ready.InitializationMilliseconds} warmup_ms={ready.WarmupMilliseconds}");
        await using (pipeline.ConfigureAwait(false))
        await using (NamedPipeServerStream pluginPipe = CreatePipe())
        {
            await pluginPipe.WaitForConnectionAsync().ConfigureAwait(false);
            await ExpectHelloAsync(pluginPipe, LivePeerRole.Plugin).ConfigureAwait(false);
            await SendAsync(
                pluginPipe,
                LiveProtocol.Create(
                    LiveMessageType.Hello,
                    options.SessionId,
                    0,
                    0,
                    LiveProtocol.EncodeHello(LivePeerRole.Worker)),
                CancellationToken.None).ConfigureAwait(false);

            using var disconnected = new CancellationTokenSource();
            pipeline.CueReady = async (generation, sequence, cue) =>
            {
                await SendAsync(
                    pluginPipe,
                    LiveProtocol.Create(
                        LiveMessageType.Cue,
                        options.SessionId,
                        generation,
                        sequence,
                        LiveProtocol.EncodeCue(cue)),
                    disconnected.Token).ConfigureAwait(false);
            };
            pipeline.MetricsReady = async metrics =>
            {
                await SendAsync(
                    pluginPipe,
                    LiveProtocol.Create(
                        LiveMessageType.Metrics,
                        options.SessionId,
                        playbackGeneration: 0,
                        sequence: 0,
                        LiveProtocol.EncodeMetrics(metrics)),
                    disconnected.Token).ConfigureAwait(false);
            };

            try
            {
                await PlaybackLoopAsync(pluginPipe, pipeline, disconnected.Token).ConfigureAwait(false);
            }
            finally
            {
                disconnected.Cancel();
            }
        }
        Console.WriteLine("event=worker_stop outcome=clean");
    }

    private async Task PlaybackLoopAsync(
        Stream pipe,
        WorkerPipeline pipeline,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            LiveProtocolMessage? message =
                await LiveProtocolStream.ReadAsync(pipe, cancellationToken).ConfigureAwait(false);
            if (message == null)
                return;
            ValidateSession(message);
            switch (message.Header.MessageType)
            {
                case LiveMessageType.Audio:
                    pipeline.PushAudio(
                        LiveProtocol.DecodeAudio(message.Payload),
                        message.Header.PlaybackGeneration);
                    break;
                case LiveMessageType.Flush:
                    if (message.Payload.Length != 0)
                        throw new InvalidDataException("Flush payload must be empty.");
                    pipeline.Flush(message.Header.PlaybackGeneration);
                    break;
                case LiveMessageType.Shutdown:
                    return;
                default:
                    throw new InvalidDataException(
                        $"Unexpected plugin message {message.Header.MessageType}.");
            }
        }
    }

    private NamedPipeServerStream CreatePipe() =>
        new(
            options.PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            LiveProtocol.MaximumPayloadBytes + LiveProtocol.HeaderSize,
            LiveProtocol.MaximumPayloadBytes + LiveProtocol.HeaderSize);

    private async Task ExpectHelloAsync(Stream pipe, LivePeerRole role)
    {
        LiveProtocolMessage hello = await ReadRequiredAsync(pipe, CancellationToken.None).ConfigureAwait(false);
        ValidateSession(hello);
        if (hello.Header.MessageType != LiveMessageType.Hello ||
            LiveProtocol.DecodeHello(hello.Payload) != role)
        {
            throw new InvalidDataException($"Expected hello from {role}.");
        }
    }

    private static async Task<LiveProtocolMessage> ReadRequiredAsync(
        Stream pipe,
        CancellationToken cancellationToken) =>
        await LiveProtocolStream.ReadAsync(pipe, cancellationToken).ConfigureAwait(false)
            ?? throw new EndOfStreamException("Peer disconnected.");

    private async ValueTask SendAsync(
        Stream pipe,
        LiveProtocolMessage message,
        CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LiveProtocolStream.WriteAsync(pipe, message, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void ValidateSession(LiveProtocolMessage message)
    {
        if (message.Header.SessionId != options.SessionId)
            throw new InvalidDataException("Peer used the wrong session ID.");
    }
}
