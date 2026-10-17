using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VLCLR.Native;
using Xunit;
using G = VLCLR.Native.Interop;

namespace VLCLR.Tests.Migration;

public sealed class FormatMigrationTests
{
    [Fact]
    public void VideoFormatSizeIsSharedPrefix()
    {
        Assert.Equal(G.VideoFormat.MinSizeOf, Unsafe.SizeOf<VLCVideoFormat>());
        Assert.Equal(G.VideoFormat.MinSizeOf, Marshal.SizeOf<VLCVideoFormat>());
    }

    [Fact]
    public void VideoFormatOffsetsMatchGenerated()
    {
        VLCVideoFormat v = default;
        Assert.Equal(G.VideoFormat.Offsets.Palette, Off(ref v, ref v.Palette));
        Assert.Equal(G.VideoFormat.Offsets.ProjectionMode, Off(ref v, ref v.ProjectionMode));
        Assert.Equal(G.VideoFormat.Offsets.Pose + G.Viewpoint.Offsets.Fov, Off(ref v, ref v.PoseFov));
        Assert.Equal(G.VideoFormat.Offsets.Mastering + G.VideoFormatMastering.Offsets.WhitePoint + 2, Off(ref v, ref v.MasteringWhitePointY));
        Assert.Equal(G.VideoFormat.Offsets.Mastering + G.VideoFormatMastering.Offsets.MinLuminance, Off(ref v, ref v.MasteringMinLuminance));
        Assert.Equal(G.VideoFormat.Offsets.Lighting + 2, Off(ref v, ref v.LightingMaxFALL));
        Assert.Equal(G.VideoFormat.Offsets.Dovi + 1, Off(ref v, ref v.DoviVersionMinor));
    }

    [Fact]
    public void AudioFormatMatchesGenerated()
    {
        Assert.Equal(G.AudioFormat.MaxSizeOf, Unsafe.SizeOf<VLCAudioFormat>());
        VLCAudioFormat a = default;
        Assert.Equal(G.AudioFormat.Offsets.Channels, Off(ref a, ref a.Channels));
        Assert.Equal(G.AudioFormat.Offsets.Blockalign, Off(ref a, ref a.BlockAlign));
    }

    [Fact]
    public void ChromaDescriptionMatchesGenerated()
    {
        Assert.Equal(G.ChromaDescription.MaxSizeOf, Unsafe.SizeOf<VLCChromaDescription>());
        Assert.Equal(G.ChromaDescriptionP.MaxSizeOf, Unsafe.SizeOf<VLCChromaPlaneSize>());
        Assert.Equal(G.Rational.MaxSizeOf, Unsafe.SizeOf<VLCRational>());
        VLCChromaDescription c = default;
        Assert.Equal(G.ChromaDescription.Offsets.P + 3 * G.ChromaDescriptionP.MaxSizeOf, Off(ref c, ref c.Plane3));
        Assert.Equal(G.ChromaDescription.Offsets.ColorBits, Off(ref c, ref c.ColorBits));
        VLCChromaPlaneSize p = default;
        Assert.Equal(G.ChromaDescriptionP.Offsets.H, Off(ref p, ref p.Height));
    }

    private static int Off<TOwner, TField>(ref TOwner owner, ref TField field) =>
        (int)Unsafe.ByteOffset(ref Unsafe.As<TOwner, byte>(ref owner), ref Unsafe.As<TField, byte>(ref field));
}
