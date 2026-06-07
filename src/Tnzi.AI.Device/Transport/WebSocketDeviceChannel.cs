namespace Tnzi.AI.Device.Transport;

/// <summary>
/// <see cref="IDeviceChannel"/> adapter over a real <see cref="WebSocket"/>.
/// Each send/receive is serialised by the caller (WebSocketDeviceNode reads the WS on a dedicated loop).
/// </summary>
public sealed class WebSocketDeviceChannel : IDeviceChannel, IAsyncDisposable
{
    private readonly WebSocket _ws;
    // Reusable send buffer — guarded by _sendLock (one outstanding send at a time per WS)
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public WebSocketDeviceChannel(WebSocket webSocket)
    {
        _ws = Check.NotNull(webSocket);
    }

    public bool IsOpen =>
        _ws.State == WebSocketState.Open;

    public async Task SendAsync(string json, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(json);

        var bytes = Encoding.UTF8.GetBytes(json);
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _ws.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        // 使用可扩展缓冲区接收可能超过单个缓冲的消息
        using var ms = new MemoryStream();
        var buffer = new byte[4096];

        while (true)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            }
            catch (WebSocketException)
            {
                return null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                // Acknowledge the close handshake when still open
                if (_ws.State == WebSocketState.CloseReceived)
                {
                    try
                    {
                        await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    }
                    catch (WebSocketException) { /* ignore */ }
                }

                return null;
            }

            ms.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_ws.State == WebSocketState.Open)
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server closing", CancellationToken.None);
            }
            catch (WebSocketException) { /* ignore */ }
        }

        _ws.Dispose();
        _sendLock.Dispose();
    }
}
