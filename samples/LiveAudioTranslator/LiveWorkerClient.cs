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
    private bool _ready;
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

    public bool IsReady
    {
        get
        {
            lock (_stateSync)
                return _ready;
        }
    }

    public bool TryQueueAudio(LiveAudioMessage audio, int generation, long sequence)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return false;

        bool reportFirstAccepted = false;
        bool accepted;
        int dropped;
        lock (_stateSync)
        {
            if (!_ready)
            {
                Interlocked.Increment(ref _discardedNotReadyAudio);
                Interlocked.Add(ref _discardedNotReadyAudioTicks, Math.Max(0, audio.DurationTicks));
                return false;
            }

            accepted = _audioQueue.TryEnqueue(
                new QueuedAudioFrame(audio, generation, sequence),
                out dropped);
            if (accepted && Interlocked.CompareExchange(
                    ref _firstAcceptedAudioPts,
                    audio.SourcePts,
                    -1) == -1)
            {
                reportFirstAccepted = true;
            }
        }

        if (dropped > 0)
        {
            long total = Interlocked.Add(ref _droppedAudio, dropped);
            _onStatus($"event=audio_queue outcome=drop-oldest count={dropped} dropped_audio={total}");
        }
        if (!accepted)
        {
            long total = Interlocked.Increment(ref _droppedAudio);
            _onStatus($"event=audio_queue outcome=drop-oversized dropped_audio={total}");
            return false;
        }
        if (reportFirstAccepted)
        {
            _onStatus(
                $"event=audio_accept outcome=first source_pts={audio.SourcePts} " +
                $"generation={generation}");
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
            signal = _ready;
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
            _ready = false;
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
                _ready = true;
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
            _ready = false;
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
            while (_controlQueue.TryDequeue(out LiveProtocolMessage? control))
                await LiveProtocolStream.WriteAsync(pipe, control, cancellationToken).ConfigureAwait(false);

            if (_audioQueue.TryDequeue(out QueuedAudioFrame frame))
            {
                await LiveProtocolStream.WriteAsync(
                    pipe,
                    LiveProtocol.Create(
                        LiveMessageType.Audio,
                        _sessionId,
                        frame.Generation,
                        frame.Sequence,
                        LiveProtocol.EncodeAudio(frame.Audio)),
                    cancellationToken).ConfigureAwait(false);
            }
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
