namespace SubtitleTranslator;

/// <summary>Thread-safe, bounded LRU cache for normalized subtitle cues.</summary>
public sealed class TranslationCache
{
    private readonly object _sync = new();
    private readonly LinkedList<(string Key, string Value)> _entries = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, string Value)>> _lookup =
        new(StringComparer.Ordinal);
    private readonly int _capacity;

    public int Count
    {
        get { lock (_sync) return _lookup.Count; }
    }

    public TranslationCache(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public bool TryGet(string input, out string value)
    {
        string key = TranslationTextNormalizer.NormalizeCacheKey(input);
        lock (_sync)
        {
            if (!_lookup.TryGetValue(key, out var node))
            {
                value = "";
                return false;
            }

            _entries.Remove(node);
            _entries.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
    }

    public void Set(string input, string value)
    {
        string key = TranslationTextNormalizer.NormalizeCacheKey(input);
        if (key.Length == 0)
            return;

        lock (_sync)
        {
            if (_lookup.TryGetValue(key, out var existing))
            {
                existing.Value = (key, value);
                _entries.Remove(existing);
                _entries.AddFirst(existing);
                return;
            }

            if (_lookup.Count >= _capacity)
            {
                var last = _entries.Last!;
                _lookup.Remove(last.Value.Key);
                _entries.RemoveLast();
            }

            var node = _entries.AddFirst((key, value));
            _lookup[key] = node;
        }
    }

    public string GetOrTranslate(string input, ITranslationEngine translator)
    {
        if (string.IsNullOrEmpty(input))
            return input;
        if (TryGet(input, out string cached))
            return cached;

        string normalized = TranslationTextNormalizer.NormalizeCacheKey(input);
        string translated = translator.TranslateDetailed(normalized).Text;
        Set(normalized, translated);
        return translated;
    }
}
