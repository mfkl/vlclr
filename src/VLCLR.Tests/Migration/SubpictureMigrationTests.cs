using System.Runtime.CompilerServices;
using VLCLR.Native;
using Xunit;
using Gen = VLCLR.Native.Interop;

namespace VLCLR.Tests.Migration;

public sealed class SubpictureMigrationTests
{
    [Fact]
    public void SubpictureRegionMatchesGenerated()
    {
        Assert.Equal(Gen.SubpictureRegion.MaxSizeOf, Unsafe.SizeOf<VLCSubpictureRegion>());
        VLCSubpictureRegion r = default;
        Assert.Equal(Gen.SubpictureRegion.Offsets.Fmt, Off(ref r, ref r.Format));
        Assert.Equal(Gen.SubpictureRegion.Offsets.Picture, Off(ref r, ref r.Picture));
        Assert.Equal(Gen.SubpictureRegion.Offsets.Absolute, Off(ref r, ref r.IsAbsolute));
        Assert.Equal(Gen.SubpictureRegion.Offsets.InWindow, Off(ref r, ref r.IsInWindow));
        Assert.Equal(Gen.SubpictureRegion.Offsets.X, Off(ref r, ref r.X));
        Assert.Equal(Gen.SubpictureRegion.Offsets.Y, Off(ref r, ref r.Y));
        Assert.Equal(Gen.SubpictureRegion.Offsets.Align, Off(ref r, ref r.Align));
        Assert.Equal(Gen.SubpictureRegion.Offsets.Alpha, Off(ref r, ref r.Alpha));
        Assert.Equal(Gen.SubpictureRegion.Offsets.Text, Off(ref r, ref r.Text));
        Assert.Equal(Gen.SubpictureRegion.Offsets.TextFlags, Off(ref r, ref r.TextFlags));
        Assert.Equal(Gen.SubpictureRegion.Offsets.MaxWidth, Off(ref r, ref r.MaxWidth));
        Assert.Equal(Gen.SubpictureRegion.Offsets.MaxHeight, Off(ref r, ref r.MaxHeight));
        Assert.Equal(Gen.SubpictureRegion.Offsets.Node + Gen.List.Offsets.Prev, Off(ref r, ref r.NodePrev));
        Assert.Equal(Gen.SubpictureRegion.Offsets.Node + Gen.List.Offsets.Next, Off(ref r, ref r.NodeNext));
    }

    [Fact]
    public void ListNodeAndPictureContextMatchGenerated()
    {
        Assert.Equal(Gen.List.MaxSizeOf, Unsafe.SizeOf<VLCListNode>());
        VLCListNode n = default;
        Assert.Equal(Gen.List.Offsets.Next, Off(ref n, ref n.Next));
        Assert.Equal(Gen.PictureContext.MaxSizeOf, Unsafe.SizeOf<VLCPictureContext>());
        VLCPictureContext c = default;
        Assert.Equal(Gen.PictureContext.Offsets.Copy, Off(ref c, ref c.Copy));
        Assert.Equal(Gen.PictureContext.Offsets.Vctx, Off(ref c, ref c.VideoContext));
        VLCD3D11PictureContext d = default;
        Assert.Equal(24, Off(ref d, ref d.System));
        Assert.Equal(128, Unsafe.SizeOf<VLCD3D11PictureContext>());
    }

    [Fact]
    public void ConstantsKeepValues()
    {
        Assert.Equal(1, VLCSubpictureAlign.Left);
        Assert.Equal(2, VLCSubpictureAlign.Right);
        Assert.Equal(4, VLCSubpictureAlign.Top);
        Assert.Equal(8, VLCSubpictureAlign.Bottom);
        Assert.Equal(15, VLCSubpictureAlign.Mask);
        Assert.Equal(16, VLCSubpictureTextFlags.NoRegionBackground);
        Assert.Equal(32, VLCSubpictureTextFlags.GridMode);
        Assert.Equal(64, VLCSubpictureTextFlags.TextNotBalanced);
        Assert.Equal(128, VLCSubpictureTextFlags.IsText);
    }

    private static int Off<TOwner, TField>(ref TOwner owner, ref TField field) =>
        (int)Unsafe.ByteOffset(ref Unsafe.As<TOwner, byte>(ref owner), ref Unsafe.As<TField, byte>(ref field));
}
