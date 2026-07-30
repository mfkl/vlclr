namespace VLCLR.ObjectDetection;

public sealed record DetectionOverlayOptions(
    TimeSpan MaximumResultAge,
    TimeSpan MaximumFutureSkew)
{
    public static DetectionOverlayOptions Default { get; } = new(
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(50));
}

public sealed class DetectionOverlaySelector
{
    private readonly DetectionOverlayOptions _options;

    public DetectionOverlaySelector(DetectionOverlayOptions? options = null)
    {
        _options = options ?? DetectionOverlayOptions.Default;
        if (_options.MaximumResultAge < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                _options.MaximumResultAge,
                "Maximum result age cannot be negative.");
        }
        if (_options.MaximumFutureSkew < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                _options.MaximumFutureSkew,
                "Maximum future skew cannot be negative.");
        }
    }

    public int Select(
        DetectionBatch? batch,
        TimeSpan currentMediaTime,
        DetectionQuery? query,
        Span<ObjectDetection> destination)
    {
        if (batch is null || destination.IsEmpty)
        {
            return 0;
        }

        TimeSpan age = currentMediaTime - batch.MediaTime;
        if (age > _options.MaximumResultAge ||
            age < -_options.MaximumFutureSkew)
        {
            return 0;
        }

        int count = 0;
        foreach (ObjectDetection detection in batch.Detections)
        {
            if (query is not null &&
                (detection.ClassId != query.ObjectClass.Id ||
                 detection.Confidence < query.MinimumConfidence))
            {
                continue;
            }

            destination[count++] = detection;
            if (count == destination.Length)
            {
                break;
            }
        }

        return count;
    }
}
