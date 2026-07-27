namespace VLCLR.LiveTranslation.Protocol;

public static class LiveProtocolStream
{
    public static async ValueTask<LiveProtocolMessage?> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var headerBytes = new byte[LiveProtocol.HeaderSize];
        int headerRead = await ReadExactAsync(
            stream,
            headerBytes,
            allowCleanEndOfStream: true,
            cancellationToken).ConfigureAwait(false);
        if (headerRead == 0)
            return null;

        LiveProtocolHeader header = LiveProtocol.DecodeHeader(headerBytes);
        var payload = new byte[header.PayloadLength];
        if (payload.Length > 0)
        {
            _ = await ReadExactAsync(
                stream,
                payload,
                allowCleanEndOfStream: false,
                cancellationToken).ConfigureAwait(false);
        }
        return new LiveProtocolMessage(header, payload);
    }

    public static async ValueTask WriteAsync(
        Stream stream,
        LiveProtocolMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(message);
        if (message.Payload.Length != message.Header.PayloadLength)
            throw new InvalidDataException("Message payload length does not match its header.");

        byte[] header = LiveProtocol.EncodeHeader(message.Header);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        if (message.Payload.Length > 0)
            await stream.WriteAsync(message.Payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<int> ReadExactAsync(
        Stream stream,
        Memory<byte> destination,
        bool allowCleanEndOfStream,
        CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < destination.Length)
        {
            int read = await stream.ReadAsync(destination[total..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (allowCleanEndOfStream && total == 0)
                    return 0;
                throw new EndOfStreamException(
                    $"Protocol stream ended after {total} of {destination.Length} required bytes.");
            }
            total += read;
        }
        return total;
    }
}
