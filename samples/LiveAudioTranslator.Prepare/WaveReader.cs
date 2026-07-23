using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace LiveAudioTranslator;

internal sealed class WaveReader : IDisposable
{
    private const int PcmFormat = 1;
    private const int ExtensibleFormat = 0xFFFE;
    private readonly FileStream _stream;
    private long _remainingBytes;

    public WaveReader(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        _stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1_024, FileOptions.SequentialScan);
        try
        {
            ParseHeader(out long dataOffset, out long dataLength);
            if (Channels != 1 || SampleRate != 16_000 || BitsPerSample != 16)
            {
                throw new InvalidDataException(
                    $"WAV must be 16-kHz mono PCM16; got {SampleRate} Hz, {Channels} channel(s), {BitsPerSample} bits.");
            }
            if (dataLength < SampleRate / 10 * sizeof(short))
                throw new InvalidDataException("WAV contains less than 100 ms of audio.");
            if (dataLength % BlockAlign != 0)
                throw new InvalidDataException("WAV data length is not aligned to complete samples.");

            SampleCount = dataLength / BlockAlign;
            DurationTicks = checked(SampleCount * 1_000_000L / SampleRate);
            if (DurationTicks <= 0 || DurationTicks > 24L * 60 * 60 * 1_000_000)
                throw new InvalidDataException("WAV duration is implausible.");
            _remainingBytes = dataLength;
            _stream.Position = dataOffset;
        }
        catch
        {
            _stream.Dispose();
            throw;
        }
    }

    public string Path { get; }
    public int Channels { get; private set; }
    public int SampleRate { get; private set; }
    public int BitsPerSample { get; private set; }
    public int BlockAlign { get; private set; }
    public long SampleCount { get; }
    public long DurationTicks { get; }

    public int ReadSamples(Span<short> destination)
    {
        if (destination.IsEmpty || _remainingBytes <= 0)
            return 0;
        Span<byte> bytes = MemoryMarshal.AsBytes(destination);
        int requested = checked((int)Math.Min(bytes.Length, _remainingBytes));
        int total = 0;
        while (total < requested)
        {
            int read = _stream.Read(bytes.Slice(total, requested - total));
            if (read == 0)
                throw new EndOfStreamException("WAV data ended before its declared length.");
            total += read;
        }
        _remainingBytes -= total;
        return total / sizeof(short);
    }

    private void ParseHeader(out long dataOffset, out long dataLength)
    {
        dataOffset = -1;
        dataLength = -1;
        Span<byte> header = stackalloc byte[12];
        ReadExactly(header);
        if (!header[..4].SequenceEqual("RIFF"u8) || !header[8..12].SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Audio extraction output is not a RIFF/WAVE file.");

        bool foundFormat = false;
        Span<byte> chunkHeader = stackalloc byte[8];
        while (_stream.Position + chunkHeader.Length <= _stream.Length)
        {
            ReadExactly(chunkHeader);
            string chunkId = Encoding.ASCII.GetString(chunkHeader[..4]);
            uint chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..]);
            long chunkStart = _stream.Position;
            long chunkEnd = checked(chunkStart + chunkSize);
            if (chunkEnd > _stream.Length)
                throw new InvalidDataException($"WAV chunk '{chunkId}' extends past the end of the file.");

            if (chunkId == "fmt ")
            {
                if (chunkSize < 16 || chunkSize > 4_096)
                    throw new InvalidDataException("WAV format chunk has an invalid size.");
                byte[] format = new byte[chunkSize];
                ReadExactly(format);
                int formatTag = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(0, 2));
                Channels = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(2, 2));
                SampleRate = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(format.AsSpan(4, 4)));
                BlockAlign = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(12, 2));
                BitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(14, 2));
                bool extensiblePcm = formatTag == ExtensibleFormat && format.Length >= 40 &&
                    BinaryPrimitives.ReadUInt32LittleEndian(format.AsSpan(24, 4)) == PcmFormat;
                if (formatTag != PcmFormat && !extensiblePcm)
                    throw new InvalidDataException($"WAV format {formatTag} is not integer PCM.");
                foundFormat = true;
            }
            else if (chunkId == "data")
            {
                dataOffset = chunkStart;
                dataLength = chunkSize;
                _stream.Position = chunkEnd;
            }

            _stream.Position = chunkEnd + (chunkSize & 1);
            if (foundFormat && dataOffset >= 0)
                break;
        }

        if (!foundFormat || dataOffset < 0 || dataLength <= 0)
            throw new InvalidDataException("WAV is missing a format or non-empty data chunk.");
        if (BlockAlign != Channels * BitsPerSample / 8)
            throw new InvalidDataException("WAV block alignment does not match its PCM format.");
    }

    private void ReadExactly(Span<byte> destination)
    {
        int total = 0;
        while (total < destination.Length)
        {
            int read = _stream.Read(destination[total..]);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of WAV header.");
            total += read;
        }
    }

    public void Dispose() => _stream.Dispose();
}
