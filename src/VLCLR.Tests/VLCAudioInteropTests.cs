using System.Runtime.InteropServices;
using VLCLR.Native;
using Xunit;

namespace VLCLR.Tests;

public sealed class VLCAudioInteropTests
{
    [Fact]
    public void AudioFormatLayoutMatchesVlc4Headers()
    {
        Assert.Equal(36, Marshal.SizeOf<VLCAudioFormat>());
        Assert.Equal(0, Marshal.OffsetOf<VLCAudioFormat>(nameof(VLCAudioFormat.Format)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<VLCAudioFormat>(nameof(VLCAudioFormat.Rate)).ToInt32());
        Assert.Equal(32, Marshal.OffsetOf<VLCAudioFormat>(nameof(VLCAudioFormat.Channels)).ToInt32());
    }

    [Fact]
    public void AudioBlockPrefixMatchesVlc4Headers()
    {
        Assert.Equal(72, Marshal.SizeOf<VLCBlock>());
        Assert.Equal(8, Marshal.OffsetOf<VLCBlock>(nameof(VLCBlock.Buffer)).ToInt32());
        Assert.Equal(44, Marshal.OffsetOf<VLCBlock>(nameof(VLCBlock.SampleCount)).ToInt32());
        Assert.Equal(48, Marshal.OffsetOf<VLCBlock>(nameof(VLCBlock.PresentationTimestamp)).ToInt32());
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
