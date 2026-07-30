namespace VLCLR.ObjectDetection;

public readonly record struct DetectionBox(
    float X,
    float Y,
    float Width,
    float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
}

public readonly record struct ObjectDetection(
    int ClassId,
    string Label,
    float Confidence,
    DetectionBox Box);

public sealed record DetectionBatch(
    Guid SessionId,
    long Generation,
    TimeSpan MediaTime,
    int SourceWidth,
    int SourceHeight,
    TimeSpan InferenceDuration,
    IReadOnlyList<ObjectDetection> Detections);

public sealed record DetectionQuery(
    ObjectClassDescriptor ObjectClass,
    float MinimumConfidence,
    string OriginalText);
