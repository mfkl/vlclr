using System.Text;
using LiveAudioTranslator;
using Xunit;

namespace SubtitleTranslator.UnitTests;

public sealed class TimedCueTests
{
    [Fact]
    public void CueFileTailsOnlyCompleteValidatedLines()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "timeline.jsonl");
        TimedCueManifest manifest = CreateManifest(Guid.NewGuid());
        using (var writer = new TimedCueFileWriter(path, manifest))
        {
            writer.Append(new TimedCue(0, 1_000_000, 2_000_000, "  Bonjour\r\nmonde  "));
            var reader = new TimedCueFileReader(path);
            TimedCue first = Assert.Single(reader.Cues);
            Assert.Equal("Bonjour monde", first.Text);

            byte[] partial = Encoding.UTF8.GetBytes(
                "{\"kind\":\"cue\",\"sequence\":1,\"startMediaTicks\":2000000," +
                "\"endMediaTicks\":3000000,\"text\":\"secret partial\"}");
            using (var append = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                append.Write(partial);

            Assert.Equal(0, reader.Refresh());
            Assert.Single(reader.Cues);
            using (var append = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                append.WriteByte((byte)'\n');
            Assert.Equal(1, reader.Refresh());
            Assert.Equal(2, reader.Cues.Count);
        }
    }

    [Fact]
    public void MalformedCompleteCueIsIgnoredWithoutExposingText()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "timeline.jsonl");
        using var writer = new TimedCueFileWriter(path, CreateManifest(Guid.NewGuid()));
        var reader = new TimedCueFileReader(path);
        using (var append = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            byte[] malformed = Encoding.UTF8.GetBytes("{not-json}\n");
            append.Write(malformed);
        }

        Assert.Equal(0, reader.Refresh());
        Assert.Empty(reader.Cues);
        Assert.NotNull(reader.LastError);
    }

    [Fact]
    public void ProgressReplacementIsReadableAndGenerationBound()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "timeline.jsonl");
        Guid generation = Guid.NewGuid();
        using var writer = new TimedCueFileWriter(path, CreateManifest(generation));
        writer.WriteProgress(new TimedCueProgress
        {
            GenerationId = generation.ToString("D"),
            ProcessedAudioTicks = 20_000_000,
            PreparedThroughTicks = 19_000_000,
            AudioDurationTicks = 60_000_000,
            ProcessingWallMilliseconds = 10_000,
            CueCount = 3
        });

        TimedCueProgress progress = Assert.IsType<TimedCueProgress>(new TimedCueFileReader(path).ReadProgress());
        Assert.Equal(0.5, progress.RealTimeFactor, precision: 3);
        Assert.Equal(3, progress.CueCount);
    }

    [Fact]
    public void TextBoundNeverSplitsUtf16SurrogatePair()
    {
        string input = new string('a', TimedCue.MaximumTextLength - 1) + "😀";

        string normalized = TimedCueText.Normalize(input);

        Assert.Equal(TimedCue.MaximumTextLength - 1, normalized.Length);
        Assert.DoesNotContain(normalized, char.IsSurrogate);
    }

    private static TimedCueManifest CreateManifest(Guid generation) => new()
    {
        MediaIdentity = "file:///C:/media/example.mp4",
        SourceLanguage = "auto",
        TargetLanguage = "fr",
        WhisperModelIdentity = "whisper:fixture",
        AudioDurationTicks = 60_000_000,
        GenerationId = generation.ToString("D")
    };

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vlclr-cues-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
