using VLCLR.Plugin;
using Xunit;

namespace VLCLR.Tests;

public class FrameAllocationMeterTests
{
    private static object? s_sink;

    private static void Frame(FrameAllocationMeter meter, int allocate = 0)
    {
        meter.BeginFrame();
        if (allocate > 0)
        {
            s_sink = new byte[allocate];
        }
        meter.EndFrame();
    }

    [Fact]
    public void FramesThatAllocateNothing_AreAllocationFree()
    {
        var meter = new FrameAllocationMeter(warmUpFrames: 2);

        for (int i = 0; i < 10; i++)
        {
            Frame(meter);
        }

        Assert.Equal(10, meter.Frames);
        Assert.Equal(8, meter.MeasuredFrames);
        Assert.True(meter.IsAllocationFree);
        Assert.Equal(0, meter.AllocatingFrames);
        Assert.StartsWith("Frame allocations: none in 8 frames after a warm-up of 2", meter.Summary());
    }

    [Fact]
    public void WarmUpAllocations_AreNotCounted()
    {
        var meter = new FrameAllocationMeter(warmUpFrames: 2);

        Frame(meter, allocate: 1000);
        Frame(meter, allocate: 1000);
        Frame(meter);

        Assert.Equal(1, meter.MeasuredFrames);
        Assert.True(meter.IsAllocationFree);
    }

    [Fact]
    public void AllocatingFrames_AreCounted()
    {
        var meter = new FrameAllocationMeter(warmUpFrames: 1);

        Frame(meter);
        Frame(meter);
        Frame(meter, allocate: 100);
        Frame(meter);
        Frame(meter, allocate: 1000);

        Assert.False(meter.IsAllocationFree);
        Assert.Equal(4, meter.MeasuredFrames);
        Assert.Equal(2, meter.AllocatingFrames);
        Assert.Equal(3, meter.FirstAllocatingFrame);
        Assert.InRange(meter.MaxFrameBytes, 1000, 1100);
        Assert.InRange(meter.Bytes, 1100, 1300);
        Assert.StartsWith($"Frame allocations: {meter.Bytes} bytes in 2 of 4 frames", meter.Summary());
    }

    [Fact]
    public void Restart_StartsANewWarmUp()
    {
        var meter = new FrameAllocationMeter(warmUpFrames: 1);
        Frame(meter);
        Frame(meter);

        meter.Restart();
        Frame(meter, allocate: 1000);
        Frame(meter);

        Assert.Equal(2, meter.MeasuredFrames);
        Assert.True(meter.IsAllocationFree);
    }

    [Fact]
    public void FewerFramesThanTheWarmUp_AreNotMeasured()
    {
        var meter = new FrameAllocationMeter(warmUpFrames: 3);
        Frame(meter);
        meter.Stop();

        Assert.Equal(0, meter.MeasuredFrames);
        Assert.Equal(-1, meter.RuntimeBytes);
        Assert.Equal("Frame allocations: not measured (1 frames, fewer than the 3 of the warm-up)", meter.Summary());
    }

    [Fact]
    public void RuntimeBytes_SeeAllocationsOnOtherThreads()
    {
        var meter = new FrameAllocationMeter(warmUpFrames: 1);
        Frame(meter);

        meter.BeginFrame();
        var worker = new Thread(() => s_sink = new byte[100_000]);
        worker.Start();
        worker.Join();
        meter.EndFrame();
        meter.Stop();

        Assert.True(meter.Bytes < 100_000, "the frame's thread did not allocate the buffer");
        Assert.True(meter.RuntimeBytes >= 100_000, $"whole runtime: {meter.RuntimeBytes} bytes");
    }

    [Fact]
    public void RuntimeBytes_LeaveOutTheWarmUpAfterARestart()
    {
        var meter = new FrameAllocationMeter(warmUpFrames: 1);
        Frame(meter);
        Frame(meter);

        meter.Restart();
        Frame(meter, allocate: 16 << 20);
        Frame(meter);
        meter.Stop();

        Assert.InRange(meter.RuntimeBytes, 0, (16 << 20) - 1);
    }

    [Fact]
    public void WithoutAWarmUp_EveryFrameAndTheRuntimeAreCounted()
    {
        var meter = new FrameAllocationMeter(warmUpFrames: 0);
        Frame(meter, allocate: 1000);
        meter.Stop();

        Assert.Equal(1, meter.MeasuredFrames);
        Assert.Equal(1, meter.FirstAllocatingFrame);
        Assert.True(meter.RuntimeBytes >= 1000, $"whole runtime: {meter.RuntimeBytes} bytes");
    }
}
