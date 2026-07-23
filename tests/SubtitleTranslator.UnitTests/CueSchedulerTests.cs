using LiveAudioTranslator;
using Xunit;

namespace SubtitleTranslator.UnitTests;

public sealed class CueSchedulerTests
{
    private static readonly TimedCue[] Cues =
    [
        new(0, 1_000_000, 2_000_000, "un"),
        new(1, 3_000_000, 4_000_000, "deux")
    ];

    [Fact]
    public void EmitsOnlyContainingOrSlightlyEarlyCueOnce()
    {
        var scheduler = new CueScheduler(earlyToleranceTicks: 80_000);

        Assert.False(scheduler.TrySchedule(Cues, 900_000, 0, "g", out _));
        Assert.True(scheduler.TrySchedule(Cues, 950_000, 0, "g", out ScheduledCue cue));
        Assert.Equal(0, cue.Cue.Sequence);
        Assert.Equal(1_050_000, cue.RemainingTicks);
        Assert.False(scheduler.TrySchedule(Cues, 1_100_000, 0, "g", out _));
        Assert.False(scheduler.TrySchedule(Cues, 2_500_000, 0, "g", out _));
    }

    [Fact]
    public void NeverEmitsCueThatAlreadyEnded()
    {
        var scheduler = new CueScheduler();

        Assert.False(scheduler.TrySchedule(Cues, 2_000_000, 0, "g", out _));
        Assert.True(scheduler.TrySchedule(Cues, 3_500_000, 0, "g", out ScheduledCue cue));
        Assert.Equal(1, cue.Cue.Sequence);
    }

    [Fact]
    public void NewPlaybackGenerationPermitsCueAfterSeekBack()
    {
        var scheduler = new CueScheduler();
        Assert.True(scheduler.TrySchedule(Cues, 1_100_000, 0, "g", out _));
        Assert.False(scheduler.TrySchedule(Cues, 1_100_000, 0, "g", out _));

        Assert.True(scheduler.TrySchedule(Cues, 1_100_000, 1, "g", out _));
    }

    [Fact]
    public void TimelineGenerationChangeClearsSuppression()
    {
        var scheduler = new CueScheduler();
        Assert.True(scheduler.TrySchedule(Cues, 1_100_000, 0, "old", out _));
        Assert.True(scheduler.TrySchedule(Cues, 1_100_000, 0, "new", out _));
    }
}
