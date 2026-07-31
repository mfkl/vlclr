namespace VLCLR.ObjectDetection;

public sealed record DetectionPersistenceOptions(
    TimeSpan HoldDuration,
    float MinimumIntersectionOverUnion)
{
    public static DetectionPersistenceOptions Default { get; } = new(
        TimeSpan.FromMilliseconds(500),
        0.20f);
}

/// <summary>
/// Associates detections across sampled inference results and keeps unmatched
/// tracks alive for a bounded amount of media time.
/// </summary>
public sealed class DetectionPersistenceTracker
{
    private struct Track
    {
        public bool Active;
        public bool Matched;
        public ObjectDetection Detection;
        public TimeSpan LastSeenMediaTime;
    }

    private readonly DetectionPersistenceOptions _options;
    private readonly Track[] _tracks;
    private long? _lastObservationGeneration;
    private long _revision;

    public DetectionPersistenceTracker(
        int capacity,
        DetectionPersistenceOptions? options = null)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _options = options ?? DetectionPersistenceOptions.Default;
        if (_options.HoldDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                _options.HoldDuration,
                "Hold duration cannot be negative.");
        }
        if (_options.MinimumIntersectionOverUnion is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                _options.MinimumIntersectionOverUnion,
                "Minimum intersection over union must be between zero and one.");
        }

        _tracks = new Track[capacity];
    }

    /// <summary>
    /// Changes whenever the visible tracked regions change.
    /// </summary>
    public long Revision => _revision;

    /// <summary>
    /// Adds a new sampled inference result. Repeated generations are ignored so
    /// rendering the same result on intermediate video frames cannot extend its
    /// lifetime.
    /// </summary>
    public bool Observe(
        long generation,
        TimeSpan mediaTime,
        ReadOnlySpan<ObjectDetection> detections)
    {
        if (_lastObservationGeneration == generation)
        {
            return false;
        }

        _lastObservationGeneration = generation;
        for (int index = 0; index < _tracks.Length; index++)
        {
            _tracks[index].Matched = false;
        }

        bool visualChange = false;
        foreach (ObjectDetection detection in detections)
        {
            int trackIndex = FindBestTrack(detection);
            if (trackIndex < 0)
            {
                trackIndex = FindReplacementTrack();
                visualChange = true;
            }

            ref Track track = ref _tracks[trackIndex];
            if (!track.Active || track.Detection != detection)
            {
                visualChange = true;
            }

            track.Active = true;
            track.Matched = true;
            track.Detection = detection;
            track.LastSeenMediaTime = mediaTime;
        }

        if (visualChange)
        {
            _revision++;
        }
        return true;
    }

    /// <summary>
    /// Copies tracks that are still inside the configured media-time hold
    /// window. Tracks expire while playback advances, not while paused.
    /// </summary>
    public int Snapshot(
        TimeSpan currentMediaTime,
        Span<ObjectDetection> destination)
    {
        bool visualChange = false;
        for (int index = 0; index < _tracks.Length; index++)
        {
            ref Track track = ref _tracks[index];
            if (track.Active &&
                currentMediaTime - track.LastSeenMediaTime >
                _options.HoldDuration)
            {
                track.Active = false;
                visualChange = true;
            }
        }

        if (visualChange)
        {
            _revision++;
        }

        int count = 0;
        foreach (Track track in _tracks)
        {
            if (!track.Active || count == destination.Length)
            {
                continue;
            }

            destination[count++] = track.Detection;
        }
        return count;
    }

    public void Reset()
    {
        bool hadActiveTrack = _tracks.Any(track => track.Active);
        Array.Clear(_tracks);
        _lastObservationGeneration = null;
        if (hadActiveTrack)
        {
            _revision++;
        }
    }

    private int FindBestTrack(ObjectDetection detection)
    {
        int bestIndex = -1;
        float bestIntersectionOverUnion =
            _options.MinimumIntersectionOverUnion;
        for (int index = 0; index < _tracks.Length; index++)
        {
            Track track = _tracks[index];
            if (!track.Active ||
                track.Matched ||
                track.Detection.ClassId != detection.ClassId)
            {
                continue;
            }

            float intersectionOverUnion = CalculateIntersectionOverUnion(
                track.Detection.Box,
                detection.Box);
            if (intersectionOverUnion >= bestIntersectionOverUnion)
            {
                bestIndex = index;
                bestIntersectionOverUnion = intersectionOverUnion;
            }
        }
        return bestIndex;
    }

    private int FindReplacementTrack()
    {
        int oldestIndex = 0;
        TimeSpan oldestMediaTime = TimeSpan.MaxValue;
        for (int index = 0; index < _tracks.Length; index++)
        {
            Track track = _tracks[index];
            if (!track.Active)
            {
                return index;
            }
            if (!track.Matched &&
                track.LastSeenMediaTime < oldestMediaTime)
            {
                oldestIndex = index;
                oldestMediaTime = track.LastSeenMediaTime;
            }
        }
        return oldestIndex;
    }

    private static float CalculateIntersectionOverUnion(
        DetectionBox left,
        DetectionBox right)
    {
        float intersectionLeft = MathF.Max(left.X, right.X);
        float intersectionTop = MathF.Max(left.Y, right.Y);
        float intersectionRight = MathF.Min(left.Right, right.Right);
        float intersectionBottom = MathF.Min(left.Bottom, right.Bottom);
        float intersectionWidth = MathF.Max(
            0,
            intersectionRight - intersectionLeft);
        float intersectionHeight = MathF.Max(
            0,
            intersectionBottom - intersectionTop);
        float intersectionArea = intersectionWidth * intersectionHeight;
        if (intersectionArea <= 0)
        {
            return 0;
        }

        float leftArea =
            MathF.Max(0, left.Width) * MathF.Max(0, left.Height);
        float rightArea =
            MathF.Max(0, right.Width) * MathF.Max(0, right.Height);
        float unionArea = leftArea + rightArea - intersectionArea;
        return unionArea > 0
            ? intersectionArea / unionArea
            : 0;
    }
}
