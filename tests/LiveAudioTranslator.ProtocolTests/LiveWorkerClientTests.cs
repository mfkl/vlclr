using System.Collections.Concurrent;
using System.IO.Pipes;
using VLCLR.LiveTranslation.Protocol;

namespace LiveAudioTranslator.ProtocolTests;

public sealed class LiveWorkerClientTests
{
    [Fact]
    public async Task RejectsWarmupAudioAndFlushesCurrentGenerationBeforeOpeningGate()
    {
        Guid session = Guid.NewGuid();
        string pipeName = $"vlclr-client-test-{session:N}";
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var statuses = new ConcurrentQueue<string>();
        using var client = new LiveAudioTranslator.LiveWorkerClient(
            session,
            pipeName,
            queueDurationBudgetTicks: 2_000_000,
            _ => { },
            statuses.Enqueue);

        client.Flush(generation: 7, sequence: 11);
        Assert.False(QueueAudio(client, 1_000_000, 7, 1));
        Assert.False(client.IsReady);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await server.WaitForConnectionAsync(timeout.Token);
        LiveProtocolMessage hello = (await LiveProtocolStream.ReadAsync(server, timeout.Token))!;
        Assert.Equal(LiveMessageType.Hello, hello.Header.MessageType);
        Assert.Equal(LivePeerRole.Plugin, LiveProtocol.DecodeHello(hello.Payload));

        await Task.Delay(100, timeout.Token);
        Assert.False(client.IsReady);
        await LiveProtocolStream.WriteAsync(
            server,
            LiveProtocol.Create(
                LiveMessageType.Hello,
                session,
                0,
                0,
                LiveProtocol.EncodeHello(LivePeerRole.Worker)),
            timeout.Token);

        LiveProtocolMessage flush = (await LiveProtocolStream.ReadAsync(server, timeout.Token))!;
        Assert.Equal(LiveMessageType.Flush, flush.Header.MessageType);
        Assert.Equal(7, flush.Header.PlaybackGeneration);
        Assert.Equal(11, flush.Header.Sequence);
        await WaitUntilAsync(() => client.IsReady, timeout.Token);

        Assert.Equal(1, client.DiscardedNotReadyAudio);
        Assert.Equal(20_000, client.DiscardedNotReadyAudioTicks);
        Assert.True(QueueAudio(client, 8_000_000, 7, 2));

        LiveProtocolMessage audio = (await LiveProtocolStream.ReadAsync(server, timeout.Token))!;
        Assert.Equal(LiveMessageType.Audio, audio.Header.MessageType);
        Assert.Equal(7, audio.Header.PlaybackGeneration);
        Assert.Equal(2, audio.Header.Sequence);
        Assert.Equal(8_000_000, LiveProtocol.DecodeAudio(audio.Payload).SourcePts);
        Assert.Equal(8_000_000, client.FirstAcceptedAudioPts);
        Assert.Contains(
            statuses,
            status => status.Contains(
                "event=audio_gate outcome=ready discarded_blocks=1",
                StringComparison.Ordinal));
    }

    private static bool QueueAudio(
        LiveAudioTranslator.LiveWorkerClient client,
        long sourcePts,
        int generation,
        long sequence) =>
        client.TryQueueAudio(
            LiveAudioSampleFormat.Pcm16LittleEndian,
            16_000,
            1,
            sourcePts,
            20_000,
            new byte[] { 0, 0 },
            generation,
            sequence);

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        CancellationToken cancellationToken)
    {
        while (!condition())
            await Task.Delay(10, cancellationToken);
    }
}
