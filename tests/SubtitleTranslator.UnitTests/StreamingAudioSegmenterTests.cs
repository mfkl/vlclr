using LiveAudioTranslator;
using Xunit;

namespace SubtitleTranslator.UnitTests;

public sealed class StreamingAudioSegmenterTests
{
    [Fact]
    public void FloatStereoIsDownmixedResampledAndSplitAfterSilence()
    {
        var utterances = new List<float[]>();
        var segmenter = new StreamingAudioSegmenter(0.05f, 200, 2_000, utterances.Add);
        float[] speech = InterleavedFloat(48_000, 2, 0.2f);
        float[] silence = InterleavedFloat(14_400, 2, 0f);

        segmenter.PushFloat32(speech, 48_000, 2);
        segmenter.PushFloat32(silence, 48_000, 2);

        float[] utterance = Assert.Single(utterances);
        Assert.InRange(utterance.Length, 19_000, 19_400);
        Assert.All(utterance.Take(15_000), value => Assert.InRange(value, 0.199f, 0.201f));
    }

    [Fact]
    public void Pcm16InputIsNormalizedForWhisper()
    {
        var utterances = new List<float[]>();
        var segmenter = new StreamingAudioSegmenter(0.05f, 200, 2_000, utterances.Add);
        short[] speech = Enumerable.Repeat((short)8_192, 16_000).ToArray();
        short[] silence = new short[4_000];

        segmenter.PushPcm16(speech, 16_000, 1);
        segmenter.PushPcm16(silence, 16_000, 1);

        float[] utterance = Assert.Single(utterances);
        Assert.InRange(utterance[0], 0.249f, 0.251f);
    }

    [Fact]
    public void OpposingStereoChannelsDoNotTriggerSpeech()
    {
        var utterances = new List<float[]>();
        var segmenter = new StreamingAudioSegmenter(0.05f, 200, 2_000, utterances.Add);
        var samples = new float[32_000];
        for (int index = 0; index < samples.Length; index += 2)
        {
            samples[index] = 0.4f;
            samples[index + 1] = -0.4f;
        }

        segmenter.PushFloat32(samples, 16_000, 2);

        Assert.Empty(utterances);
    }

    [Fact]
    public void ResetDiscardsPendingSpeech()
    {
        var utterances = new List<float[]>();
        var segmenter = new StreamingAudioSegmenter(0.05f, 200, 2_000, utterances.Add);
        segmenter.PushFloat32(Enumerable.Repeat(0.2f, 8_000).ToArray(), 16_000, 1);

        segmenter.Reset();
        segmenter.PushFloat32(new float[4_000], 16_000, 1);

        Assert.Empty(utterances);
    }

    [Fact]
    public void TimedSegmentsPreservePtsAcrossResamplingBoundaries()
    {
        var segments = new List<TimedAudioSegment>();
        var segmenter = new StreamingAudioSegmenter(0.05f, 200, 2_500, segments.Add);
        float[] first = InterleavedFloat(24_000, 2, 0.2f);
        float[] second = InterleavedFloat(14_400, 2, 0f);

        segmenter.PushFloat32(first, 48_000, 2, 5_000_000, 500_000);
        segmenter.PushFloat32(second, 48_000, 2, 5_500_000, 300_000);

        TimedAudioSegment segment = Assert.Single(segments);
        Assert.InRange(segment.StartMediaTicks, 4_999_999, 5_000_001);
        Assert.InRange(segment.EndMediaTicks, 5_499_999, 5_500_064);
        Assert.False(segment.ForcedSplit);
    }

    [Fact]
    public void ForcedSplitCarriesAtMostQuarterSecondOverlap()
    {
        var segments = new List<TimedAudioSegment>();
        var segmenter = new StreamingAudioSegmenter(0.05f, 400, 1_000, segments.Add);
        float[] speech = InterleavedFloat(32_000, 1, 0.2f);

        segmenter.PushFloat32(speech, 16_000, 1, 2_000_000, 2_000_000);

        Assert.True(segments.Count >= 2);
        Assert.All(segments.Take(2), segment => Assert.True(segment.ForcedSplit));
        long overlap = segments[0].EndMediaTicks - segments[1].StartMediaTicks;
        Assert.InRange(overlap, 0, 250_001);
    }

    [Fact]
    public void FractionalResamplingAcrossBlocksKeepsMonotonicMediaTime()
    {
        var segments = new List<TimedAudioSegment>();
        var segmenter = new StreamingAudioSegmenter(0.05f, 200, 2_500, segments.Add);
        for (int block = 0; block < 10; block++)
        {
            long start = 8_000_000 + block * 100_000L;
            segmenter.PushFloat32(
                InterleavedFloat(4_410, 1, 0.2f),
                44_100,
                1,
                start,
                100_000);
        }
        segmenter.PushFloat32(new float[13_230], 44_100, 1, 9_000_000, 300_000);

        TimedAudioSegment segment = Assert.Single(segments);
        Assert.InRange(segment.StartMediaTicks, 7_999_999, 8_000_001);
        Assert.InRange(segment.EndMediaTicks, 8_999_900, 9_000_100);
    }

    private static float[] InterleavedFloat(int frames, int channels, float value) =>
        Enumerable.Repeat(value, frames * channels).ToArray();
}
