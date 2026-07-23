using LiveAudioTranslator;
using Xunit;

namespace SubtitleTranslator.UnitTests;

public sealed class LeadPolicyTests
{
    [Fact]
    public void FastWorkerLaunchesAtFifteenSecondLead()
    {
        PreparationLaunchDecision decision = LeadPolicy.Decide(0.5, 15_000_000, 120_000_000, false);
        Assert.True(decision.Launch);
        Assert.False(decision.RequireComplete);
        Assert.Equal(15_000_000, decision.RequiredLeadTicks);
    }

    [Fact]
    public void NearRealtimeWorkerRequiresConservativeLargerLead()
    {
        PreparationLaunchDecision decision = LeadPolicy.Decide(0.9, 15_000_000, 120_000_000, false);
        Assert.False(decision.Launch);
        Assert.InRange(decision.RequiredLeadTicks, 25_000_000, 27_000_000);
    }

    [Fact]
    public void SlowWorkerRequiresCompleteTimeline()
    {
        PreparationLaunchDecision decision = LeadPolicy.Decide(1.1, 90_000_000, 120_000_000, false);
        Assert.True(decision.RequireComplete);
        Assert.False(decision.Launch);
    }

    [Fact]
    public void CompletedTimelineAlwaysLaunches()
    {
        Assert.True(LeadPolicy.Decide(2.0, 120_000_000, 120_000_000, true).Launch);
    }
}
