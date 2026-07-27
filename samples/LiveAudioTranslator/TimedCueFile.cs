using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;

namespace LiveAudioTranslator;

internal sealed record TimedCueManifest
{
    public const int SupportedVersion = 1;

    public string Kind { get; init; } = "manifest";
    public int Version { get; init; } = SupportedVersion;
    public required string MediaIdentity { get; init; }
    public required string SourceLanguage { get; init; }
    public required string TargetLanguage { get; init; }
    public required string WhisperModelIdentity { get; init; }
    public required long AudioDurationTicks { get; init; }
    public long TimelineOffsetTicks { get; init; }
    public required string GenerationId { get; init; }

    public void Validate()
    {
        if (!string.Equals(Kind, "manifest", StringComparison.Ordinal) || Version != SupportedVersion)
            throw new InvalidDataException($"Unsupported timed-cue manifest version {Version}.");
        if (string.IsNullOrWhiteSpace(MediaIdentity) || string.IsNullOrWhiteSpace(SourceLanguage) ||
            string.IsNullOrWhiteSpace(TargetLanguage) || string.IsNullOrWhiteSpace(WhisperModelIdentity))
        {
            throw new InvalidDataException("Timed-cue manifest metadata is incomplete.");
        }
        if (AudioDurationTicks <= 0)
            throw new InvalidDataException("Timed-cue audio duration must be positive.");
        if (!Guid.TryParse(GenerationId, out _))
            throw new InvalidDataException("Timed-cue generation ID is invalid.");
    }
}

internal sealed record TimedCueProgress
{
    public int Version { get; init; } = TimedCueManifest.SupportedVersion;
    public required string GenerationId { get; init; }
    public long ProcessedAudioTicks { get; init; }
    public long PreparedThroughTicks { get; init; }
    public long AudioDurationTicks { get; init; }
    public long ProcessingWallMilliseconds { get; init; }
    public long CueCount { get; init; }
    public bool Complete { get; init; }
    public string? Error { get; init; }

    [JsonIgnore]
    public double RealTimeFactor => ProcessedAudioTicks <= 0
        ? double.PositiveInfinity
        : ProcessingWallMilliseconds * 1_000d / ProcessedAudioTicks;
}

internal sealed record TimedCueRecord
{
    public string Kind { get; init; } = "cue";
    public long Sequence { get; init; }
    public long StartMediaTicks { get; init; }
    public long EndMediaTicks { get; init; }
    public string Text { get; init; } = "";

    public TimedCue ToCue() => new(Sequence, StartMediaTicks, EndMediaTicks, Text);
}

internal sealed class TimedCueFileWriter : IDisposable
{
    private static readonly byte[] NewLine = [(byte)'\n'];
    private const int ProgressReplacementAttempts = 20;
    private readonly FileStream _stream;
    private long _lastSequence = -1;
    private long _lastStart = -1;
    private bool _disposed;

    public TimedCueFileWriter(string path, TimedCueManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        manifest.Validate();
        Path = System.IO.Path.GetFullPath(path);
        ProgressPath = GetProgressPath(Path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        _stream = new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite,
            bufferSize: 4_096, FileOptions.SequentialScan);
        WriteJsonLine(JsonSerializer.SerializeToUtf8Bytes(manifest, TimedCueJsonContext.Default.TimedCueManifest));
        _stream.Flush(flushToDisk: true);
    }

    public string Path { get; }
    public string ProgressPath { get; }

    public void Append(TimedCue cue)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cue = cue.NormalizeAndValidate();
        if (cue.Sequence <= _lastSequence || cue.StartMediaTicks < _lastStart)
            throw new InvalidDataException("Cue sequence and start time must be monotonic.");

        var record = new TimedCueRecord
        {
            Sequence = cue.Sequence,
            StartMediaTicks = cue.StartMediaTicks,
            EndMediaTicks = cue.EndMediaTicks,
            Text = cue.Text
        };
        WriteJsonLine(JsonSerializer.SerializeToUtf8Bytes(record, TimedCueJsonContext.Default.TimedCueRecord));
        _stream.Flush(flushToDisk: false);
        _lastSequence = cue.Sequence;
        _lastStart = cue.StartMediaTicks;
    }

    public void WriteProgress(TimedCueProgress progress)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(progress, TimedCueJsonContext.Default.TimedCueProgress);
        string temporaryPath = ProgressPath + ".tmp";
        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                   bufferSize: 4_096, FileOptions.WriteThrough))
        {
            stream.Write(json);
            stream.Flush(flushToDisk: true);
        }
        ReplaceProgressFile(temporaryPath);
    }

    public static string GetProgressPath(string cuePath) => cuePath + ".progress.json";

    private void WriteJsonLine(ReadOnlySpan<byte> json)
    {
        _stream.Write(json);
        _stream.Write(NewLine);
    }

    private void ReplaceProgressFile(string temporaryPath)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temporaryPath, ProgressPath, overwrite: true);
                return;
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException &&
                attempt + 1 < ProgressReplacementAttempts)
            {
                // File.Move(..., overwrite: true) needs delete sharing on the
                // destination on Windows. A progress reader that opened the
                // prior file without FileShare.Delete can therefore race this
                // atomic replacement for a few milliseconds.
                Thread.Sleep(Math.Min(10 * (attempt + 1), 100));
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _stream.Dispose();
    }
}

internal sealed class TimedCueFileReader
{
    private const int MaximumLineBytes = 128 * 1_024;
    private readonly string _path;
    private readonly List<TimedCue> _cues = [];
    private readonly List<byte> _pending = [];
    private long _offset;
    private long _lastSequence = -1;
    private long _lastStart = -1;

    public TimedCueFileReader(string path)
    {
        _path = System.IO.Path.GetFullPath(path);
        if (!File.Exists(_path))
            throw new FileNotFoundException("Timed-cue file not found.", _path);
        Refresh();
        if (Manifest == null)
            throw new InvalidDataException("Timed-cue file does not contain a complete manifest line.");
    }

    public TimedCueManifest? Manifest { get; private set; }
    public IReadOnlyList<TimedCue> Cues => _cues;
    public string? LastError { get; private set; }
    public int Revision { get; private set; }

    public int Refresh()
    {
        var info = new FileInfo(_path);
        if (info.Length < _offset)
            ResetForReplacement();
        if (info.Length == _offset)
            return 0;

        byte[] buffer = new byte[Math.Min(64 * 1_024, checked((int)Math.Min(int.MaxValue, info.Length - _offset)))];
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, bufferSize: 4_096, FileOptions.SequentialScan);
        stream.Position = _offset;
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            _offset += read;
            for (int index = 0; index < read; index++)
                _pending.Add(buffer[index]);
            if (_pending.Count > MaximumLineBytes && !_pending.Contains((byte)'\n'))
                throw new InvalidDataException("Timed-cue line exceeds the maximum size.");
        }

        int added = 0;
        int consumed = 0;
        while (true)
        {
            int relativeNewline = _pending.IndexOf((byte)'\n', consumed);
            if (relativeNewline < 0)
                break;
            int length = relativeNewline - consumed;
            if (length > 0 && _pending[relativeNewline - 1] == (byte)'\r')
                length--;
            if (length > MaximumLineBytes)
                throw new InvalidDataException("Timed-cue line exceeds the maximum size.");
            if (length > 0)
                added += ParseCompleteLine(CollectionsMarshal.AsSpan(_pending).Slice(consumed, length));
            consumed = relativeNewline + 1;
        }
        if (consumed > 0)
            _pending.RemoveRange(0, consumed);
        return added;
    }

    public TimedCueProgress? ReadProgress()
    {
        string path = TimedCueFileWriter.GetProgressPath(_path);
        if (!File.Exists(path))
            return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            TimedCueProgress? progress = JsonSerializer.Deserialize(
                stream, TimedCueJsonContext.Default.TimedCueProgress);
            return progress?.GenerationId == Manifest?.GenerationId ? progress : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private int ParseCompleteLine(ReadOnlySpan<byte> utf8)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8.ToArray());
            if (!document.RootElement.TryGetProperty("kind", out JsonElement kindElement))
                throw new InvalidDataException("Timed-cue record does not define a kind.");
            string? kind = kindElement.GetString();
            if (Manifest == null)
            {
                if (!string.Equals(kind, "manifest", StringComparison.Ordinal))
                    throw new InvalidDataException("The first timed-cue record must be a manifest.");
                TimedCueManifest manifest = JsonSerializer.Deserialize(
                    utf8, TimedCueJsonContext.Default.TimedCueManifest)
                    ?? throw new InvalidDataException("Timed-cue manifest is empty.");
                manifest.Validate();
                Manifest = manifest;
                Revision++;
                return 0;
            }

            if (!string.Equals(kind, "cue", StringComparison.Ordinal))
                throw new InvalidDataException($"Unknown timed-cue record kind '{kind}'.");
            TimedCueRecord record = JsonSerializer.Deserialize(
                utf8, TimedCueJsonContext.Default.TimedCueRecord)
                ?? throw new InvalidDataException("Timed-cue record is empty.");
            TimedCue cue = record.ToCue();
            cue.Validate();
            if (cue.Sequence <= _lastSequence || cue.StartMediaTicks < _lastStart)
                throw new InvalidDataException("Cue sequence or start time is not monotonic.");
            _cues.Add(cue);
            _lastSequence = cue.Sequence;
            _lastStart = cue.StartMediaTicks;
            return 1;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            LastError = ex.Message;
            if (Manifest == null)
                throw new InvalidDataException("Invalid timed-cue manifest.", ex);
            return 0;
        }
    }

    private void ResetForReplacement()
    {
        _offset = 0;
        _pending.Clear();
        _cues.Clear();
        _lastSequence = -1;
        _lastStart = -1;
        Manifest = null;
        LastError = null;
        Revision++;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TimedCueManifest))]
[JsonSerializable(typeof(TimedCueRecord))]
[JsonSerializable(typeof(TimedCueProgress))]
internal partial class TimedCueJsonContext : JsonSerializerContext;
