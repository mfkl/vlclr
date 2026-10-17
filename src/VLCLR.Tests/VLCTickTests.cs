using VLCLR.Native;
using Xunit;

namespace VLCLR.Tests;

public class VLCTickTests
{
    [Fact]
    public void Constants_MatchVlc()
    {
        Assert.Equal(0L, VLCTick.Invalid);   // VLC_TICK_INVALID
        Assert.Equal(1L, VLCTick.Zero);      // VLC_TICK_0
        Assert.Equal(1_000_000L, VLCTick.Second);
        Assert.Equal(1_000L, VLCTick.Millisecond);
    }
}
