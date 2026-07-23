namespace LiveAudioTranslator;

internal enum LatestWorkOfferResult
{
    RejectedNotReady,
    Added,
    Replaced
}

/// <summary>A model-ready gate with exactly one replaceable pending item.</summary>
internal sealed class LatestWorkQueue<T>
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _available = new(0, 1);
    private T? _value;
    private bool _hasValue;
    private bool _ready;

    public bool IsReady
    {
        get { lock (_sync) return _ready; }
    }

    public int Count
    {
        get { lock (_sync) return _hasValue ? 1 : 0; }
    }

    public void MarkReady()
    {
        lock (_sync)
            _ready = true;
    }

    public LatestWorkOfferResult Offer(T value)
    {
        bool signal;
        LatestWorkOfferResult result;
        lock (_sync)
        {
            if (!_ready)
                return LatestWorkOfferResult.RejectedNotReady;
            signal = !_hasValue;
            result = _hasValue ? LatestWorkOfferResult.Replaced : LatestWorkOfferResult.Added;
            _value = value;
            _hasValue = true;
        }
        if (signal)
            _available.Release();
        return result;
    }

    public async ValueTask<T> TakeAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            await _available.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                if (!_hasValue)
                    continue;
                T value = _value!;
                _value = default;
                _hasValue = false;
                return value;
            }
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _value = default;
            _hasValue = false;
        }
        _available.Wait(0);
    }
}
