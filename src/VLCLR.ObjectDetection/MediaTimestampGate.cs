namespace VLCLR.ObjectDetection;

public sealed class MediaTimestampGate
{
    private long _lastTimestamp;
    private bool _hasTimestamp;

    public bool TryAdvance(long timestamp)
    {
        if (_hasTimestamp && timestamp == _lastTimestamp)
        {
            return false;
        }

        _lastTimestamp = timestamp;
        _hasTimestamp = true;
        return true;
    }

    public void Reset()
    {
        _lastTimestamp = default;
        _hasTimestamp = false;
    }
}
