using System.Runtime.CompilerServices;
using VLCLR.Native;
using Xunit;
using Gen = VLCLR.Native.Interop;

namespace VLCLR.Tests.Migration;

public sealed class FilterStructMigrationTests
{
    [Fact]
    public void SizeMatchesGenerated()
    {
        Assert.Equal(Gen.Filter.MaxSizeOf, Unsafe.SizeOf<VLCFilter>());
    }

    [Fact]
    public unsafe void OffsetsMatchGenerated()
    {
        VLCFilter f = default;
        Assert.Equal(Gen.Filter.Offsets.Obj, Off(ref f, ref f.Obj));
        Assert.Equal(Gen.Filter.Offsets.Module, Off(ref f, ref f.Module));
        Assert.Equal(Gen.Filter.Offsets.Sys, Off(ref f, ref f.Sys));
        Assert.Equal(Gen.Filter.Offsets.FmtIn, Off(ref f, ref *f.FormatIn));
        Assert.Equal(Gen.Filter.Offsets.VctxIn, Off(ref f, ref f.VideoContextIn));
        Assert.Equal(Gen.Filter.Offsets.FmtOut, Off(ref f, ref *f.FormatOut));
        Assert.Equal(Gen.Filter.Offsets.VctxOut, Off(ref f, ref f.VideoContextOut));
        Assert.Equal(Gen.Filter.Offsets.AllowFmtOutChange, Off(ref f, ref f.AllowFormatOutChange));
        Assert.Equal(Gen.Filter.Offsets.Name, Off(ref f, ref f.Name));
        Assert.Equal(Gen.Filter.Offsets.Cfg, Off(ref f, ref f.Config));
        Assert.Equal(Gen.Filter.Offsets.Ops, Off(ref f, ref f.Operations));
        Assert.Equal(Gen.Filter.Offsets.Owner, Off(ref f, ref f.Owner));
    }

    private static int Off<TOwner, TField>(ref TOwner owner, ref TField field) =>
        (int)Unsafe.ByteOffset(ref Unsafe.As<TOwner, byte>(ref owner), ref Unsafe.As<TField, byte>(ref field));
}
