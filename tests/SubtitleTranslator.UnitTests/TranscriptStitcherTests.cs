using LiveAudioTranslator;
using Xunit;

namespace SubtitleTranslator.UnitTests;

public sealed class TranscriptStitcherTests
{
    [Fact]
    public void RemovesNormalizedForcedSplitWordOverlap()
    {
        string result = TranscriptStitcher.RemoveForcedSplitOverlap(
            "we need to keep this sentence moving",
            "This sentence moving into the next thought.");

        Assert.Equal("into the next thought.", result);
    }

    [Fact]
    public void DoesNotRemoveUnrelatedWords()
    {
        Assert.Equal("another idea", TranscriptStitcher.RemoveForcedSplitOverlap("first idea", "another idea"));
    }
}
