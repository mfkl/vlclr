using VLCLR.Filters.Audio;
using VLCLR.Native;
using Xunit;

namespace VLCLR.Tests;

public class VLCAudioChannelsTests
{
    [Fact]
    public void Surround51_IsInterleavedLeftRightRearsCenterLfe()
    {
        // VLC's order (pi_vlc_chan_order_wg4): L R RL RR C LFE for 5.1.
        ushort mask = VLCAudioChannels.Surround51;
        Assert.Equal(6, VLCAudioChannels.Count(mask));
        Assert.Equal(0, VLCAudioChannels.IndexOf(mask, VLCAudioChannels.Left));
        Assert.Equal(1, VLCAudioChannels.IndexOf(mask, VLCAudioChannels.Right));
        Assert.Equal(2, VLCAudioChannels.IndexOf(mask, VLCAudioChannels.RearLeft));
        Assert.Equal(3, VLCAudioChannels.IndexOf(mask, VLCAudioChannels.RearRight));
        Assert.Equal(4, VLCAudioChannels.IndexOf(mask, VLCAudioChannels.Center));
        Assert.Equal(5, VLCAudioChannels.IndexOf(mask, VLCAudioChannels.Lfe));
    }

    [Fact]
    public void IndexOf_IsMinusOneForAMissingChannel()
    {
        Assert.Equal(2, VLCAudioChannels.Count(VLCAudioChannels.Stereo));
        Assert.Equal(-1, VLCAudioChannels.IndexOf(VLCAudioChannels.Stereo, VLCAudioChannels.Center));
    }

    [Fact]
    public void ProposedChannels_KeepsAChannelMapAndIgnoresAmbisonics()
    {
        var stereo = new VLCAudioFormat
        {
            PhysicalChannels = VLCAudioChannels.Stereo,
            ChannelType = (int)VLCLR.Native.Interop.AudioChannelType.Bitmap,
        };
        var ambisonics = stereo with { ChannelType = (int)VLCLR.Native.Interop.AudioChannelType.Ambisonics };

        Assert.Equal(VLCAudioChannels.Stereo, VLCLR.Plugin.VLCAudioFilterBase.ProposedChannels(stereo));
        Assert.Equal(0, VLCLR.Plugin.VLCAudioFilterBase.ProposedChannels(ambisonics));
    }

    [Fact]
    public void Context_ReturnsTheProposedOutputChannels()
    {
        Assert.Equal(VLCAudioChannels.Stereo,
            new VLCLR.Plugin.VLCAudioFilterContext(0, VLCAudioChannels.Stereo).ProposedOutputChannels);
        Assert.Equal(0, new VLCLR.Plugin.VLCAudioFilterContext(0).ProposedOutputChannels);
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(1.7f)]
    [InlineData(1f)]
    public void GainAmplify_MultipliesEverySampleLikeTheScalarLoop(float multiplier)
    {
        // Odd length: the vector loop and the scalar tail both run.
        var samples = new float[37];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = MathF.Sin(i) * 0.9f;
        var expected = samples.Select(s => multiplier == 1f ? s : s * multiplier).ToArray();

        Gain.Amplify(samples, multiplier);

        Assert.Equal(expected, samples);
    }
}
