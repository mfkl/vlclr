using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VLCLR.Native;
using Gen = VLCLR.Native.Interop;
using Xunit;

namespace VLCLR.Tests.Migration;

public class NativeTypesOnInteropTests
{
    private static int Off(Type t, string f) => Marshal.OffsetOf(t, f).ToInt32();

    [Fact]
    public void Plane_MatchesGenerated()
    {
        Assert.Equal(Gen.Plane.SizeOf, Unsafe.SizeOf<VLCPlane>());
        Assert.Equal(Gen.Plane.Offsets.Pixels, Off(typeof(VLCPlane), nameof(VLCPlane.Pixels)));
        Assert.Equal(Gen.Plane.Offsets.Lines, Off(typeof(VLCPlane), nameof(VLCPlane.Lines)));
        Assert.Equal(Gen.Plane.Offsets.Pitch, Off(typeof(VLCPlane), nameof(VLCPlane.Pitch)));
        Assert.Equal(Gen.Plane.Offsets.PixelPitch, Off(typeof(VLCPlane), nameof(VLCPlane.PixelPitch)));
        Assert.Equal(Gen.Plane.Offsets.VisibleLines, Off(typeof(VLCPlane), nameof(VLCPlane.VisibleLines)));
        Assert.Equal(Gen.Plane.Offsets.VisiblePitch, Off(typeof(VLCPlane), nameof(VLCPlane.VisiblePitch)));
    }

    [Fact]
    public void ObjectHeader_MatchesGenerated()
    {
        Assert.Equal(Gen.ObjectHeader.SizeOf, Unsafe.SizeOf<VLCObjectHeader>());
        Assert.Equal(Gen.ObjectHeader.Offsets.Logger, Off(typeof(VLCObjectHeader), nameof(VLCObjectHeader.Logger)));
        Assert.Equal(Gen.ObjectHeader.Offsets.Priv, Off(typeof(VLCObjectHeader), nameof(VLCObjectHeader.PrivOrObj)));
        Assert.Equal(Gen.ObjectHeader.Offsets.NoInteract, Off(typeof(VLCObjectHeader), nameof(VLCObjectHeader.NoInteract)));
        Assert.Equal(Gen.ObjectHeader.Offsets.Force, Off(typeof(VLCObjectHeader), nameof(VLCObjectHeader.Force)));
    }

    [Fact]
    public void Block_IsPrefixOfFrame()
    {
        Assert.Equal(Gen.Frame.Offsets.Ancillaries, Unsafe.SizeOf<VLCBlock>());
        Assert.Equal(Gen.Frame.Offsets.Next, Off(typeof(VLCBlock), nameof(VLCBlock.Next)));
        Assert.Equal(Gen.Frame.Offsets.Buffer, Off(typeof(VLCBlock), nameof(VLCBlock.Buffer)));
        Assert.Equal(Gen.Frame.Offsets.BufferSize, Off(typeof(VLCBlock), nameof(VLCBlock.BufferLength)));
        Assert.Equal(Gen.Frame.Offsets.Start, Off(typeof(VLCBlock), nameof(VLCBlock.AllocationStart)));
        Assert.Equal(Gen.Frame.Offsets.Size, Off(typeof(VLCBlock), nameof(VLCBlock.AllocationSize)));
        Assert.Equal(Gen.Frame.Offsets.Flags, Off(typeof(VLCBlock), nameof(VLCBlock.Flags)));
        Assert.Equal(Gen.Frame.Offsets.NbSamples, Off(typeof(VLCBlock), nameof(VLCBlock.SampleCount)));
        Assert.Equal(Gen.Frame.Offsets.Pts, Off(typeof(VLCBlock), nameof(VLCBlock.PresentationTimestamp)));
        Assert.Equal(Gen.Frame.Offsets.Dts, Off(typeof(VLCBlock), nameof(VLCBlock.DecodeTimestamp)));
        Assert.Equal(Gen.Frame.Offsets.Length, Off(typeof(VLCBlock), nameof(VLCBlock.Length)));
    }

    [Fact]
    public void BlockFlags_KeepValues()
    {
        Assert.Equal(0x0001u, VLCBlockFlags.Discontinuity);
        Assert.Equal(0x0400u, VLCBlockFlags.Corrupted);
    }
}
