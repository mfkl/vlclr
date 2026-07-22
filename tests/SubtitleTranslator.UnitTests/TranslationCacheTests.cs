using SubtitleTranslator;

namespace SubtitleTranslator.UnitTests;

public sealed class TranslationCacheTests
{
    [Fact]
    public void Cache_NormalizesKeysAndEvictsLeastRecentlyUsedEntry()
    {
        var cache = new TranslationCache(2);
        cache.Set("Cafe\u0301", "cafe");
        cache.Set("second", "deuxieme");

        Assert.True(cache.TryGet("Caf\u00e9", out string first));
        Assert.Equal("cafe", first);

        cache.Set("third", "troisieme");

        Assert.False(cache.TryGet("second", out _));
        Assert.True(cache.TryGet("Caf\u00e9", out _));
        Assert.True(cache.TryGet("third", out _));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void GetOrTranslate_UsesDependencyFreeEngineContract()
    {
        var engine = new RecordingEngine(text => text.ToUpperInvariant());
        var cache = new TranslationCache(4);

        Assert.Equal("HELLO", cache.GetOrTranslate("Hello", engine));
        Assert.Equal("HELLO", cache.GetOrTranslate("Hello", engine));
        Assert.Equal(1, engine.CallCount);
    }
}
