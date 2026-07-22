namespace SubtitleTranslator;

/// <summary>
/// Simple LRU cache for translated subtitle text.
/// Avoids re-translating identical subtitle lines.
/// </summary>
public sealed class TranslationCache
{
    private readonly LinkedList<(string Key, string Value)> _entries = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, string Value)>> _lookup = new();
    private readonly int _capacity;

    public int Count => _lookup.Count;

    public TranslationCache(int capacity)
    {
        _capacity = capacity;
    }

    /// <summary>
    /// Get cached translation or translate and cache the result.
    /// </summary>
    public string GetOrTranslate(string input, OnnxTranslator translator)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // Cache hit: move to front (most recently used)
        if (_lookup.TryGetValue(input, out var node))
        {
            _entries.Remove(node);
            _entries.AddFirst(node);
            return node.Value.Value;
        }

        // Cache miss: translate
        var translated = translator.Translate(input);

        // Evict if at capacity (remove least recently used = last)
        if (_lookup.Count >= _capacity)
        {
            var last = _entries.Last!;
            _lookup.Remove(last.Value.Key);
            _entries.RemoveLast();
        }

        // Add to front
        var newNode = _entries.AddFirst((input, translated));
        _lookup[input] = newNode;

        return translated;
    }
}
