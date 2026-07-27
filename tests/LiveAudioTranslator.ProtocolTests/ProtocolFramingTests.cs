using VLCLR.LiveTranslation.Protocol;

namespace LiveAudioTranslator.ProtocolTests;

public sealed class ProtocolFramingTests
{
    private static readonly Guid Session = new("00112233-4455-6677-8899-aabbccddeeff");

    [Fact]
    public void HeaderHasStableGoldenLittleEndianBytes()
    {
        var header = new LiveProtocolHeader(
            LiveMessageType.Audio,
            Session,
            0x01020304,
            0x0102030405060708,
            0x0A0B0C);

        byte[] bytes = LiveProtocol.EncodeHeader(header);

        Assert.Equal(
            "564C4C54020004000C0B0A0004030201080706050403020133221100554477668899AABBCCDDEEFF",
            Convert.ToHexString(bytes));
        Assert.Equal(header, LiveProtocol.DecodeHeader(bytes));
    }

    [Fact]
    public async Task ExactReaderHandlesOneByteReads()
    {
        LiveProtocolMessage expected = LiveProtocol.Create(
            LiveMessageType.Cue,
            Session,
            7,
            42,
            LiveProtocol.EncodeCue(new LiveCueMessage
            {
                SourceStartPts = 1_000_000,
                SourceEndPts = 2_000_000,
                CompletedSystemTicks = 3_000_000,
                SemanticLatencyTicks = 400_000,
                Text = "bonjour"
            }));
        var bytes = new MemoryStream();
        await LiveProtocolStream.WriteAsync(bytes, expected);
        bytes.Position = 0;

        var partial = new ChunkedReadStream(bytes, 1);
        LiveProtocolMessage actual = (await LiveProtocolStream.ReadAsync(partial))!;

        Assert.Equal(expected.Header, actual.Header);
        Assert.Equal(expected.Payload, actual.Payload);
        Assert.Equal("bonjour", LiveProtocol.DecodeCue(actual.Payload).Text);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(39)]
    [InlineData(40)]
    [InlineData(43)]
    public async Task TruncatedMessagesFail(int retainedBytes)
    {
        LiveProtocolMessage message = LiveProtocol.Create(
            LiveMessageType.Error,
            Session,
            0,
            0,
            LiveProtocol.EncodeError(new LiveErrorMessage
            {
                Code = "failed",
                Message = "controlled failure",
                Fatal = true
            }));
        var complete = new MemoryStream();
        await LiveProtocolStream.WriteAsync(complete, message);
        byte[] truncated = complete.ToArray()[..Math.Min(retainedBytes, (int)complete.Length - 1)];

        await Assert.ThrowsAsync<EndOfStreamException>(async () =>
            await LiveProtocolStream.ReadAsync(new MemoryStream(truncated)));
    }

    [Fact]
    public void OversizedPayloadIsRejectedBeforeAllocation()
    {
        byte[] header = LiveProtocol.EncodeHeader(
            new LiveProtocolHeader(LiveMessageType.Audio, Session, 0, 0, 0));
        BitConverter.GetBytes(LiveProtocol.MaximumPayloadBytes + 1).CopyTo(header, 8);

        Assert.Throws<InvalidDataException>(() => LiveProtocol.DecodeHeader(header));
    }

    [Fact]
    public void AudioPayloadRoundTripsWithAllTimestampOwnershipFields()
    {
        var expected = new LiveAudioMessage
        {
            Format = LiveAudioSampleFormat.Pcm16LittleEndian,
            SampleRate = 48_000,
            Channels = 2,
            SourcePts = 15_123_456,
            DurationTicks = 20_000,
            AudioBytes = [1, 0, 2, 0]
        };

        LiveAudioMessage actual = LiveProtocol.DecodeAudio(LiveProtocol.EncodeAudio(expected));

        Assert.Equal(expected.Format, actual.Format);
        Assert.Equal(expected.SampleRate, actual.SampleRate);
        Assert.Equal(expected.Channels, actual.Channels);
        Assert.Equal(expected.SourcePts, actual.SourcePts);
        Assert.Equal(expected.DurationTicks, actual.DurationTicks);
        Assert.Equal(expected.AudioBytes, actual.AudioBytes);
    }

    [Theory]
    [InlineData("cpu")]
    [InlineData("gpu")]
    [InlineData("auto")]
    public void ConfigurePayloadRoundTripsSpeechDevice(string speechDevice)
    {
        var expected = new LiveConfigureMessage
        {
            SpeechModelId = "whisper-tiny-multilingual",
            TranslationModelId = "opus-mt-en-fr",
            SpeechProviderId = "openvino",
            SpeechDeviceId = speechDevice,
            TranslationProviderId = "cpu",
            SourceLanguage = "auto",
            TargetLanguage = "fr",
            SpeechThreads = 2,
            TranslationThreads = 1,
            InputDelayMilliseconds = 0,
            VadSilenceMilliseconds = 450,
            MaximumUtteranceMilliseconds = 2_500,
            EnergyVadThreshold = 0.012f
        };

        LiveConfigureMessage actual =
            LiveProtocol.DecodeConfigure(LiveProtocol.EncodeConfigure(expected));

        Assert.Equal(speechDevice, actual.SpeechDeviceId);
    }

    private sealed class ChunkedReadStream(Stream inner, int maximumRead) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, Math.Min(count, maximumRead));
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer[..Math.Min(buffer.Length, maximumRead)], cancellationToken);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
