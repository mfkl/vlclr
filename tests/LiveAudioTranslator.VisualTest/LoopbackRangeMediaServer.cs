using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LiveAudioTranslator.VisualTest;

internal sealed record LoopbackMediaServerMetrics(
    long Requests,
    long RangeRequests,
    long BytesServed,
    long RejectedRequests);

/// <summary>
/// Serves one local media file over an owned loopback HTTP endpoint. VLC seeks
/// MP4 inputs with byte ranges, so the harness must not depend on a generic
/// development server whose range behavior can vary.
/// </summary>
internal sealed class LoopbackRangeMediaServer : IAsyncDisposable
{
    private const int MaximumHeaderLines = 64;
    private const int MaximumHeaderLength = 16 * 1024;
    private readonly string _mediaPath;
    private readonly string _requestPath;
    private readonly long _mediaLength;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<long, Task> _requests = new();
    private readonly ConcurrentQueue<string> _trace = new();
    private readonly Task _acceptLoop;
    private long _requestSequence;
    private long _requestCount;
    private long _rangeRequestCount;
    private long _bytesServed;
    private long _rejectedRequests;

    private LoopbackRangeMediaServer(string mediaPath)
    {
        _mediaPath = Path.GetFullPath(mediaPath);
        _mediaLength = new FileInfo(_mediaPath).Length;
        string token = Guid.NewGuid().ToString("N");
        string fileName = Uri.EscapeDataString(Path.GetFileName(_mediaPath));
        _requestPath = $"/media/{token}/{fileName}";
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        MediaUri = new Uri($"http://127.0.0.1:{port}{_requestPath}");
        _trace.Enqueue(
            $"event=http_server outcome=listening port={port} media_bytes={_mediaLength}");
        _acceptLoop = AcceptLoopAsync();
    }

    public Uri MediaUri { get; }

    public static LoopbackRangeMediaServer Start(string mediaPath)
    {
        if (!File.Exists(mediaPath))
            throw new FileNotFoundException("Loopback media input was not found.", mediaPath);
        if (new FileInfo(mediaPath).Length <= 0)
            throw new InvalidDataException("Loopback media input is empty.");
        return new LoopbackRangeMediaServer(mediaPath);
    }

    public LoopbackMediaServerMetrics Snapshot() =>
        new(
            Interlocked.Read(ref _requestCount),
            Interlocked.Read(ref _rangeRequestCount),
            Interlocked.Read(ref _bytesServed),
            Interlocked.Read(ref _rejectedRequests));

    public string[] GetTrace() => _trace.ToArray();

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(_shutdown.Token)
                    .ConfigureAwait(false);
                long requestId = Interlocked.Increment(ref _requestSequence);
                Task request = HandleClientAsync(client, requestId, _shutdown.Token);
                _requests[requestId] = request;
                _ = request.ContinueWith(
                    static (completed, state) =>
                    {
                        var item = ((ConcurrentDictionary<long, Task> Requests, long Id))state!;
                        item.Requests.TryRemove(item.Id, out Task? ignored);
                    },
                    (_requests, requestId),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested)
        {
        }
    }

    private async Task HandleClientAsync(
        TcpClient client,
        long requestId,
        CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                NetworkStream stream = client.GetStream();
                using var reader = new StreamReader(
                    stream,
                    Encoding.ASCII,
                    detectEncodingFromByteOrderMarks: false,
                    bufferSize: 4 * 1024,
                    leaveOpen: true);
                string? requestLine = await reader.ReadLineAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(requestLine) ||
                    requestLine.Length > MaximumHeaderLength)
                {
                    await RejectAsync(stream, 400, "Bad Request", cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                string[] requestParts = requestLine.Split(' ', 3);
                if (requestParts.Length != 3)
                {
                    await RejectAsync(stream, 400, "Bad Request", cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int totalHeaderLength = requestLine.Length;
                for (int lineIndex = 0; lineIndex < MaximumHeaderLines; lineIndex++)
                {
                    string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line == null)
                        return;
                    totalHeaderLength += line.Length;
                    if (totalHeaderLength > MaximumHeaderLength)
                    {
                        await RejectAsync(
                                stream,
                                431,
                                "Request Header Fields Too Large",
                                cancellationToken)
                            .ConfigureAwait(false);
                        return;
                    }
                    if (line.Length == 0)
                        break;
                    int separator = line.IndexOf(':');
                    if (separator > 0)
                        headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
                    if (lineIndex == MaximumHeaderLines - 1)
                    {
                        await RejectAsync(
                                stream,
                                431,
                                "Request Header Fields Too Large",
                                cancellationToken)
                            .ConfigureAwait(false);
                        return;
                    }
                }

                string method = requestParts[0];
                string targetPath = requestParts[1].Split('?', 2)[0];
                if (method is not ("GET" or "HEAD") ||
                    !string.Equals(targetPath, _requestPath, StringComparison.Ordinal))
                {
                    await RejectAsync(
                            stream,
                            method is "GET" or "HEAD" ? 404 : 405,
                            method is "GET" or "HEAD" ? "Not Found" : "Method Not Allowed",
                            cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                bool hasRange = headers.TryGetValue("Range", out string? rangeHeader);
                if (!TryParseRange(rangeHeader, out long start, out long end))
                {
                    Interlocked.Increment(ref _rejectedRequests);
                    await WriteHeadersAsync(
                            stream,
                            "HTTP/1.1 416 Range Not Satisfiable",
                            [
                                "Accept-Ranges: bytes",
                                $"Content-Range: bytes */{_mediaLength}",
                                "Content-Length: 0",
                                "Connection: close"
                            ],
                            cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                long count = checked(end - start + 1);
                Interlocked.Increment(ref _requestCount);
                if (hasRange)
                    Interlocked.Increment(ref _rangeRequestCount);
                _trace.Enqueue(
                    $"event=http_request id={requestId} method={method} range={start}-{end} " +
                    $"bytes={count} partial={hasRange}");

                var responseHeaders = new List<string>
                {
                    "Accept-Ranges: bytes",
                    $"Content-Length: {count}",
                    $"Content-Type: {ContentType(_mediaPath)}",
                    "Cache-Control: no-store",
                    "Connection: close"
                };
                if (hasRange)
                    responseHeaders.Insert(1, $"Content-Range: bytes {start}-{end}/{_mediaLength}");
                await WriteHeadersAsync(
                        stream,
                        hasRange ? "HTTP/1.1 206 Partial Content" : "HTTP/1.1 200 OK",
                        responseHeaders,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (method == "HEAD")
                    return;

                await using var media = new FileStream(
                    _mediaPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                media.Position = start;
                byte[] buffer = new byte[64 * 1024];
                long remaining = count;
                while (remaining > 0)
                {
                    int read = await media.ReadAsync(
                            buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                        throw new EndOfStreamException("Media file ended during a range response.");
                    await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);
                    remaining -= read;
                    Interlocked.Add(ref _bytesServed, read);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (IOException exception)
            {
                _trace.Enqueue(
                    $"event=http_request id={requestId} outcome=client-disconnected " +
                    $"error={exception.GetType().Name}");
            }
            catch (SocketException exception)
            {
                _trace.Enqueue(
                    $"event=http_request id={requestId} outcome=client-disconnected " +
                    $"error={exception.SocketErrorCode}");
            }
            catch (Exception exception)
            {
                _trace.Enqueue(
                    $"event=http_request id={requestId} outcome=failed " +
                    $"error={exception.GetType().Name}");
            }
        }
    }

    private bool TryParseRange(string? value, out long start, out long end)
    {
        start = 0;
        end = _mediaLength - 1;
        if (string.IsNullOrWhiteSpace(value))
            return true;
        if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) ||
            value.Contains(',', StringComparison.Ordinal))
        {
            return false;
        }

        string range = value["bytes=".Length..].Trim();
        int separator = range.IndexOf('-');
        if (separator < 0)
            return false;
        string first = range[..separator].Trim();
        string last = range[(separator + 1)..].Trim();
        if (first.Length == 0)
        {
            if (!long.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out long suffix) ||
                suffix <= 0)
            {
                return false;
            }
            start = Math.Max(0, _mediaLength - suffix);
            return true;
        }

        if (!long.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out start) ||
            start < 0 ||
            start >= _mediaLength)
        {
            return false;
        }
        if (last.Length == 0)
            return true;
        if (!long.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out end) ||
            end < start)
        {
            return false;
        }
        end = Math.Min(end, _mediaLength - 1);
        return true;
    }

    private async Task RejectAsync(
        Stream stream,
        int statusCode,
        string reason,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _rejectedRequests);
        await WriteHeadersAsync(
                stream,
                $"HTTP/1.1 {statusCode} {reason}",
                ["Content-Length: 0", "Connection: close"],
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task WriteHeadersAsync(
        Stream stream,
        string status,
        IEnumerable<string> headers,
        CancellationToken cancellationToken)
    {
        string response = status + "\r\n" + string.Join("\r\n", headers) + "\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken)
            .ConfigureAwait(false);
    }

    private static string ContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp4" or ".m4v" => "video/mp4",
            ".mkv" => "video/x-matroska",
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            ".avi" => "video/x-msvideo",
            _ => "application/octet-stream"
        };

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _listener.Stop();
        try
        {
            await _acceptLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        Task[] active = _requests.Values.ToArray();
        if (active.Length > 0)
        {
            try
            {
                await Task.WhenAll(active).WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }
        }
        _trace.Enqueue("event=http_server outcome=stopped");
        _shutdown.Dispose();
    }
}
