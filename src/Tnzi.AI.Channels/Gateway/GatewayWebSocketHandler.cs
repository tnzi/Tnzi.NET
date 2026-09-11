using System.Net.WebSockets;

namespace Tnzi.AI.Channels.Gateway;

/// <summary>
/// WebSocket 连接处理器 - 接收 GatewayMessage，按 Method 分发到 IGateway
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
        "heartbeat"
    };

    private readonly IGateway _gateway;
    private readonly IPresenceTracker _presence;
    private readonly ILogger<GatewayWebSocketHandler> _logger;
    private readonly int _maxConnectionsPerUser;
    private readonly int _heartbeatIntervalSeconds;
    private readonly bool _requireAuthentication;
    private readonly int _maxInboundMessageBytes;

    /// <summary>
    /// 匿名连接的 peer 命名空间前缀。已认证 peer 的标识是用户 id（Guid 字符串），
    /// 永远不会以此开头，因此两个命名空间不可能相交。
    /// </summary>
    public const string AnonymousPeerPrefix = "anon:";

    /// <summary>
    /// Serializes all sends on this connection - WebSocket forbids overlapping SendAsync
    /// calls (heartbeat vs stream), which otherwise corrupt frames / throw InvalidOperationException.
    /// </summary>
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public GatewayWebSocketHandler(IGateway gateway, IPresenceTracker presence, ILogger<GatewayWebSocketHandler> logger, int maxConnectionsPerUser = 5, int heartbeatIntervalSeconds = 30, bool requireAuthentication = false, int maxInboundMessageBytes = 256 * 1024)
    {
        _gateway = Check.NotNull(gateway);
        _presence = Check.NotNull(presence);
        _logger = Check.NotNull(logger);
        _maxConnectionsPerUser = maxConnectionsPerUser;
        _heartbeatIntervalSeconds = heartbeatIntervalSeconds;
        _requireAuthentication = requireAuthentication;
        _maxInboundMessageBytes = maxInboundMessageBytes;
    }

    /// <summary>
    /// 处理 WebSocket 连接的完整生命周期。
    /// <paramref name="tenantId"/> 来自已认证连接的租户上下文（连接建立时由中间件解析），
    /// 随每条 chat.send 填入 <see cref="GatewayRequest.TenantId"/>；null = 单租户/匿名连接。
    /// </summary>
    public async Task HandleAsync(WebSocket ws, string? userId, CancellationToken ct, Guid? tenantId = null)
    {
        // 要求认证时，拒绝匿名连接
        if (_requireAuthentication && string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("Rejecting anonymous WebSocket connection: AI:Channels:Gateway:RequireAuthentication is enabled");
            await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation,
                "Authentication required", CancellationToken.None);
            return;
        }

        // 检查每用户最大连接数 - 匿名连接共享 UserId==null 桶，同样受上限约束
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
            // 按字节计而不是按 StringBuilder.Length：后者是字符数，UTF-8 下永远小于等于
            // 收到的字节数，拿它去比一个以字节命名的上限会让实际允许的量高于配置值。
            var bufferedBytes = 0;

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

                // 分片帧的重组缓冲必须有上限：一个客户端可以无限发 EndOfMessage=false 的续帧，
                // 服务端会一直 Append 下去直到进程内存耗尽 —— 一条连接就够。
                if (bufferedBytes + result.Count > _maxInboundMessageBytes)
                {
                    _logger.LogWarning(
                        "WebSocket inbound message exceeded {MaxBytes} bytes on connectionId={ConnectionId}; closing connection",
                        _maxInboundMessageBytes, connectionId);
                    await ws.CloseAsync(WebSocketCloseStatus.MessageTooBig,
                        "Message too large", CancellationToken.None);
                    break;
                }

                messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                bufferedBytes += result.Count;

                if (!result.EndOfMessage)
                {
                    continue;
                }

                var text = messageBuffer.ToString();
                messageBuffer.Clear();
                bufferedBytes = 0;

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

                await HandleMessageAsync(ws, msg, userId, connectionId, tenantId, ct);
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

    private async Task HandleMessageAsync(WebSocket ws, GatewayMessage msg, string? userId, string connectionId, Guid? tenantId, CancellationToken ct)
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
                await HandleChatSendAsync(ws, msg, userId, connectionId, tenantId, ct);
                break;

            case "session.list":
                await HandleSessionListAsync(ws, msg, userId, connectionId, ct);
                break;

            case "session.get":
                await HandleSessionGetAsync(ws, msg, userId, connectionId, ct);
                break;

            case "session.prune":
                await HandleSessionPruneAsync(ws, msg, userId, connectionId, ct);
                break;

        }
    }

    private async Task HandleChatSendAsync(WebSocket ws, GatewayMessage msg, string? userId, string connectionId, Guid? tenantId, CancellationToken ct)
    {
        var payload = msg.Payload;
        var text = payload?.TryGetProperty("text", out var textElem) == true ? textElem.GetString() : null;
        var channel = payload?.TryGetProperty("channel", out var chanElem) == true ? chanElem.GetString() : "web";
        var chatId = ResolveChatId(payload, userId, connectionId);

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
            UserMessage = text,
            // 来自已认证连接的租户上下文（服务端解析），绝不信任客户端 payload
            TenantId = tenantId
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

    /// <summary>
    /// 三个 session.* 方法一律只作用于<b>调用者自己的</b>会话。
    /// <para>
    /// ★ 此前 <c>session.list</c> 直调 <c>GetSessionsAsync()</c>，返回<b>全部</b>活跃会话，
    /// 每条都带 <c>PeerId</c>（已认证渠道下就是用户 id）—— 一个匿名 WebSocket 客户端由此
    /// 枚举出全部在线用户，再拿 channel + peerId 去接管他们的线程。<c>session.prune</c>
    /// 则能 TryRemove 任意键。
    /// </para>
    /// </summary>
    private async Task HandleSessionListAsync(WebSocket ws, GatewayMessage msg, string? userId, string connectionId, CancellationToken ct)
    {
        var agentId = msg.Payload?.TryGetProperty("agentId", out var agentElem) == true
            ? agentElem.GetString()
            : null;

        var sessions = await _gateway.GetPeerSessionsAsync(ResolvePeerId(userId, connectionId), agentId);
        var response = BuildResponseMessage(msg.Id, "session.list", JsonSerializer.SerializeToElement(sessions));
        await SendAsync(ws, response, ct);
    }

    /// <inheritdoc cref="HandleSessionListAsync"/>
    private async Task HandleSessionGetAsync(WebSocket ws, GatewayMessage msg, string? userId, string connectionId, CancellationToken ct)
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

        var session = await _gateway.GetPeerSessionAsync(ResolvePeerId(userId, connectionId), sessionKey);
        var response = BuildResponseMessage(msg.Id, "session.get",
            session != null ? JsonSerializer.SerializeToElement(session) : null);
        await SendAsync(ws, response, ct);
    }

    /// <inheritdoc cref="HandleSessionListAsync"/>
    private async Task HandleSessionPruneAsync(WebSocket ws, GatewayMessage msg, string? userId, string connectionId, CancellationToken ct)
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

        // 不属于调用者的键与根本不存在的键给同一个答案（pruned=false）。
        var pruned = await _gateway.PrunePeerSessionAsync(ResolvePeerId(userId, connectionId), sessionKey);
        var response = BuildResponseMessage(msg.Id, "session.prune", JsonSerializer.SerializeToElement(new { pruned }));
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
    /// 客户端提供的 chatId 仅作话题区分（绝不能用于冒充其他 peer）。
    /// <para>
    /// ★ 匿名连接的 chatId <b>一律落在匿名命名空间</b>（<see cref="AnonymousPeerPrefix"/> 前缀）。
    /// 此前匿名分支原样采用客户端提供的 chatId，而 <c>DefaultGateway.ResolveThreadIdAsync</c>
    /// 在请求与绑定都没带 ThreadId 时会回退到 <c>threadStore.GetThreadIdAsync(channel, chatId, topicId)</c>
    /// —— 于是一个匿名客户端只要把受害者的 userId 填进 chatId，就直接接上了对方的线程：
    /// 历史随之流回攻击者，之后的发言也记进对方的线程。这个方法的注释当时已经写着
    /// 「客户端提供的 chatId 绝不能用于冒充其他 peer」，但只堵了已认证那一半。
    /// </para>
    /// <para>
    /// 匿名客户端仍可自选 chatId 以在重连后接回自己的话题，但那只在匿名命名空间内成立
    /// （彼此之间的隔离取决于客户端选了多难猜的值 —— 这是"不认证"的固有代价，
    /// 生产部署应当开 <c>AI:Channels:Gateway:RequireAuthentication</c>）。
    /// 不提供 chatId 时回退到连接 id，天然唯一。
    /// </para>
    /// </summary>
    public static string ResolveChatId(JsonElement? payload, string? userId, string connectionId)
    {
        // 已认证：FORCE chatId = userId，忽略客户端提供的 chatId 作为 peer 身份
        if (!string.IsNullOrEmpty(userId))
        {
            return userId;
        }

        // 匿名：客户端可自选话题标识，但整体收进匿名命名空间，绝不可能与用户 id 相交
        var clientChatId = payload?.TryGetProperty("chatId", out var chatIdElem) == true
            ? chatIdElem.GetString()
            : null;

        return AnonymousPeerPrefix + (string.IsNullOrEmpty(clientChatId) ? connectionId : clientChatId);
    }

    /// <summary>
    /// 解析调用者的 peer 标识：已认证 = 用户 id；匿名 = 匿名命名空间下的连接 id。
    /// <para>
    /// session.* 用它做归属过滤。匿名连接刻意用<b>连接 id</b> 而不是客户端自选的 chatId：
    /// 会话列表泄漏的正是 peer 身份本身，而客户端自选值可以被另一个匿名客户端猜中。
    /// </para>
    /// </summary>
    public static string ResolvePeerId(string? userId, string connectionId)
        => string.IsNullOrEmpty(userId) ? AnonymousPeerPrefix + connectionId : userId;

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
