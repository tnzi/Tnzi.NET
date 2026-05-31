using System.Net.WebSockets;

namespace Tnzi.AI.Tests.Channels.Gateway;

/// <summary>
/// Test double for <see cref="WebSocket"/> that records sends, tracks concurrent in-flight
/// sends (to assert serialization), and can be scripted with inbound text/close frames.
/// </summary>
public sealed class FakeWebSocket : WebSocket
{
    private readonly Queue<Func<ArraySegment<byte>, WebSocketReceiveResult>> _receiveScript = new();
    private int _inFlightSends;

    public int MaxConcurrentSends { get; private set; }
    public WebSocketCloseStatus? CloseStatusValue { get; private set; }
    public string? CloseDescriptionValue { get; private set; }
    public List<string> SentMessages { get; } = new();

    /// <summary>Optional per-send delay to widen the window for concurrent-send detection.</summary>
    public TimeSpan SendDelay { get; set; } = TimeSpan.Zero;

    private WebSocketState _state = WebSocketState.Open;
    public override WebSocketState State => _state;

    public override WebSocketCloseStatus? CloseStatus => CloseStatusValue;
    public override string? CloseStatusDescription => CloseDescriptionValue;
    public override string? SubProtocol => null;

    /// <summary>Enqueue a text frame to be returned by ReceiveAsync.</summary>
    public void EnqueueText(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        _receiveScript.Enqueue(buffer =>
        {
            var count = Math.Min(bytes.Length, buffer.Count);
            Array.Copy(bytes, 0, buffer.Array!, buffer.Offset, count);
            return new WebSocketReceiveResult(count, WebSocketMessageType.Text, endOfMessage: true);
        });
    }

    /// <summary>Enqueue a close frame.</summary>
    public void EnqueueClose()
    {
        _receiveScript.Enqueue(_ =>
            new WebSocketReceiveResult(0, WebSocketMessageType.Close, endOfMessage: true,
                WebSocketCloseStatus.NormalClosure, "client closed"));
    }

    public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_receiveScript.Count == 0)
        {
            // Block until cancelled — mimics an idle open connection.
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        return _receiveScript.Dequeue()(buffer);
    }

    public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        var current = Interlocked.Increment(ref _inFlightSends);
        // Record the high-water mark of concurrent sends (lock-free best-effort).
        if (current > MaxConcurrentSends) MaxConcurrentSends = current;
        try
        {
            SentMessages.Add(System.Text.Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count));
            if (SendDelay > TimeSpan.Zero)
            {
                await Task.Delay(SendDelay, cancellationToken);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _inFlightSends);
        }
    }

    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        CloseStatusValue = closeStatus;
        CloseDescriptionValue = statusDescription;
        _state = WebSocketState.Closed;
        return Task.CompletedTask;
    }

    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        => CloseAsync(closeStatus, statusDescription, cancellationToken);

    public override void Abort() => _state = WebSocketState.Aborted;

    public override void Dispose() { }
}
