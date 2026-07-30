using VLCLR.ObjectDetection;

namespace VLCLR.ObjectDetection.Tests;

public sealed class MediaTimestampGateTests
{
    [Fact]
    public void TryAdvanceRejectsRepeatedPausedFrameTimestamp()
    {
        var gate = new MediaTimestampGate();

        Assert.True(gate.TryAdvance(1_000_000));
        Assert.False(gate.TryAdvance(1_000_000));
        Assert.False(gate.TryAdvance(1_000_000));
    }

    [Fact]
    public void TryAdvanceAcceptsPlaybackAndSeekMovement()
    {
        var gate = new MediaTimestampGate();

        Assert.True(gate.TryAdvance(1_000_000));
        Assert.True(gate.TryAdvance(1_033_333));
        Assert.True(gate.TryAdvance(250_000));
    }

    [Fact]
    public void ResetAcceptsSameTimestampOnNewTimeline()
    {
        var gate = new MediaTimestampGate();

        Assert.True(gate.TryAdvance(1_000_000));
        Assert.False(gate.TryAdvance(1_000_000));

        gate.Reset();

        Assert.True(gate.TryAdvance(1_000_000));
    }
}
