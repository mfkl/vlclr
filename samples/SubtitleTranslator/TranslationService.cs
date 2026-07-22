using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

namespace SubtitleTranslator;

public enum TranslationOutcome
{
    Translated,
    CacheHit,
    QueueFull,
    DeadlineExceeded,
    Failed,
    ShuttingDown
}

public readonly record struct TranslationResponse(
    TranslationOutcome Outcome,
    string Text,
    bool CacheHit,
    TranslationResult? Details,
    TimeSpan QueueDuration,
    TimeSpan TotalDuration,
    string? ErrorType)
{
    public bool IsSuccess => Outcome is TranslationOutcome.Translated or TranslationOutcome.CacheHit;
}

public sealed record TranslationServiceOptions
{
    public int CacheCapacity { get; init; } = 512;
    public int QueueCapacity { get; init; } = 8;
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Serializes access to a mutable decoder, bounds pending work, and allows a
/// caller deadline to expire without cancelling useful cache population.
/// </summary>
public sealed class TranslationService : IDisposable
{
    private readonly ITranslationEngine _engine;
    private readonly TranslationCache _cache;
    private readonly Channel<WorkItem> _queue;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TranslationResponse>> _inflight =
        new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private readonly TimeSpan _shutdownTimeout;
    private int _disposed;

    public TranslationService(ITranslationEngine engine, TranslationServiceOptions? options = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        options ??= new TranslationServiceOptions();
        if (options.CacheCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.CacheCapacity));
        if (options.QueueCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.QueueCapacity));

        _cache = new TranslationCache(options.CacheCapacity);
        _shutdownTimeout = options.ShutdownTimeout;
        _queue = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(options.QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        _worker = Task.Factory.StartNew(
                WorkerLoopAsync,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)
            .Unwrap();
    }

    public int CachedCueCount => _cache.Count;
    public int PendingCueCount => _inflight.Count;

    public TranslationResponse Translate(string text, TimeSpan deadline)
    {
        long started = Stopwatch.GetTimestamp();
        string normalized = TranslationTextNormalizer.NormalizeCacheKey(text);
        if (normalized.Length == 0)
        {
            return new TranslationResponse(
                TranslationOutcome.CacheHit,
                text,
                true,
                null,
                TimeSpan.Zero,
                Stopwatch.GetElapsedTime(started),
                null);
        }

        if (_cache.TryGet(normalized, out string cached))
        {
            return new TranslationResponse(
                TranslationOutcome.CacheHit,
                cached,
                true,
                null,
                TimeSpan.Zero,
                Stopwatch.GetElapsedTime(started),
                null);
        }

        if (Volatile.Read(ref _disposed) != 0)
            return Failure(TranslationOutcome.ShuttingDown, text, started, nameof(ObjectDisposedException));

        var completion = new TaskCompletionSource<TranslationResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<TranslationResponse> shared = _inflight.GetOrAdd(normalized, completion);
        if (ReferenceEquals(shared, completion))
        {
            var item = new WorkItem(normalized, Stopwatch.GetTimestamp(), completion);
            if (!_queue.Writer.TryWrite(item))
            {
                _inflight.TryRemove(new KeyValuePair<string, TaskCompletionSource<TranslationResponse>>(normalized, completion));
                return Failure(TranslationOutcome.QueueFull, text, started, null);
            }
        }

        if (deadline <= TimeSpan.Zero || !shared.Task.Wait(deadline))
            return Failure(TranslationOutcome.DeadlineExceeded, text, started, null);

        TranslationResponse response = shared.Task.GetAwaiter().GetResult();
        return response with { TotalDuration = Stopwatch.GetElapsedTime(started) };
    }

    private async Task WorkerLoopAsync()
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(_stop.Token).ConfigureAwait(false))
            {
                while (_queue.Reader.TryRead(out WorkItem? item))
                    Process(item);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        finally
        {
            while (_queue.Reader.TryRead(out WorkItem? abandoned))
            {
                Complete(
                    abandoned,
                    new TranslationResponse(
                        TranslationOutcome.ShuttingDown,
                        abandoned.Text,
                        false,
                        null,
                        Stopwatch.GetElapsedTime(abandoned.EnqueuedAt),
                        TimeSpan.Zero,
                        nameof(OperationCanceledException)));
            }

            _engine.Dispose();
            _stop.Dispose();
        }
    }

    private void Process(WorkItem item)
    {
        TimeSpan queueDuration = Stopwatch.GetElapsedTime(item.EnqueuedAt);
        long started = Stopwatch.GetTimestamp();
        TranslationResponse response;
        try
        {
            TranslationResult result = _engine.TranslateDetailed(item.Text);
            _cache.Set(item.Text, result.Text);
            response = new TranslationResponse(
                TranslationOutcome.Translated,
                result.Text,
                false,
                result,
                queueDuration,
                Stopwatch.GetElapsedTime(started),
                null);
        }
        catch (Exception ex)
        {
            response = new TranslationResponse(
                TranslationOutcome.Failed,
                item.Text,
                false,
                null,
                queueDuration,
                Stopwatch.GetElapsedTime(started),
                ex.GetType().Name);
        }

        Complete(item, response);
    }

    private void Complete(WorkItem item, TranslationResponse response)
    {
        _inflight.TryRemove(new KeyValuePair<string, TaskCompletionSource<TranslationResponse>>(item.Text, item.Completion));
        item.Completion.TrySetResult(response);
    }

    private static TranslationResponse Failure(
        TranslationOutcome outcome,
        string originalText,
        long started,
        string? errorType) =>
        new(outcome, originalText, false, null, TimeSpan.Zero, Stopwatch.GetElapsedTime(started), errorType);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _queue.Writer.TryComplete();
        try
        {
            if (!_worker.Wait(_shutdownTimeout))
                _stop.Cancel();
        }
        catch (AggregateException)
        {
            _stop.Cancel();
        }
    }

    private sealed record WorkItem(
        string Text,
        long EnqueuedAt,
        TaskCompletionSource<TranslationResponse> Completion);
}

public sealed record TranslationServiceKey(
    string ModelDirectory,
    string SourceLanguage,
    string TargetLanguage,
    string Provider,
    int IntraOpThreads,
    int MaximumSourceTokens,
    int MaximumOutputTokens,
    int CacheCapacity,
    int QueueCapacity)
{
    public TranslationServiceKey Canonicalize() => this with
    {
        ModelDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ModelDirectory)),
        SourceLanguage = SourceLanguage.ToLowerInvariant(),
        TargetLanguage = TargetLanguage.ToLowerInvariant(),
        Provider = Provider.ToLowerInvariant()
    };
}

public sealed class TranslationServiceLease : IDisposable
{
    private TranslationServiceKey? _key;
    public TranslationService Service { get; }

    internal TranslationServiceLease(TranslationServiceKey key, TranslationService service)
    {
        _key = key;
        Service = service;
    }

    public void Dispose()
    {
        TranslationServiceKey? key = Interlocked.Exchange(ref _key, null);
        if (key != null)
            TranslationServiceRegistry.Release(key);
    }
}

public static class TranslationServiceRegistry
{
    private static readonly object Sync = new();
    private static readonly Dictionary<TranslationServiceKey, Entry> Services = [];

    public static int ActiveServiceCount
    {
        get { lock (Sync) return Services.Count; }
    }

    public static TranslationServiceLease Acquire(
        TranslationServiceKey key,
        Func<ITranslationEngine> engineFactory)
    {
        ArgumentNullException.ThrowIfNull(engineFactory);
        TranslationServiceKey canonicalKey = key.Canonicalize();
        lock (Sync)
        {
            if (!Services.TryGetValue(canonicalKey, out Entry? entry))
            {
                var options = new TranslationServiceOptions
                {
                    CacheCapacity = canonicalKey.CacheCapacity,
                    QueueCapacity = canonicalKey.QueueCapacity
                };
                entry = new Entry(new TranslationService(engineFactory(), options));
                Services.Add(canonicalKey, entry);
            }

            entry.ReferenceCount++;
            return new TranslationServiceLease(canonicalKey, entry.Service);
        }
    }

    internal static void Release(TranslationServiceKey key)
    {
        TranslationService? toDispose = null;
        lock (Sync)
        {
            if (!Services.TryGetValue(key, out Entry? entry))
                return;
            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0)
            {
                Services.Remove(key);
                toDispose = entry.Service;
            }
        }

        toDispose?.Dispose();
    }

    private sealed class Entry(TranslationService service)
    {
        public TranslationService Service { get; } = service;
        public int ReferenceCount { get; set; }
    }
}
