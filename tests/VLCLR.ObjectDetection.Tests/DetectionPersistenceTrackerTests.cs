using VLCLR.ObjectDetection;

namespace VLCLR.ObjectDetection.Tests;

public sealed class DetectionPersistenceTrackerTests
{
    [Fact]
    public void HoldsDetectionAcrossEmptyInferenceResults()
    {
        var tracker = CreateTracker();
        var destination = new ObjectDetection[4];

        tracker.Observe(
            1,
            TimeSpan.Zero,
            [CreateDetection(0, 100, 100)]);
        tracker.Observe(
            2,
            TimeSpan.FromMilliseconds(70),
            ReadOnlySpan<ObjectDetection>.Empty);

        Assert.Equal(
            1,
            tracker.Snapshot(
                TimeSpan.FromMilliseconds(499),
                destination));
        Assert.Equal(
            0,
            tracker.Snapshot(
                TimeSpan.FromMilliseconds(501),
                destination));
    }

    [Fact]
    public void MatchingDetectionUpdatesOneTrack()
    {
        var tracker = CreateTracker();
        var destination = new ObjectDetection[4];

        tracker.Observe(
            1,
            TimeSpan.Zero,
            [CreateDetection(0, 100, 100)]);
        tracker.Observe(
            2,
            TimeSpan.FromMilliseconds(70),
            [CreateDetection(0, 112, 106)]);

        Assert.Equal(
            1,
            tracker.Snapshot(
                TimeSpan.FromMilliseconds(70),
                destination));
        Assert.Equal(112, destination[0].Box.X);
        Assert.Equal(106, destination[0].Box.Y);
    }

    [Fact]
    public void MissingObjectExpiresIndependently()
    {
        var tracker = CreateTracker();
        var destination = new ObjectDetection[4];

        tracker.Observe(
            1,
            TimeSpan.Zero,
            [
                CreateDetection(0, 100, 100),
                CreateDetection(0, 600, 100)
            ]);
        tracker.Observe(
            2,
            TimeSpan.FromMilliseconds(200),
            [CreateDetection(0, 110, 100)]);

        Assert.Equal(
            2,
            tracker.Snapshot(
                TimeSpan.FromMilliseconds(499),
                destination));
        Assert.Equal(
            1,
            tracker.Snapshot(
                TimeSpan.FromMilliseconds(501),
                destination));
        Assert.Equal(110, destination[0].Box.X);
    }

    [Fact]
    public void RepeatedGenerationDoesNotExtendHoldTime()
    {
        var tracker = CreateTracker();
        var destination = new ObjectDetection[4];
        ObjectDetection detection = CreateDetection(0, 100, 100);

        Assert.True(
            tracker.Observe(1, TimeSpan.Zero, [detection]));
        Assert.False(
            tracker.Observe(
                1,
                TimeSpan.FromMilliseconds(400),
                [detection]));

        Assert.Equal(
            0,
            tracker.Snapshot(
                TimeSpan.FromMilliseconds(501),
                destination));
    }

    [Fact]
    public void ResetDropsEveryTrack()
    {
        var tracker = CreateTracker();
        var destination = new ObjectDetection[4];
        tracker.Observe(
            1,
            TimeSpan.Zero,
            [CreateDetection(0, 100, 100)]);

        tracker.Reset();

        Assert.Equal(
            0,
            tracker.Snapshot(TimeSpan.Zero, destination));
        Assert.True(
            tracker.Observe(
                1,
                TimeSpan.Zero,
                [CreateDetection(0, 100, 100)]));
    }

    private static DetectionPersistenceTracker CreateTracker() =>
        new(
            4,
            new DetectionPersistenceOptions(
                TimeSpan.FromMilliseconds(500),
                0.20f));

    private static ObjectDetection CreateDetection(
        int classId,
        float x,
        float y) =>
        new(
            classId,
            "person",
            0.8f,
            new DetectionBox(x, y, 200, 300));
}
