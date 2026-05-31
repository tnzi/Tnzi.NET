using System.Net.WebSockets;
using System.Text;

namespace Tnzi.AI.Channels.Gateway;

/// <summary>
/// WebSocket 连接处理器 — 接收 GatewayMessage，按 Method 分发到 IGateway
/// </summary>
public class GatewayWebSocketHandler
{
    /// <summary>支持的 WebSocket 方法集合</summary>
    public static readonly HashSet<string> ValidMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "chat.send",
        "session.list",
        "session.get",
        "session.prune",
        "device.register",
        "device.invoke",
        "device.result",
        "heartbeat"
    };

    private readonly IGateway _gateway;
    private readonly IPresenceTracker _presence;
    private readonly ILogger<GatewayWebSocketHandler> _logger;
    private readonly int _maxConnectionsPerUser;
    private readonly int _heartbeatIntervalSeconds;
    private readonly bool _requireAuthentication;

    /// <summary>
    /// Serializes all sends on this connection — WebSocket forbids overlapping SendAsync
    /// calls (heartbeat vs stream), which otherwise corrupt frames / throw InvalidOperationException.
    /// </summary>
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public GatewayWebSocketHandler(IGateway gateway, IPresenceTracker presence, ILogger<GatewayWebSocketHandler> logger, int maxConnectionsPerUser = 5, int heartbeatIntervalSeconds = 30, bool requireAuthentication = false)
    {
        _gateway = Check.NotNull(gateway);
        _presence = Check.NotNull(presence);
        _logger = Check.NotNull(logger);
        _maxConnectionsPerUser = maxConnectionsPerUser;
        _heartbeatIntervalSeconds = heartbeatIntervalSeconds;
        _requireAuthentication = requireAuthentication;
    }

    /// <summary>处理 WebSocket 连接的完整生命周期</summary>
    public async Task HandleAsync(WebSocket ws, string? userId, CancellationToken ct)
    {
        // 要求认证时，拒绝匿名连接
        if (_requireAuthentication && string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("Rejecting anonymous WebSocket connection: AI:Channels:Gateway:RequireAuthentication is enabled");
            await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation,
                "Authentication required", CancellationToken.None);
            return;
        }

        // 检查每用户最大连接数 — 匿名连接共享 UserId==null 桶，同样受上限约束
        var existingConnections = _presence.GetConnections()
            .Count(c => c.UserId == userId);
        if (existingConnections >= _maxConnectionsPerUser)
        {
            _logger.LogWarning("Maximum connections per user exceeded: userId={UserId} existing={Count} max={Max}",
                userId ?? "<anonymous>", existingConnections, _maxConnectionsPerUser);
            await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation,
                "Maximum connections per user exceeded", CancellationToken.None);
            return;
        }

        var connectionId = Guid.NewGuid().ToString("N");

        _presence.TrackConnection(new GatewayConnectionInfo
        {
            ConnectionId = connectionId,
            UserId = userId,
            ClientType = "websocket",
            ConnectedAt = DateTimeOffset.UtcNow
        });

        _logger.LogInformation("WebSocket connected: connectionId={ConnectionId} userId={UserId}", connectionId, userId);

        // Start heartbeat timer
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeatTask = RunHeartbeatAsync(ws, _heartbeatIntervalSeconds, heartbeatCts.Token);

        try
        {
            var buffer = new byte[4096];
            var messageBuffer = new StringBuilder();

            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                if (!result.EndOfMessage)
                {
                    continue;
                }

                var text = messageBuffer.ToString();
                messageBuffer.Clear();

                GatewayMessage? msg;
                try
                {
                    msg = JsonSerializer.Deserialize<GatewayMessage>(text);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse WebSocket message: {Text}", text.Truncate(200));
                    continue;
                }

                if (msg == null) continue;

                await HandleMessageAsync(ws, msg, userId, ct);
            }
        }
        catch (OperationCanceledException) { /* expected on shutdown */ }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex, "WebSocket error for connectionId={ConnectionId}", connectionId);
        }
        finally
        {
            await heartbeatCts.CancelAsync();
            try { await heartbeatTask; } catch (OperationCanceledException) { /* expected */ }

            _presence.RemoveConnection(connectionId);
            _logger.LogInformation("WebSocket disconnected: connectionId={ConnectionId}", connectionId);

            if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
                }
                catch { /* best effort */ }
            }

            _sendLock.Dispose();
        }
    }

    /// <summary>发送周期性心跳消息，检测并清理僵尸连接（与流式发送共用 _sendLock 串行化）</summary>
    private async Task RunHeartbeatAsync(WebSocket ws, int intervalSeconds, CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(intervalSeconds);
        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            try
            {
                await Task.Delay(interval, ct);
                if (ws.State == WebSocketState.Open)
                {
                    var heartbeat = new GatewayMessage { Type = "event", Method = "heartbeat" };
                    await SendAsync(ws, heartbeat, ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (WebSocketException) { break; }
        }
    }

    private async Task HandleMessageAsync(WebSocket ws, GatewayMessage msg, string? userId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(msg.Method) || !IsValidMethod(msg.Method))
        {
            var errorResponse = BuildResponseMessage(msg.Id, msg.Method, error: $"Unknown method: {msg.Method}");
            await SendAsync(ws, errorResponse, ct);
            return;
        }

        switch (msg.Method.ToLowerInvariant())
        {
            case "chat.send":
                await HandleChatSendAsync(ws, msg, userId, ct);
                break;

            case "session.list":
                await HandleSessionListAsync(ws, msg, ct);
                break;

            case "session.get":
                await HandleSessionGetAsync(ws, msg, ct);
                break;

            case "session.prune":
                await HandleSessionPruneAsync(ws, msg, ct);
                break;

            case "device.register":
            case "device.invoke":
            case "device.result":
                // 预留方法 — DeviceModule 加载后通过 DI 处理
                var notImpl = BuildResponseMessage(msg.Id, msg.Method, error: $"Method '{msg.Method}' is not yet implemented. Load DeviceModule to enable device features.");
                await SendAsync(ws, notImpl, ct);
                break;
        }
    }

    private async Task HandleChatSendAsync(WebSocket ws, GatewayMessage msg, string? userId, CancellationToken ct)
    {
        var payload = msg.Payload;
        var text = payload?.TryGetProperty("text", out var textElem) == true ? textElem.GetString() : null;
        var channel = payload?.TryGetProperty("channel", out var chanElem) == true ? chanElem.GetString() : "web";
        var chatId = ResolveChatId(payload, userId);

        if (string.IsNullOrEmpty(text))
        {
            var errorResponse = BuildResponseMessage(msg.Id, "chat.send", error: "Missing 'text' in payload.");
            await SendAsync(ws, errorResponse, ct);
            return;
        }

        var request = new GatewayRequest
        {
            Channel = channel ?? "web",
            ChatId = chatId,
            UserId = userId ?? "anonymous",
            UserMessage = text
        };

        await foreach (var chunk in _gateway.ProcessStreamingAsync(request, ct))
        {
            var streamEvent = new GatewayMessage
            {
                Type = "event",
                Id = msg.Id,
                Method = "chat.stream",
                Payload = JsonSerializer.SerializeToElement(chunk)
            };
            await SendAsync(ws, streamEvent, ct);
        }
    }

    private async Task HandleSessionListAsync(WebSocket ws, GatewayMessage msg, CancellationToken ct)
    {
        var agentId = msg.Payload?.TryGetProperty("agentId", out var agentElem) == true
            ? agentElem.GetString()
            : null;

        var sessions = await _gateway.GetSessionsAsync(agentId);
        var response = BuildResponseMessage(msg.Id, "session.list", JsonSerializer.SerializeToElement(sessions));
        await SendAsync(ws, response, ct);
    }

    private async Task HandleSessionGetAsync(WebSocket ws, GatewayMessage msg, CancellationToken ct)
    {
        var sessionKey = msg.Payload?.TryGetProperty("sessionKey", out var keyElem) == true
            ? keyElem.GetString()
            : null;

        if (string.IsNullOrEmpty(sessionKey))
        {
            var errorResponse = BuildResponseMessage(msg.Id, "session.get", error: "Missing 'sessionKey' in payload.");
            await SendAsync(ws, errorResponse, ct);
            return;
        }

        var session = await _gateway.GetSessionAsync(sessionKey);
        var response = BuildResponseMessage(msg.Id, "session.get",
            session != null ? JsonSerializer.SerializeToElement(session) : null);
        await SendAsync(ws, response, ct);
    }

    private async Task HandleSessionPruneAsync(WebSocket ws, GatewayMessage msg, CancellationToken ct)
    {
        var sessionKey = msg.Payload?.TryGetProperty("sessionKey", out var keyElem) == true
            ? keyElem.GetString()
            : null;

        if (string.IsNullOrEmpty(sessionKey))
        {
            var errorResponse = BuildResponseMessage(msg.Id, "session.prune", error: "Missing 'sessionKey' in payload.");
            await SendAsync(ws, errorResponse, ct);
            return;
        }

        await _gateway.PruneSessionAsync(sessionKey);
        var response = BuildResponseMessage(msg.Id, "session.prune", JsonSerializer.SerializeToElement(new { pruned = true }));
        await SendAsync(ws, response, ct);
    }

    /// <summary>构建响应消息</summary>
    public static GatewayMessage BuildResponseMessage(string? requestId, string? method, JsonElement? payload = null, string? error = null)
    {
        return new GatewayMessage
        {
            Type = "response",
            Id = requestId,
            Method = method,
            Payload = error == null ? payload : null,
            Error = error
        };
    }

    /// <summary>检查方法名是否有效</summary>
    public static bool IsValidMethod(string method)
    {
        return ValidMethods.Contains(method);
    }

    /// <summary>
    /// 解析 chat.send 的 chatId。已认证用户强制使用其身份作为 peer/session 标识，
    /// 客户端提供的 chatId 仅作话题区分（绝不能用于冒充其他 peer）。匿名连接可自行选择 chatId。
    /// </summary>
    public static string ResolveChatId(JsonElement? payload, string? userId)
    {
        // 已认证：FORCE chatId = userId，忽略客户端提供的 chatId 作为 peer 身份
        if (!string.IsNullOrEmpty(userId))
        {
            return userId;
        }

        // 匿名：使用客户端提供的 chatId，缺省回退到 "unknown"
        var clientChatId = payload?.TryGetProperty("chatId", out var chatIdElem) == true
            ? chatIdElem.GetString()
            : null;
        return string.IsNullOrEmpty(clientChatId) ? "unknown" : clientChatId;
    }

    /// <summary>
    /// 通过 per-connection 信号量串行化的唯一发送入口。所有发送路径（心跳 + 流式 + 响应）
    /// 都经此方法，确保对同一 WebSocket 不会有重叠 SendAsync 调用。
    /// </summary>
    private async Task SendAsync(WebSocket ws, GatewayMessage msg, CancellationToken ct)
    {
        if (ws.State != WebSocketState.Open) return;

        var json = JsonSerializer.Serialize(msg);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _sendLock.WaitAsync(ct);
        try
        {
            // 取锁后再次检查状态，避免在关闭竞争窗口内发送
            if (ws.State != WebSocketState.Open) return;
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }
}
