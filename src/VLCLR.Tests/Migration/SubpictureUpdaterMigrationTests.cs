using System.Runtime.CompilerServices;
using VLCLR.Native;
using Xunit;
using Gen = VLCLR.Native.Interop;

namespace VLCLR.Tests.Migration;

public sealed class SubpictureUpdaterMigrationTests
{
    [Fact]
    public void SubpictureMatchesGenerated()
    {
        Assert.Equal(Gen.Subpicture.MaxSizeOf, Unsafe.SizeOf<VLCSubpicture>());
        VLCSubpicture s = default;
        Assert.Equal(Gen.Subpicture.Offsets.Channel, Off(ref s, ref s.Channel));
        Assert.Equal(Gen.Subpicture.Offsets.Order, Off(ref s, ref s.Order));
        Assert.Equal(Gen.Subpicture.Offsets.Next, Off(ref s, ref s.Next));
        Assert.Equal(Gen.Subpicture.Offsets.Regions + Gen.List.Offsets.Prev, Off(ref s, ref s.RegionsPrev));
        Assert.Equal(Gen.Subpicture.Offsets.Regions + Gen.List.Offsets.Next, Off(ref s, ref s.RegionsNext));
        Assert.Equal(Gen.Subpicture.Offsets.Start, Off(ref s, ref s.Start));
        Assert.Equal(Gen.Subpicture.Offsets.Stop, Off(ref s, ref s.Stop));
        Assert.Equal(Gen.Subpicture.Offsets.Ephemer, Off(ref s, ref s.IsEphemer));
        Assert.Equal(Gen.Subpicture.Offsets.Fade, Off(ref s, ref s.IsFade));
        Assert.Equal(Gen.Subpicture.Offsets.Subtitle, Off(ref s, ref s.IsSubtitle));
        Assert.Equal(Gen.Subpicture.Offsets.OriginalPictureWidth, Off(ref s, ref s.OriginalPictureWidth));
        Assert.Equal(Gen.Subpicture.Offsets.OriginalPictureHeight, Off(ref s, ref s.OriginalPictureHeight));
        Assert.Equal(Gen.Subpicture.Offsets.Alpha, Off(ref s, ref s.Alpha));
        Assert.Equal(Gen.Subpicture.Offsets.Updater + Gen.SubpictureUpdater.Offsets.Sys, Off(ref s, ref s.UpdaterSys));
        Assert.Equal(Gen.Subpicture.Offsets.Updater + Gen.SubpictureUpdater.Offsets.Ops, Off(ref s, ref s.UpdaterOps));
        Assert.Equal(Gen.Subpicture.Offsets.Private, Off(ref s, ref s.Private));
    }

    [Fact]
    public void UpdaterAndOpsMatchGenerated()
    {
        Assert.Equal(Gen.SubpictureUpdater.MaxSizeOf, Unsafe.SizeOf<VLCSubpictureUpdater>());
        VLCSubpictureUpdater u = default;
        Assert.Equal(Gen.SubpictureUpdater.Offsets.Sys, Off(ref u, ref u.Sys));
        Assert.Equal(Gen.SubpictureUpdater.Offsets.Ops, Off(ref u, ref u.Ops));
        Assert.Equal(Gen.SpuUpdaterOps.MaxSizeOf, Unsafe.SizeOf<VLCSubpictureUpdaterOps>());
        VLCSubpictureUpdaterOps o = default;
        Assert.Equal(Gen.SpuUpdaterOps.Offsets.Update, Off(ref o, ref o.Update));
        Assert.Equal(Gen.SpuUpdaterOps.Offsets.Destroy, Off(ref o, ref o.Destroy));
    }

    private static int Off<TOwner, TField>(ref TOwner owner, ref TField field) =>
        (int)Unsafe.ByteOffset(ref Unsafe.As<TOwner, byte>(ref owner), ref Unsafe.As<TField, byte>(ref field));
}
