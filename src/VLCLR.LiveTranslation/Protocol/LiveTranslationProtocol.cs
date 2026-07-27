using System.Buffers.Binary;
using System.Text;

namespace VLCLR.LiveTranslation.Protocol;

public enum LiveMessageType : ushort
{
    Hello = 1,
    Configure = 2,
    Ready = 3,
    Audio = 4,
    Flush = 5,
    Cue = 6,
    Metrics = 7,
    Error = 8,
    Shutdown = 9
}

public enum LivePeerRole : byte
{
    Runner = 1,
    Plugin = 2,
    Worker = 3
}

public enum LiveAudioSampleFormat : byte
{
    Float32LittleEndian = 1,
    Pcm16LittleEndian = 2
}

public readonly record struct LiveProtocolHeader(
    LiveMessageType MessageType,
    Guid SessionId,
    int PlaybackGeneration,
    long Sequence,
    int PayloadLength);

public sealed record LiveProtocolMessage(LiveProtocolHeader Header, byte[] Payload);

public sealed record LiveConfigureMessage
{
    public required string SpeechModelId { get; init; }
    public required string TranslationModelId { get; init; }
    public required string SpeechProviderId { get; init; }
    public required string TranslationProviderId { get; init; }
    public required string SourceLanguage { get; init; }
    public required string TargetLanguage { get; init; }
    public int SpeechThreads { get; init; } = 2;
    public int TranslationThreads { get; init; } = 1;
    public int InputDelayMilliseconds { get; init; } = 15_000;
    public int VadSilenceMilliseconds { get; init; } = 500;
    public int MaximumUtteranceMilliseconds { get; init; } = 6_000;
    public float EnergyVadThreshold { get; init; } = 0.012f;
    public bool FakeInference { get; init; }
}

public sealed record LiveReadyMessage
{
    public required string SpeechModelId { get; init; }
    public required string TranslationModelId { get; init; }
    public required string SpeechProviderId { get; init; }
    public required string TranslationProviderId { get; init; }
    public required string ProviderFallbackReason { get; init; }
    public long InitializationMilliseconds { get; init; }
    public long WarmupMilliseconds { get; init; }
}

public sealed record LiveAudioMessage
{
    public required LiveAudioSampleFormat Format { get; init; }
    public required int SampleRate { get; init; }
    public required ushort Channels { get; init; }
    public required long SourcePts { get; init; }
    public required long DurationTicks { get; init; }
    public required byte[] AudioBytes { get; init; }
}

public sealed record LiveCueMessage
{
    public required long SourceStartPts { get; init; }
    public required long SourceEndPts { get; init; }
    public required long CompletedSystemTicks { get; init; }
    public required long SemanticLatencyTicks { get; init; }
    public required string Text { get; init; }
}

public sealed record LiveMetricsMessage
{
    public double RollingRealTimeFactor { get; init; }
    public double TotalRealTimeFactor { get; init; }
    public long CueLatencyP50Ticks { get; init; }
    public long CueLatencyP95Ticks { get; init; }
    public long CueLatencyP99Ticks { get; init; }
    public long QueueDepth { get; init; }
    public long DroppedAudio { get; init; }
    public long DroppedUtterances { get; init; }
    public long StaleCompletions { get; init; }
}

public sealed record LiveErrorMessage
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public bool Fatal { get; init; }
}

public static class LiveProtocol
{
    public const uint Magic = 0x544C4C56; // "VLLT" in little-endian byte order.
    public const ushort Version = 1;
    public const int HeaderSize = 40;
    public const int MaximumPayloadBytes = 1_048_576;
    public const int MaximumTextBytes = 8_192;
    public const int MaximumIdentifierBytes = 128;

    public static LiveProtocolMessage Create(
        LiveMessageType type,
        Guid sessionId,
        int playbackGeneration,
        long sequence,
        byte[]? payload = null)
    {
        if (sessionId == Guid.Empty)
            throw new ArgumentException("A non-empty session ID is required.", nameof(sessionId));
        payload ??= [];
        ValidatePayloadLength(payload.Length);
        return new LiveProtocolMessage(
            new LiveProtocolHeader(type, sessionId, playbackGeneration, sequence, payload.Length),
            payload);
    }

    public static byte[] EncodeHeader(LiveProtocolHeader header)
    {
        ValidatePayloadLength(header.PayloadLength);
        var bytes = new byte[HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4, 2), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6, 2), (ushort)header.MessageType);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), header.PayloadLength);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12, 4), header.PlaybackGeneration);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(16, 8), header.Sequence);
        header.SessionId.TryWriteBytes(bytes.AsSpan(24, 16));
        return bytes;
    }

    public static LiveProtocolHeader DecodeHeader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != HeaderSize)
            throw new InvalidDataException($"Protocol header must be exactly {HeaderSize} bytes.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[0..4]) != Magic)
            throw new InvalidDataException("Live translation protocol magic is invalid.");
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..6]);
        if (version != Version)
            throw new InvalidDataException($"Unsupported live translation protocol version {version}.");

        ushort rawType = BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..8]);
        if (!Enum.IsDefined((LiveMessageType)rawType))
            throw new InvalidDataException($"Unknown live translation message type {rawType}.");
        int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..12]);
        ValidatePayloadLength(payloadLength);
        int generation = BinaryPrimitives.ReadInt32LittleEndian(bytes[12..16]);
        long sequence = BinaryPrimitives.ReadInt64LittleEndian(bytes[16..24]);
        var session = new Guid(bytes[24..40]);
        if (session == Guid.Empty)
            throw new InvalidDataException("Protocol session ID cannot be empty.");
        return new LiveProtocolHeader((LiveMessageType)rawType, session, generation, sequence, payloadLength);
    }

    public static byte[] EncodeHello(LivePeerRole role) => [(byte)role];

    public static LivePeerRole DecodeHello(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 1 || !Enum.IsDefined((LivePeerRole)payload[0]))
            throw new InvalidDataException("Invalid hello payload.");
        return (LivePeerRole)payload[0];
    }

    public static byte[] EncodeConfigure(LiveConfigureMessage value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var writer = new PayloadWriter();
        writer.WriteString(value.SpeechModelId);
        writer.WriteString(value.TranslationModelId);
        writer.WriteString(value.SpeechProviderId);
        writer.WriteString(value.TranslationProviderId);
        writer.WriteString(value.SourceLanguage);
        writer.WriteString(value.TargetLanguage);
        writer.WriteInt32(value.SpeechThreads);
        writer.WriteInt32(value.TranslationThreads);
        writer.WriteInt32(value.InputDelayMilliseconds);
        writer.WriteInt32(value.VadSilenceMilliseconds);
        writer.WriteInt32(value.MaximumUtteranceMilliseconds);
        writer.WriteSingle(value.EnergyVadThreshold);
        writer.WriteByte(value.FakeInference ? (byte)1 : (byte)0);
        return writer.ToArray();
    }

    public static LiveConfigureMessage DecodeConfigure(ReadOnlySpan<byte> payload)
    {
        var reader = new PayloadReader(payload);
        var value = new LiveConfigureMessage
        {
            SpeechModelId = reader.ReadIdentifier(),
            TranslationModelId = reader.ReadIdentifier(),
            SpeechProviderId = reader.ReadIdentifier(),
            TranslationProviderId = reader.ReadIdentifier(),
            SourceLanguage = reader.ReadIdentifier(),
            TargetLanguage = reader.ReadIdentifier(),
            SpeechThreads = reader.ReadInt32(),
            TranslationThreads = reader.ReadInt32(),
            InputDelayMilliseconds = reader.ReadInt32(),
            VadSilenceMilliseconds = reader.ReadInt32(),
            MaximumUtteranceMilliseconds = reader.ReadInt32(),
            EnergyVadThreshold = reader.ReadSingle(),
            FakeInference = reader.ReadByte() != 0
        };
        reader.RequireEnd();
        ValidateConfigure(value);
        return value;
    }

    public static byte[] EncodeReady(LiveReadyMessage value)
    {
        using var writer = new PayloadWriter();
        writer.WriteString(value.SpeechModelId);
        writer.WriteString(value.TranslationModelId);
        writer.WriteString(value.SpeechProviderId);
        writer.WriteString(value.TranslationProviderId);
        writer.WriteString(value.ProviderFallbackReason, MaximumTextBytes);
        writer.WriteInt64(value.InitializationMilliseconds);
        writer.WriteInt64(value.WarmupMilliseconds);
        return writer.ToArray();
    }

    public static LiveReadyMessage DecodeReady(ReadOnlySpan<byte> payload)
    {
        var reader = new PayloadReader(payload);
        var value = new LiveReadyMessage
        {
            SpeechModelId = reader.ReadIdentifier(),
            TranslationModelId = reader.ReadIdentifier(),
            SpeechProviderId = reader.ReadIdentifier(),
            TranslationProviderId = reader.ReadIdentifier(),
            ProviderFallbackReason = reader.ReadString(MaximumTextBytes),
            InitializationMilliseconds = reader.ReadInt64(),
            WarmupMilliseconds = reader.ReadInt64()
        };
        reader.RequireEnd();
        return value;
    }

    public static byte[] EncodeAudio(LiveAudioMessage value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return EncodeAudio(
            value.Format,
            value.SampleRate,
            value.Channels,
            value.SourcePts,
            value.DurationTicks,
            value.AudioBytes);
    }

    public static byte[] EncodeAudio(
        LiveAudioSampleFormat format,
        int sampleRate,
        ushort channels,
        long sourcePts,
        long durationTicks,
        ReadOnlySpan<byte> audioBytes)
    {
        ValidateAudio(format, sampleRate, channels, sourcePts, durationTicks, audioBytes.Length);
        using var writer = new PayloadWriter(28 + audioBytes.Length);
        writer.WriteInt64(sourcePts);
        writer.WriteInt64(durationTicks);
        writer.WriteInt32(sampleRate);
        writer.WriteUInt16(channels);
        writer.WriteByte((byte)format);
        writer.WriteByte(0);
        writer.WriteInt32(audioBytes.Length);
        writer.WriteBytes(audioBytes);
        return writer.ToArray();
    }

    public static LiveAudioMessage DecodeAudio(ReadOnlySpan<byte> payload)
    {
        var reader = new PayloadReader(payload);
        long sourcePts = reader.ReadInt64();
        long duration = reader.ReadInt64();
        int sampleRate = reader.ReadInt32();
        ushort channels = reader.ReadUInt16();
        var format = (LiveAudioSampleFormat)reader.ReadByte();
        _ = reader.ReadByte();
        int length = reader.ReadInt32();
        byte[] bytes = reader.ReadBytes(length);
        reader.RequireEnd();
        var value = new LiveAudioMessage
        {
            SourcePts = sourcePts,
            DurationTicks = duration,
            SampleRate = sampleRate,
            Channels = channels,
            Format = format,
            AudioBytes = bytes
        };
        ValidateAudio(value);
        return value;
    }

    public static byte[] EncodeCue(LiveCueMessage value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.SourceStartPts < 0 || value.SourceEndPts <= value.SourceStartPts)
            throw new InvalidDataException("Cue source interval is invalid.");
        using var writer = new PayloadWriter();
        writer.WriteInt64(value.SourceStartPts);
        writer.WriteInt64(value.SourceEndPts);
        writer.WriteInt64(value.CompletedSystemTicks);
        writer.WriteInt64(value.SemanticLatencyTicks);
        writer.WriteString(value.Text, MaximumTextBytes);
        return writer.ToArray();
    }

    public static LiveCueMessage DecodeCue(ReadOnlySpan<byte> payload)
    {
        var reader = new PayloadReader(payload);
        var value = new LiveCueMessage
        {
            SourceStartPts = reader.ReadInt64(),
            SourceEndPts = reader.ReadInt64(),
            CompletedSystemTicks = reader.ReadInt64(),
            SemanticLatencyTicks = reader.ReadInt64(),
            Text = reader.ReadString(MaximumTextBytes)
        };
        reader.RequireEnd();
        if (value.SourceStartPts < 0 || value.SourceEndPts <= value.SourceStartPts ||
            string.IsNullOrWhiteSpace(value.Text))
        {
            throw new InvalidDataException("Cue payload is invalid.");
        }
        return value;
    }

    public static byte[] EncodeMetrics(LiveMetricsMessage value)
    {
        using var writer = new PayloadWriter(72);
        writer.WriteDouble(value.RollingRealTimeFactor);
        writer.WriteDouble(value.TotalRealTimeFactor);
        writer.WriteInt64(value.CueLatencyP50Ticks);
        writer.WriteInt64(value.CueLatencyP95Ticks);
        writer.WriteInt64(value.CueLatencyP99Ticks);
        writer.WriteInt64(value.QueueDepth);
        writer.WriteInt64(value.DroppedAudio);
        writer.WriteInt64(value.DroppedUtterances);
        writer.WriteInt64(value.StaleCompletions);
        return writer.ToArray();
    }

    public static LiveMetricsMessage DecodeMetrics(ReadOnlySpan<byte> payload)
    {
        var reader = new PayloadReader(payload);
        var value = new LiveMetricsMessage
        {
            RollingRealTimeFactor = reader.ReadDouble(),
            TotalRealTimeFactor = reader.ReadDouble(),
            CueLatencyP50Ticks = reader.ReadInt64(),
            CueLatencyP95Ticks = reader.ReadInt64(),
            CueLatencyP99Ticks = reader.ReadInt64(),
            QueueDepth = reader.ReadInt64(),
            DroppedAudio = reader.ReadInt64(),
            DroppedUtterances = reader.ReadInt64(),
            StaleCompletions = reader.ReadInt64()
        };
        reader.RequireEnd();
        return value;
    }

    public static byte[] EncodeError(LiveErrorMessage value)
    {
        using var writer = new PayloadWriter();
        writer.WriteString(value.Code);
        writer.WriteString(value.Message, MaximumTextBytes);
        writer.WriteByte(value.Fatal ? (byte)1 : (byte)0);
        return writer.ToArray();
    }

    public static LiveErrorMessage DecodeError(ReadOnlySpan<byte> payload)
    {
        var reader = new PayloadReader(payload);
        var value = new LiveErrorMessage
        {
            Code = reader.ReadIdentifier(),
            Message = reader.ReadString(MaximumTextBytes),
            Fatal = reader.ReadByte() != 0
        };
        reader.RequireEnd();
        return value;
    }

    private static void ValidatePayloadLength(int payloadLength)
    {
        if (payloadLength < 0 || payloadLength > MaximumPayloadBytes)
            throw new InvalidDataException($"Payload length {payloadLength} exceeds the protocol limit.");
    }

    private static void ValidateConfigure(LiveConfigureMessage value)
    {
        if (value.SpeechThreads is < 1 or > 64 || value.TranslationThreads is < 1 or > 64)
            throw new InvalidDataException("Inference thread count is invalid.");
        if (value.InputDelayMilliseconds is < 0 or > 60_000)
            throw new InvalidDataException("Input delay is outside VLC's supported range.");
        if (value.VadSilenceMilliseconds is < 200 or > 2_000 ||
            value.MaximumUtteranceMilliseconds is < 1_000 or > 15_000 ||
            value.EnergyVadThreshold is < 0.0001f or > 1f)
        {
            throw new InvalidDataException("Segmentation tuning is invalid.");
        }
    }

    private static void ValidateAudio(LiveAudioMessage value) =>
        ValidateAudio(
            value.Format,
            value.SampleRate,
            value.Channels,
            value.SourcePts,
            value.DurationTicks,
            value.AudioBytes.Length);

    private static void ValidateAudio(
        LiveAudioSampleFormat format,
        int sampleRate,
        ushort channels,
        long sourcePts,
        long durationTicks,
        int audioLength)
    {
        if (!Enum.IsDefined(format) || sampleRate is < 8_000 or > 384_000 ||
            channels is < 1 or > 32 || sourcePts < 0 || durationTicks <= 0)
        {
            throw new InvalidDataException("Audio metadata is invalid.");
        }
        int bytesPerSample = format == LiveAudioSampleFormat.Float32LittleEndian ? 4 : 2;
        if (audioLength == 0 ||
            audioLength > MaximumPayloadBytes - 28 ||
            audioLength % (bytesPerSample * channels) != 0)
        {
            throw new InvalidDataException("Audio payload length is invalid.");
        }
    }

    private sealed class PayloadWriter : IDisposable
    {
        private readonly MemoryStream _stream;

        public PayloadWriter(int capacity = 128) => _stream = new MemoryStream(capacity);

        public void WriteByte(byte value) => _stream.WriteByte(value);

        public void WriteUInt16(ushort value)
        {
            Span<byte> bytes = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
            _stream.Write(bytes);
        }

        public void WriteInt32(int value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            _stream.Write(bytes);
        }

        public void WriteInt64(long value)
        {
            Span<byte> bytes = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
            _stream.Write(bytes);
        }

        public void WriteSingle(float value) => WriteInt32(BitConverter.SingleToInt32Bits(value));
        public void WriteDouble(double value) => WriteInt64(BitConverter.DoubleToInt64Bits(value));

        public void WriteString(string value, int maximumBytes = MaximumIdentifierBytes)
        {
            value ??= "";
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length > maximumBytes)
                throw new InvalidDataException($"UTF-8 value exceeds {maximumBytes} bytes.");
            WriteInt32(bytes.Length);
            WriteBytes(bytes);
        }

        public void WriteBytes(ReadOnlySpan<byte> value) => _stream.Write(value);
        public byte[] ToArray() => _stream.ToArray();
        public void Dispose() => _stream.Dispose();
    }

    private ref struct PayloadReader
    {
        private readonly ReadOnlySpan<byte> _payload;
        private int _offset;

        public PayloadReader(ReadOnlySpan<byte> payload) => _payload = payload;

        public byte ReadByte()
        {
            Require(1);
            return _payload[_offset++];
        }

        public ushort ReadUInt16()
        {
            Require(2);
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(_offset, 2));
            _offset += 2;
            return value;
        }

        public int ReadInt32()
        {
            Require(4);
            int value = BinaryPrimitives.ReadInt32LittleEndian(_payload.Slice(_offset, 4));
            _offset += 4;
            return value;
        }

        public long ReadInt64()
        {
            Require(8);
            long value = BinaryPrimitives.ReadInt64LittleEndian(_payload.Slice(_offset, 8));
            _offset += 8;
            return value;
        }

        public float ReadSingle() => BitConverter.Int32BitsToSingle(ReadInt32());
        public double ReadDouble() => BitConverter.Int64BitsToDouble(ReadInt64());
        public string ReadIdentifier() => ReadString(MaximumIdentifierBytes);

        public string ReadString(int maximumBytes)
        {
            int length = ReadInt32();
            if (length < 0 || length > maximumBytes)
                throw new InvalidDataException("UTF-8 value length is invalid.");
            Require(length);
            string value;
            try
            {
                value = new UTF8Encoding(false, true).GetString(_payload.Slice(_offset, length));
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException("UTF-8 value is malformed.", ex);
            }
            _offset += length;
            return value;
        }

        public byte[] ReadBytes(int length)
        {
            if (length < 0)
                throw new InvalidDataException("Byte array length cannot be negative.");
            Require(length);
            byte[] value = _payload.Slice(_offset, length).ToArray();
            _offset += length;
            return value;
        }

        public void RequireEnd()
        {
            if (_offset != _payload.Length)
                throw new InvalidDataException("Protocol payload has trailing bytes.");
        }

        private void Require(int count)
        {
            if (count < 0 || _offset > _payload.Length - count)
                throw new EndOfStreamException("Protocol payload is truncated.");
        }
    }
}
