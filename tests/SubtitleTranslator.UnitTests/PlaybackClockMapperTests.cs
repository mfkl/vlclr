using LiveAudioTranslator;
using Xunit;

namespace SubtitleTranslator.UnitTests;

public sealed class PlaybackClockMapperTests
{
    [Fact]
    public void MapsCallbackSystemDateFromLatestAudioAnchor()
    {
        var mapper = new PlaybackClockMapper();
        Assert.False(mapper.Observe(5_000_000, 20_000, 100_000_000));

        Assert.True(mapper.TryMap(100_125_000, out long media, out int generation));
        Assert.Equal(5_125_000, media);
        Assert.Equal(0, generation);
    }

    [Fact]
    public void RejectsStaleAnchor()
    {
        var mapper = new PlaybackClockMapper(staleAnchorTicks: 500_000);
        mapper.Observe(5_000_000, 20_000, 100_000_000);

        Assert.False(mapper.TryMap(100_600_001, out _, out _, out PlaybackClockFailure failure));
        Assert.Equal(PlaybackClockFailure.StaleAnchor, failure);
    }

    [Fact]
    public void SeekBackAndForwardAdvanceGeneration()
    {
        var mapper = new PlaybackClockMapper();
        mapper.Observe(10_000_000, 20_000, 100_000_000);
        Assert.True(mapper.Observe(2_000_000, 20_000, 100_020_000));
        int afterBack = mapper.Generation;
        Assert.True(mapper.Observe(30_000_000, 20_000, 100_040_000));

        Assert.Equal(1, afterBack);
        Assert.Equal(2, mapper.Generation);
    }

    [Fact]
    public void OrdinaryDriftDoesNotResetGeneration()
    {
        var mapper = new PlaybackClockMapper(jumpThresholdTicks: 500_000);
        mapper.Observe(1_000_000, 20_000, 10_000_000);

        Assert.False(mapper.Observe(1_023_000, 20_000, 10_020_000));
        Assert.Equal(0, mapper.Generation);
    }
}
