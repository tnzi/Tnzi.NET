using System.Net.Sockets;

namespace Tnzi.AI.Sandbox.Providers.Docker;

/// <summary>
/// HTTP message handler that connects to the Docker daemon via Unix socket or Windows named pipe.
/// </summary>
internal sealed class DockerSocketHandler : HttpMessageHandler
{
    private readonly string _dockerHost;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    public DockerSocketHandler(string dockerHost)
    {
        _dockerHost = Check.NotNullOrWhiteSpace(dockerHost);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var socket = await ConnectAsync(ct);
        var stream = new NetworkStream(socket, ownsSocket: true);

        // 构建 raw HTTP 请求
        var requestLine = $"{request.Method} {request.RequestUri?.PathAndQuery} HTTP/1.1\r\n";
        var headers = $"Host: localhost\r\n";

        byte[]? contentBytes = null;
        if (request.Content is not null)
        {
            contentBytes = await request.Content.ReadAsByteArrayAsync(ct);
            headers += $"Content-Type: {request.Content.Headers.ContentType}\r\n";
            headers += $"Content-Length: {contentBytes.Length}\r\n";
        }
        headers += "\r\n";

        var headerBytes = Encoding.ASCII.GetBytes(requestLine + headers);
        await stream.WriteAsync(headerBytes, ct);

        if (contentBytes is not null)
            await stream.WriteAsync(contentBytes, ct);

        await stream.FlushAsync(ct);

        // 解析 HTTP 响应
        return await ParseResponseAsync(stream, ct);
    }

    private async Task<Socket> ConnectAsync(CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ConnectTimeout);
        var linkedCt = timeoutCts.Token;

        try
        {
            if (_dockerHost.StartsWith("unix://", StringComparison.OrdinalIgnoreCase))
            {
                var socketPath = _dockerHost["unix://".Length..];
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), linkedCt);
                return socket;
            }

            if (_dockerHost.StartsWith("npipe://", StringComparison.OrdinalIgnoreCase))
            {
                // Windows named pipe — connect via TCP loopback as a fallback
                // Docker Desktop on Windows also exposes TCP on localhost:2375
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                var pipePath = _dockerHost["npipe://".Length..].Replace("/", "\\");
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(pipePath), linkedCt);
                return socket;
            }

            if (_dockerHost.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(_dockerHost);
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(uri.Host, uri.Port, linkedCt);
                return socket;
            }

            throw new InvalidOperationException($"Unsupported Docker host scheme: {_dockerHost}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Docker socket connection timed out after {ConnectTimeout.TotalSeconds}s for host: {_dockerHost}");
        }
    }

    private static async Task<HttpResponseMessage> ParseResponseAsync(NetworkStream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

        // 读取状态行
        var statusLine = await reader.ReadLineAsync(ct)
            ?? throw new InvalidOperationException("Empty response from Docker daemon");

        var statusParts = statusLine.Split(' ', 3);
        if (statusParts.Length < 2)
            throw new InvalidOperationException($"Invalid HTTP response: {statusLine}");

        var statusCode = int.Parse(statusParts[1]);
        var response = new HttpResponseMessage((System.Net.HttpStatusCode)statusCode);

        // 读取 headers
        var contentLength = -1L;
        var isChunked = false;
        while (true)
        {
            var headerLine = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(headerLine)) break;

            var colonIdx = headerLine.IndexOf(':');
            if (colonIdx <= 0) continue;

            var name = headerLine[..colonIdx].Trim();
            var value = headerLine[(colonIdx + 1)..].Trim();

            if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
                contentLength = long.Parse(value);
            if (string.Equals(name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
                && value.Contains("chunked", StringComparison.OrdinalIgnoreCase))
                isChunked = true;
        }

        // 读取 body
        if (contentLength > 0)
        {
            var buffer = new char[contentLength];
            var read = await reader.ReadBlockAsync(buffer, 0, (int)contentLength);
            response.Content = new StringContent(new string(buffer, 0, read));
        }
        else if (isChunked)
        {
            var body = new StringBuilder();
            while (true)
            {
                var chunkSizeLine = await reader.ReadLineAsync(ct);
                if (string.IsNullOrEmpty(chunkSizeLine)) break;

                var chunkSize = Convert.ToInt32(chunkSizeLine.Trim(), 16);
                if (chunkSize == 0) break;

                var chunkBuffer = new char[chunkSize];
                await reader.ReadBlockAsync(chunkBuffer, 0, chunkSize);
                body.Append(chunkBuffer);

                await reader.ReadLineAsync(ct); // trailing CRLF
            }
            response.Content = new StringContent(body.ToString());
        }
        else
        {
            // 尝试读取剩余内容
            var remaining = await reader.ReadToEndAsync(ct);
            if (!string.IsNullOrEmpty(remaining))
                response.Content = new StringContent(remaining);
        }

        return response;
    }
}
