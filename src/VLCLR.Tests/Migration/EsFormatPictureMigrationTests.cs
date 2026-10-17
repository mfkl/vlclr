using System.Runtime.CompilerServices;
using VLCLR.Native;
using Xunit;
using Gen = VLCLR.Native.Interop;

namespace VLCLR.Tests.Migration;

public sealed class EsFormatPictureMigrationTests
{
    [Fact]
    public void EsFormatMatchesGenerated()
    {
        Assert.Equal(Gen.EsFormat.MaxSizeOf, Unsafe.SizeOf<VLCEsFormat>());
        VLCEsFormat e = default;
        Assert.Equal(Gen.EsFormat.Offsets.Cat, Off(ref e, ref e.Category));
        Assert.Equal(Gen.EsFormat.Offsets.Priority, Off(ref e, ref e.Priority));
        Assert.Equal(Gen.EsFormat.Offsets.ExtraLanguages, Off(ref e, ref e.ExtraLanguages));
        Assert.Equal(Gen.EsFormat.Offsets.Video, Off(ref e, ref e.Video));
        Assert.Equal(Gen.EsFormat.Offsets.Bitrate, Off(ref e, ref e.Bitrate));
        Assert.Equal(Gen.EsFormat.Offsets.Profile, Off(ref e, ref e.Profile));
        Assert.Equal(Gen.EsFormat.Offsets.Level, Off(ref e, ref e.Level));
        Assert.Equal(Gen.EsFormat.Offsets.Packetized, Off(ref e, ref e.Packetized));
        Assert.Equal(Gen.EsFormat.Offsets.ExtraSize, Off(ref e, ref e.ExtraSize));
        Assert.Equal(Gen.EsFormat.Offsets.Extra, Off(ref e, ref e.Extra));
    }

    [Fact]
    public void PictureMatchesGenerated()
    {
        Assert.Equal(Gen.Picture.MaxSizeOf, Unsafe.SizeOf<VLCPicture>());
        VLCPicture p = default;
        Assert.Equal(Gen.Picture.Offsets.P, Off(ref p, ref p.Plane0));
        Assert.Equal(Gen.Picture.Offsets.P + 1 * Gen.Plane.MaxSizeOf, Off(ref p, ref p.Plane1));
        Assert.Equal(Gen.Picture.Offsets.P + 4 * Gen.Plane.MaxSizeOf, Off(ref p, ref p.Plane4));
        Assert.Equal(Gen.Picture.Offsets.Planes, Off(ref p, ref p.PlaneCount));
        Assert.Equal(Gen.Picture.Offsets.Date, Off(ref p, ref p.Date));
        Assert.Equal(Gen.Picture.Offsets.Force, Off(ref p, ref p.Force));
        Assert.Equal(Gen.Picture.Offsets.MultiviewLeftEye, Off(ref p, ref p.MultiviewLeftEye));
        Assert.Equal(Gen.Picture.Offsets.NbFields, Off(ref p, ref p.FieldCount));
        Assert.Equal(Gen.Picture.Offsets.Context, Off(ref p, ref p.Context));
        Assert.Equal(Gen.Picture.Offsets.Next, Off(ref p, ref p.Next));
        Assert.Equal(Gen.Picture.Offsets.Refs, Off(ref p, ref p.Refs));
    }

    private static int Off<TOwner, TField>(ref TOwner owner, ref TField field) =>
        (int)Unsafe.ByteOffset(ref Unsafe.As<TOwner, byte>(ref owner), ref Unsafe.As<TField, byte>(ref field));
}
