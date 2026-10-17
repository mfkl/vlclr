using System.Runtime.CompilerServices;
using VLCLR.Native;
using Xunit;
using Gen = VLCLR.Native.Interop;

namespace VLCLR.Tests.Migration;

public sealed class FilterMigrationTests
{
    [Fact]
    public void SizesMatchGenerated()
    {
        Assert.Equal(Gen.FilterOwner.MaxSizeOf, Unsafe.SizeOf<VLCFilterOwner>());
        Assert.Equal(Gen.FilterOperations.MaxSizeOf, Unsafe.SizeOf<VLCFilterOperations>());
        Assert.Equal(Gen.FilterOperations.MaxSizeOf, Unsafe.SizeOf<VLCTextRendererOperations>());
        Assert.Equal(Gen.FilterVideoCallbacks.MaxSizeOf, Unsafe.SizeOf<VLCFilterVideoCallbacks>());
    }

    [Fact]
    public void OwnerOffsetsMatchGenerated()
    {
        VLCFilterOwner o = default;
        Assert.Equal(Gen.FilterOwner.Offsets.Video, Off(ref o, ref o.Callbacks));
        Assert.Equal(Gen.FilterOwner.Offsets.GetAttachments, Off(ref o, ref o.GetAttachments));
        Assert.Equal(Gen.FilterOwner.Offsets.Sys, Off(ref o, ref o.Sys));
    }

    [Fact]
    public void OperationsOffsetsMatchGenerated()
    {
        VLCFilterOperations o = default;
        Assert.Equal(Gen.FilterOperations.Offsets.FilterVideo, Off(ref o, ref o.FilterVideo));
        Assert.Equal(Gen.FilterOperations.Offsets.DrainAudio, Off(ref o, ref o.Drain));
        Assert.Equal(Gen.FilterOperations.Offsets.Flush, Off(ref o, ref o.Flush));
        Assert.Equal(Gen.FilterOperations.Offsets.ChangeViewpoint, Off(ref o, ref o.ChangeViewpoint));
        Assert.Equal(Gen.FilterOperations.Offsets.VideoMouse, Off(ref o, ref o.VideoMouse));
        Assert.Equal(Gen.FilterOperations.Offsets.Close, Off(ref o, ref o.Close));

        VLCTextRendererOperations t = default;
        Assert.Equal(Gen.FilterOperations.Offsets.Render, Off(ref t, ref t.Render));
        Assert.Equal(Gen.FilterOperations.Offsets.DrainAudio, Off(ref t, ref t.Drain));
        Assert.Equal(Gen.FilterOperations.Offsets.Flush, Off(ref t, ref t.Flush));
        Assert.Equal(Gen.FilterOperations.Offsets.ChangeViewpoint, Off(ref t, ref t.ChangeViewpoint));
        Assert.Equal(Gen.FilterOperations.Offsets.VideoMouse, Off(ref t, ref t.VideoMouse));
        Assert.Equal(Gen.FilterOperations.Offsets.Close, Off(ref t, ref t.Close));
    }

    [Fact]
    public void VideoCallbacksOffsetsMatchGenerated()
    {
        VLCFilterVideoCallbacks c = default;
        Assert.Equal(Gen.FilterVideoCallbacks.Offsets.BufferNew, Off(ref c, ref c.BufferNew));
        Assert.Equal(Gen.FilterVideoCallbacks.Offsets.HoldDevice, Off(ref c, ref c.HoldDevice));
    }

    private static int Off<TOwner, TField>(ref TOwner owner, ref TField field) =>
        (int)Unsafe.ByteOffset(ref Unsafe.As<TOwner, byte>(ref owner), ref Unsafe.As<TField, byte>(ref field));
}
