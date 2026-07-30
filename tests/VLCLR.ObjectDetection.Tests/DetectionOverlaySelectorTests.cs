using VLCLR.ObjectDetection;

namespace VLCLR.ObjectDetection.Tests;

public sealed class DetectionOverlaySelectorTests
{
    private static readonly Guid SessionId = Guid.NewGuid();

    [Fact]
    public void SelectReturnsFreshDetectionsWithoutAllocatingAResultList()
    {
        DetectionBatch batch = CreateBatch(
            TimeSpan.FromSeconds(10),
            CreateDetection(0, "person", 0.8f),
            CreateDetection(32, "sports ball", 0.7f));
        var selector = new DetectionOverlaySelector();
        var destination = new ObjectDetection[4];

        int count = selector.Select(
            batch,
            TimeSpan.FromMilliseconds(10_200),
            null,
            destination);

        Assert.Equal(2, count);
        Assert.Equal("person", destination[0].Label);
        Assert.Equal("sports ball", destination[1].Label);
    }

    [Fact]
    public void SelectRejectsStaleAndPreSeekResults()
    {
        DetectionBatch batch = CreateBatch(
            TimeSpan.FromSeconds(10),
            CreateDetection(0, "person", 0.8f));
        var selector = new DetectionOverlaySelector();
        var destination = new ObjectDetection[2];

        Assert.Equal(
            0,
            selector.Select(
                batch,
                TimeSpan.FromMilliseconds(10_251),
                null,
                destination));
        Assert.Equal(
            0,
            selector.Select(
                batch,
                TimeSpan.FromMilliseconds(9_949),
                null,
                destination));
    }

    [Fact]
    public void SelectAppliesQueryAndDestinationCapacity()
    {
        ObjectClassDescriptor ball =
            Coco80ObjectCatalog.Create().Resolve("sports ball");
        var query = new DetectionQuery(
            ball,
            0.6f,
            "ball confidence 0.6");
        DetectionBatch batch = CreateBatch(
            TimeSpan.FromSeconds(10),
            CreateDetection(0, "person", 0.9f),
            CreateDetection(32, "sports ball", 0.5f),
            CreateDetection(32, "sports ball", 0.7f),
            CreateDetection(32, "sports ball", 0.8f));
        var selector = new DetectionOverlaySelector();
        var destination = new ObjectDetection[1];

        int count = selector.Select(
            batch,
            TimeSpan.FromSeconds(10),
            query,
            destination);

        Assert.Equal(1, count);
        Assert.Equal(0.7f, destination[0].Confidence);
    }

    private static DetectionBatch CreateBatch(
        TimeSpan mediaTime,
        params ObjectDetection[] detections)
    {
        return new DetectionBatch(
            SessionId,
            1,
            mediaTime,
            1920,
            1080,
            TimeSpan.FromMilliseconds(8),
            detections);
    }

    private static ObjectDetection CreateDetection(
        int classId,
        string label,
        float confidence)
    {
        return new ObjectDetection(
            classId,
            label,
            confidence,
            new DetectionBox(100, 100, 200, 200));
    }
}
