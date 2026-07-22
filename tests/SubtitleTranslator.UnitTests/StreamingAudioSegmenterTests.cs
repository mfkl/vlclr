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

    private static float[] InterleavedFloat(int frames, int channels, float value) =>
        Enumerable.Repeat(value, frames * channels).ToArray();
}
