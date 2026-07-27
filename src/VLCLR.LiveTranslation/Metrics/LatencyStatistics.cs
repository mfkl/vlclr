namespace VLCLR.LiveTranslation.Metrics;

public sealed class LatencyStatistics
{
    private readonly object _sync = new();
    private readonly long[] _values;
    private int _count;
    private int _next;

    public LatencyStatistics(int capacity = 2_048)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _values = new long[capacity];
    }

    public void Add(long value)
    {
        if (value < 0)
            return;
        lock (_sync)
        {
            _values[_next] = value;
            _next = (_next + 1) % _values.Length;
            _count = Math.Min(_count + 1, _values.Length);
        }
    }

    public (long P50, long P95, long P99) Snapshot()
    {
        lock (_sync)
        {
            if (_count == 0)
                return (0, 0, 0);
            long[] copy = _values[.._count].ToArray();
            Array.Sort(copy);
            return (
                Percentile(copy, 0.50),
                Percentile(copy, 0.95),
                Percentile(copy, 0.99));
        }
    }

    private static long Percentile(long[] sorted, double percentile)
    {
        int index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
