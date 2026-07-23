using System.Text;
using LiveAudioTranslator;
using Xunit;

namespace SubtitleTranslator.UnitTests;

public sealed class WaveReaderTests
{
    [Fact]
    public void StreamsValidatedMono16KhzPcmWithoutWholeFileBuffer()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vlclr-wave-{Guid.NewGuid():N}.wav");
        try
        {
            WriteWave(path, sampleRate: 16_000, channels: 1, samples: 32_000);
            using var reader = new WaveReader(path);
            short[] block = new short[777];
            int total = 0;
            int count;
            while ((count = reader.ReadSamples(block)) > 0)
                total += count;

            Assert.Equal(16_000, reader.SampleRate);
            Assert.Equal(1, reader.Channels);
            Assert.Equal(2_000_000, reader.DurationTicks);
            Assert.Equal(32_000, total);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(44_100, 1)]
    [InlineData(16_000, 2)]
    public void RejectsUnexpectedExtractionFormat(int sampleRate, short channels)
    {
        string path = Path.Combine(Path.GetTempPath(), $"vlclr-wave-{Guid.NewGuid():N}.wav");
        try
        {
            WriteWave(path, sampleRate, channels, sampleRate * channels);
            Assert.Throws<InvalidDataException>(() => new WaveReader(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void WriteWave(string path, int sampleRate, short channels, int samples)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        int dataBytes = samples * sizeof(short);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * sizeof(short));
        writer.Write((short)(channels * sizeof(short)));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);
        for (int index = 0; index < samples; index++)
            writer.Write((short)(index % 100));
    }
}
