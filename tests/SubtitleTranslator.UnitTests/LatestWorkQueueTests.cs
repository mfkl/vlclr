using LiveAudioTranslator;
using Xunit;

namespace SubtitleTranslator.UnitTests;

public sealed class LatestWorkQueueTests
{
    [Fact]
    public void RejectsWarmupAudioUntilModelsAreReady()
    {
        var queue = new LatestWorkQueue<int>();

        Assert.Equal(LatestWorkOfferResult.RejectedNotReady, queue.Offer(1));
        Assert.Equal(0, queue.Count);
        queue.MarkReady();
        Assert.Equal(LatestWorkOfferResult.Added, queue.Offer(2));
    }

    [Fact]
    public async Task CapacityIsOneAndNewestWorkReplacesOldWork()
    {
        var queue = new LatestWorkQueue<int>();
        queue.MarkReady();

        Assert.Equal(LatestWorkOfferResult.Added, queue.Offer(1));
        Assert.Equal(LatestWorkOfferResult.Replaced, queue.Offer(2));
        Assert.Equal(LatestWorkOfferResult.Replaced, queue.Offer(3));
        Assert.Equal(1, queue.Count);
        Assert.Equal(3, await queue.TakeAsync());
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void ClearDropsPendingWorkWithoutClosingReadyGate()
    {
        var queue = new LatestWorkQueue<string>();
        queue.MarkReady();
        queue.Offer("pre-seek");

        queue.Clear();

        Assert.Equal(0, queue.Count);
        Assert.True(queue.IsReady);
        Assert.Equal(LatestWorkOfferResult.Added, queue.Offer("post-seek"));
    }
}
