using System.Runtime.InteropServices;
using VLCLR.Native;
using Xunit;
using Gen = VLCLR.Native.Interop;

namespace VLCLR.Tests;

public sealed class VLCAudioInteropTests
{
    [Fact]
    public void AudioFormatLayoutMatchesVlc4Headers()
    {
        Assert.Equal(Gen.AudioFormat.MaxSizeOf, Marshal.SizeOf<VLCAudioFormat>());
        Assert.Equal(Gen.AudioFormat.Offsets.Format, Marshal.OffsetOf<VLCAudioFormat>(nameof(VLCAudioFormat.Format)).ToInt32());
        Assert.Equal(Gen.AudioFormat.Offsets.Rate, Marshal.OffsetOf<VLCAudioFormat>(nameof(VLCAudioFormat.Rate)).ToInt32());
        Assert.Equal(Gen.AudioFormat.Offsets.Channels, Marshal.OffsetOf<VLCAudioFormat>(nameof(VLCAudioFormat.Channels)).ToInt32());
    }

    [Fact]
    public void AudioBlockPrefixMatchesVlc4Headers()
    {
        Assert.Equal(Gen.Frame.Offsets.Ancillaries, Marshal.SizeOf<VLCBlock>());
        Assert.Equal(Gen.Frame.Offsets.Buffer, Marshal.OffsetOf<VLCBlock>(nameof(VLCBlock.Buffer)).ToInt32());
        Assert.Equal(Gen.Frame.Offsets.NbSamples, Marshal.OffsetOf<VLCBlock>(nameof(VLCBlock.SampleCount)).ToInt32());
        Assert.Equal(Gen.Frame.Offsets.Pts, Marshal.OffsetOf<VLCBlock>(nameof(VLCBlock.PresentationTimestamp)).ToInt32());
    }

    [Fact]
    public void EsFormatAudioViewOverlaysVideoUnion()
    {
        var format = new VLCEsFormat();
        format.Audio = new VLCAudioFormat
        {
            Format = VLCFourCC.F32L,
            Rate = 48_000,
            Channels = 2
        };

        Assert.Equal(VLCFourCC.F32L, format.Video.Chroma);
        Assert.Equal(48_000u, format.Video.Width);
        Assert.Equal((byte)2, format.Audio.Channels);
    }
}
