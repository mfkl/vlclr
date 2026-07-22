using SubtitleTranslator;

namespace SubtitleTranslator.UnitTests;

public sealed class TranslationTextNormalizerTests
{
    [Fact]
    public void NormalizeCacheKey_PreservesPunctuationAndLineBreaks()
    {
        string result = TranslationTextNormalizer.NormalizeCacheKey("  Wait... what?\r\nI'm ready!  ");

        Assert.Equal("Wait... what?\nI'm ready!", result);
    }

    [Fact]
    public void NormalizeCacheKey_CanonicalizesEquivalentUnicode()
    {
        Assert.Equal(
            TranslationTextNormalizer.NormalizeCacheKey("Caf\u00e9"),
            TranslationTextNormalizer.NormalizeCacheKey("Cafe\u0301"));
    }

    [Fact]
    public void NormalizeCacheKey_ReplacesMalformedUtf16()
    {
        string malformed = new(new[] { '\ud800', 'X' });

        Assert.Equal("\uFFFDX", TranslationTextNormalizer.NormalizeCacheKey(malformed));
    }

    [Fact]
    public void ComputeCueHash_IsStableAndDoesNotExposeText()
    {
        string hash = TranslationTextNormalizer.ComputeCueHash("private subtitle text");

        Assert.Equal(16, hash.Length);
        Assert.DoesNotContain("private", hash, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(hash, TranslationTextNormalizer.ComputeCueHash("private subtitle text"));
    }
}
