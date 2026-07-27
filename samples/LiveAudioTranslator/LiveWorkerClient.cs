using System.Collections.Concurrent;
using System.IO.Pipes;
using VLCLR.LiveTranslation.Protocol;

namespace LiveAudioTranslator;

internal sealed class LiveWorkerClient : IDisposable
{
    private readonly Guid _sessionId;
    private readonly string _pipeName;
    private readonly BoundedAudioTransportQueue _audioQueue;
    private readonly Action<LiveProtocolMessage> _onMessage;
    private readonly Action<string> _onStatus;
    private readonly object _stateSync = new();
    private readonly ConcurrentQueue<LiveProtocolMessage> _controlQueue = new();
    private readonly SemaphoreSlim _available = new(0);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _transport;
    private long _droppedAudio;
    private long _discardedNotReadyAudio;
    private long _discardedNotReadyAudioTicks;
    private long _firstAcceptedAudioPts = -1;
    private int _currentGeneration;
    private long _currentControlSequence;
    private int _ready;
    private int _pendingFirstAcceptedReport;
    private int _firstAcceptedGeneration;
    private long _pendingDroppedOldest;
    private long _pendingDroppedOversized;
    private int _disposed;

    public LiveWorkerClient(
        Guid sessionId,
        string pipeName,
        long queueDurationBudgetTicks,
        Action<LiveProtocolMessage> onMessage,
        Action<string> onStatus)
    {
        if (sessionId == Guid.Empty)
            throw new ArgumentException("Worker session ID is required.", nameof(sessionId));
        if (!IsSafePipeName(pipeName))
            throw new ArgumentException("Worker pipe name is invalid.", nameof(pipeName));
        _sessionId = sessionId;
        _pipeName = pipeName;
        _audioQueue = new BoundedAudioTransportQueue(queueDurationBudgetTicks);
        _onMessage = onMessage;
        _onStatus = onStatus;
        _transport = Task.Run(RunAsync);
    }

    public int QueueDepth => _audioQueue.Count;
    public long QueueDurationTicks => _audioQueue.QueuedDurationTicks;
    public long DroppedAudio => Interlocked.Read(ref _droppedAudio);
    public long DiscardedNotReadyAudio => Interlocked.Read(ref _discardedNotReadyAudio);
    public long DiscardedNotReadyAudioTicks => Interlocked.Read(ref _discardedNotReadyAudioTicks);
    public long FirstAcceptedAudioPts => Interlocked.Read(ref _firstAcceptedAudioPts);

    public bool IsReady => Volatile.Read(ref _ready) != 0;

    public bool TryQueueAudio(
        LiveAudioSampleFormat format,
        int sampleRate,
        ushort channels,
        long sourcePts,
        long durationTicks,
        ReadOnlySpan<byte> audioBytes,
        int generation,
        long sequence)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return false;

        if (Volatile.Read(ref _ready) == 0)
        {
            Interlocked.Increment(ref _discardedNotReadyAudio);
            Interlocked.Add(ref _discardedNotReadyAudioTicks, Math.Max(0, durationTicks));
            return false;
        }

        bool accepted = _audioQueue.TryEnqueue(
            new QueuedAudioMetadata(
                format,
                sampleRate,
                channels,
                sourcePts,
                durationTicks,
                generation,
                sequence),
            audioBytes,
            out int dropped);
        if (dropped > 0)
        {
            Interlocked.Add(ref _droppedAudio, dropped);
            Interlocked.Add(ref _pendingDroppedOldest, dropped);
        }
        if (!accepted)
        {
            Interlocked.Increment(ref _droppedAudio);
            Interlocked.Increment(ref _pendingDroppedOversized);
            _available.Release();
            return false;
        }
        if (Interlocked.CompareExchange(
                ref _firstAcceptedAudioPts,
                sourcePts,
                -1) == -1)
        {
            Volatile.Write(ref _firstAcceptedGeneration, generation);
            Volatile.Write(ref _pendingFirstAcceptedReport, 1);
        }
        _available.Release();
        return true;
    }

    public void ReportNotReadyAudio(long durationTicks)
    {
        Interlocked.Increment(ref _discardedNotReadyAudio);
        Interlocked.Add(ref _discardedNotReadyAudioTicks, Math.Max(0, durationTicks));
    }

    public void Flush(int generation, long sequence)
    {
        bool signal;
        lock (_stateSync)
        {
            _currentGeneration = generation;
            _currentControlSequence = sequence;
            int cleared = _audioQueue.Clear();
            if (cleared > 0)
                Interlocked.Add(ref _droppedAudio, cleared);
            signal = Volatile.Read(ref _ready) != 0;
            if (signal)
            {
                _controlQueue.Enqueue(
                    LiveProtocol.Create(LiveMessageType.Flush, _sessionId, generation, sequence));
            }
        }
        if (signal)
            _available.Release();
    }

    private async Task RunAsync()
    {
        int attempt = 0;
        while (!_shutdown.IsCancellationRequested)
        {
            CloseReadyGate();
            try
            {
                using var pipe = new NamedPipeClientStream(
                    ".",
                    _pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);
                using var connection = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(connection.Token);
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(15));
                await pipe.ConnectAsync(connectTimeout.Token).ConfigureAwait(false);

                await LiveProtocolStream.WriteAsync(
                    pipe,
                    LiveProtocol.Create(
                        LiveMessageType.Hello,
                        _sessionId,
                        0,
                        0,
                        LiveProtocol.EncodeHello(LivePeerRole.Plugin)),
                    connection.Token).ConfigureAwait(false);
                LiveProtocolMessage hello = await LiveProtocolStream.ReadAsync(
                    pipe,
                    connection.Token).ConfigureAwait(false)
                    ?? throw new EndOfStreamException("Worker closed before its hello response.");
                ValidateSession(hello);
                if (hello.Header.MessageType != LiveMessageType.Hello ||
                    LiveProtocol.DecodeHello(hello.Payload) != LivePeerRole.Worker)
                {
                    throw new InvalidDataException("Worker did not return the expected hello.");
                }
                await OpenReadyGateAsync(pipe, attempt, connection.Token).ConfigureAwait(false);

                Task writer = WriteLoopAsync(pipe, connection.Token);
                Task receiver = ReceiveLoopAsync(pipe, connection.Token);
                await Task.WhenAny(writer, receiver).ConfigureAwait(false);
                connection.Cancel();
                await Task.WhenAll(
                    IgnoreCancellation(writer),
                    IgnoreCancellation(receiver)).ConfigureAwait(false);
                CloseReadyGate();
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                CloseReadyGate();
                _onStatus(
                    $"event=worker_failure attempt={attempt} " +
                    $"error={Sanitize($"{ex.GetType().Name}:{ex.Message}")}");
            }
            attempt++;
            if (!_shutdown.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(250, _shutdown.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
    }

    private async Task OpenReadyGateAsync(
        Stream pipe,
        int attempt,
        CancellationToken cancellationToken)
    {
        lock (_stateSync)
        {
            Volatile.Write(ref _ready, 0);
            int cleared = _audioQueue.Clear();
            if (cleared > 0)
                Interlocked.Add(ref _droppedAudio, cleared);
            while (_controlQueue.TryDequeue(out _))
            {
            }
        }
        while (_available.Wait(0))
        {
        }

        int generation;
        long sequence;
        while (true)
        {
            lock (_stateSync)
            {
                generation = _currentGeneration;
                sequence = _currentControlSequence;
            }
            await LiveProtocolStream.WriteAsync(
                pipe,
                LiveProtocol.Create(
                    LiveMessageType.Flush,
                    _sessionId,
                    generation,
                    sequence),
                cancellationToken).ConfigureAwait(false);
            lock (_stateSync)
            {
                if (generation != _currentGeneration || sequence != _currentControlSequence)
                    continue;
                Volatile.Write(ref _ready, 1);
                break;
            }
        }

        _onStatus(
            $"event=transport outcome=ready attempt={attempt} " +
            $"queue_budget_ticks={_audioQueue.DurationBudgetTicks} generation={generation}");
        _onStatus(
            $"event=audio_gate outcome=ready discarded_blocks={DiscardedNotReadyAudio} " +
            $"discarded_ticks={DiscardedNotReadyAudioTicks} generation={generation}");
    }

    private void CloseReadyGate()
    {
        lock (_stateSync)
        {
            Volatile.Write(ref _ready, 0);
            int cleared = _audioQueue.Clear();
            if (cleared > 0)
                Interlocked.Add(ref _droppedAudio, cleared);
            while (_controlQueue.TryDequeue(out _))
            {
            }
        }
    }

    private async Task WriteLoopAsync(Stream pipe, CancellationToken cancellationToken)
    {
        while (true)
        {
            await _available.WaitAsync(cancellationToken).ConfigureAwait(false);
            ReportPendingAudioStatus();
            while (_controlQueue.TryDequeue(out LiveProtocolMessage? control))
                await LiveProtocolStream.WriteAsync(pipe, control, cancellationToken).ConfigureAwait(false);

            if (_audioQueue.TryDequeue(
                    out BoundedAudioTransportQueue.DequeuedAudioFrame frame))
            {
                QueuedAudioMetadata metadata = frame.Metadata;
                byte[] payload;
                try
                {
                    payload = LiveProtocol.EncodeAudio(
                        metadata.Format,
                        metadata.SampleRate,
                        metadata.Channels,
                        metadata.SourcePts,
                        metadata.DurationTicks,
                        frame.AudioBytes);
                }
                finally
                {
                    frame.Dispose();
                }
                await LiveProtocolStream.WriteAsync(
                    pipe,
                    LiveProtocol.Create(
                        LiveMessageType.Audio,
                        _sessionId,
                        metadata.Generation,
                        metadata.Sequence,
                        payload),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void ReportPendingAudioStatus()
    {
        if (Interlocked.Exchange(ref _pendingFirstAcceptedReport, 0) != 0)
        {
            _onStatus(
                $"event=audio_accept outcome=first source_pts={FirstAcceptedAudioPts} " +
                $"generation={Volatile.Read(ref _firstAcceptedGeneration)}");
        }

        long droppedOldest = Interlocked.Exchange(ref _pendingDroppedOldest, 0);
        if (droppedOldest > 0)
        {
            _onStatus(
                $"event=audio_queue outcome=drop-oldest count={droppedOldest} " +
                $"dropped_audio={DroppedAudio}");
        }

        long droppedOversized = Interlocked.Exchange(ref _pendingDroppedOversized, 0);
        if (droppedOversized > 0)
        {
            _onStatus(
                $"event=audio_queue outcome=drop-oversized count={droppedOversized} " +
                $"dropped_audio={DroppedAudio}");
        }
    }

    private async Task ReceiveLoopAsync(Stream pipe, CancellationToken cancellationToken)
    {
        while (true)
        {
            LiveProtocolMessage message = await LiveProtocolStream.ReadAsync(pipe, cancellationToken).ConfigureAwait(false)
                ?? throw new EndOfStreamException("Worker transport closed.");
            ValidateSession(message);
            if (message.Header.MessageType is
                LiveMessageType.Cue or LiveMessageType.Metrics or LiveMessageType.Error)
            {
                _onMessage(message);
            }
            else
            {
                throw new InvalidDataException(
                    $"Unexpected worker message {message.Header.MessageType} during playback.");
            }
        }
    }

    private void ValidateSession(LiveProtocolMessage message)
    {
        if (message.Header.SessionId != _sessionId)
            throw new InvalidDataException("Worker returned a message for another session.");
    }

    private static async Task IgnoreCancellation(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        CloseReadyGate();
        _shutdown.Cancel();
        _available.Release();
    }

    private static bool IsSafePipeName(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 128 &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static string Sanitize(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Replace(' ', '-');
}
