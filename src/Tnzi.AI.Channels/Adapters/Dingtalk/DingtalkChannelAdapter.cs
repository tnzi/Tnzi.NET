
namespace Tnzi.AI.Channels.Adapters.Dingtalk;

/// <summary>
/// 钉钉频道适配器 - 通过 HTTP REST API 收发消息，使用 Markdown 格式。
/// </summary>
/// <remarks>
/// 使用纯 HTTP API 调用（无第三方 SDK 依赖）：
/// - POST /v1.0/robot/oToMessages/batchSend: 单聊回复（收件人是发送者的 staffId）
/// - POST /v1.0/robot/groupMessages/send: 群聊回复（收件人是会话的 openConversationId）
/// - 选哪条、发给谁由入站回调带来的元数据决定（见 <see cref="DingtalkConversationMetadata"/>）
/// - 事件接收通过 Webhook 回调由 Controller 调用 HandleEventAsync
/// - 认证: appkey+appsecret 获取 access_token
/// </remarks>
public class DingtalkChannelAdapter : IChannelAdapter, IInboundWebhookAdapter
{
    private const string NewApiBaseUrl = "https://api.dingtalk.com";

    private readonly ILogger<DingtalkChannelAdapter> _logger;
    private readonly IChannelMessageBus _bus;
    private readonly DingtalkAdapterOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HashSet<string> _allowedUsers;
    private readonly HashSet<string> _allowedOrganizations;
    private readonly TokenRefresher _tokenRefresher;

    public string Name => "dingtalk";
    public bool SupportsStreaming => false;

    /// <summary>此渠道 Bot 实例归属的租户（来自 adapter options；null = 单租户/全局）</summary>
    public Guid? TenantId => _options.TenantId;

    public DingtalkChannelAdapter(
        ILogger<DingtalkChannelAdapter> logger,
        IChannelMessageBus bus,
        IHttpClientFactory httpClientFactory,
        IOptions<ChannelsModuleOptions> options)
    {
        _logger = Check.NotNull(logger);
        _bus = Check.NotNull(bus);
        _httpClientFactory = Check.NotNull(httpClientFactory);
        _options = Check.NotNull(options).Value.Dingtalk;

        if (string.IsNullOrWhiteSpace(_options.AppKey))
            throw new ArgumentException("DingTalk AppKey is required when adapter is enabled");
        if (string.IsNullOrWhiteSpace(_options.AppSecret))
            throw new ArgumentException("DingTalk AppSecret is required when adapter is enabled");
        if (string.IsNullOrWhiteSpace(_options.RobotCode))
            throw new ArgumentException("DingTalk RobotCode is required when adapter is enabled");

        _allowedUsers = _options.AllowedUsers is { Count: > 0 } ? [.. _options.AllowedUsers] : [];
        _allowedOrganizations = _options.AllowedOrganizations is { Count: > 0 } ? [.. _options.AllowedOrganizations] : [];
        _tokenRefresher = new TokenRefresher(RefreshAccessTokenAsync, _logger, Name);
    }

    /// <summary>检查用户是否被允许（空白名单=不限制）</summary>
    public bool IsUserAllowed(string userId)
    {
        return _allowedUsers.Count == 0 || _allowedUsers.Contains(userId);
    }

    /// <summary>检查组织是否被允许（空白名单=不限制）</summary>
    /// <remarks>
    /// <paramref name="orgId"/> 为 null 表示回调没带 <c>senderCorpId</c>。配了白名单时，说不出自己属于哪个组织的消息
    /// 不属于任何允许的组织，必须拒绝，而不是当作不受组织名单约束。
    /// </remarks>
    public bool IsOrganizationAllowed(string? orgId)
    {
        return _allowedOrganizations.Count == 0 || (orgId != null && _allowedOrganizations.Contains(orgId));
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("DingTalk channel adapter started (webhook mode)");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("DingTalk channel adapter stopped");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 处理钉钉 Webhook 回调事件（由 ASP.NET Controller 调用），
    /// 带 HTTP 请求头用于签名验证。
    /// </summary>
    /// <param name="eventJson">请求 body 原文</param>
    /// <param name="headers">HTTP 请求头（需包含 timestamp 和 sign）</param>
    /// <param name="ct">取消令牌</param>
    public Task HandleEventAsync(string eventJson, IDictionary<string, string>? headers, CancellationToken ct = default)
    {
        if (_options.VerifyWebhookSignature && headers != null)
        {
            if (!ValidateDingtalkSignature(headers))
            {
                _logger.LogWarning("DingTalk webhook signature validation failed, rejecting event");
                return Task.CompletedTask;
            }
        }
        else if (_options.VerifyWebhookSignature && headers == null)
        {
            // Fail closed: signature verification is required but no headers were supplied,
            // so the request cannot be authenticated (parity with Slack/Discord/Feishu).
            _logger.LogWarning("DingTalk VerifyWebhookSignature is enabled but no headers provided for verification, rejecting event");
            return Task.CompletedTask;
        }

        return HandleEventCoreAsync(eventJson, ct);
    }

    /// <summary>
    /// Handle DingTalk webhook callback (no-headers compatibility overload).
    /// Rejects the event when <see cref="DingtalkAdapterOptions.VerifyWebhookSignature"/>
    /// is enabled (the secure default) since signature verification cannot be performed
    /// without the timestamp/sign headers. For trusted private networks where a
    /// fronting gateway has already validated the request, set VerifyWebhookSignature=false.
    /// </summary>
    public Task HandleEventAsync(string eventJson, CancellationToken ct = default)
    {
        if (_options.VerifyWebhookSignature)
        {
            _logger.LogWarning("DingTalk VerifyWebhookSignature is enabled but HandleEventAsync called without headers, rejecting event");
            return Task.CompletedTask;
        }

        return HandleEventCoreAsync(eventJson, ct);
    }

    /// <summary>
    /// 验证钉钉 Webhook 签名（HmacSHA256）。
    /// </summary>
    /// <remarks>
    /// 钉钉签名验证流程:
    /// 1. 获取请求头中的 timestamp（毫秒级 Unix 时间戳）
    /// 2. 检查时间差不超过 1 小时（钉钉官方要求）
    /// 3. 构造待签名字符串 = timestamp + "\n" + AppSecret
    /// 4. 用 AppSecret 计算 HmacSHA256，Base64 编码后与 sign 比较
    /// </remarks>
    internal bool ValidateDingtalkSignature(IDictionary<string, string> headers)
    {
        if (!headers.TryGetValue("timestamp", out var timestampStr) || string.IsNullOrWhiteSpace(timestampStr))
        {
            _logger.LogDebug("Missing timestamp header in DingTalk webhook");
            return false;
        }

        if (!headers.TryGetValue("sign", out var sign) || string.IsNullOrWhiteSpace(sign))
        {
            _logger.LogDebug("Missing sign header in DingTalk webhook");
            return false;
        }

        if (!long.TryParse(timestampStr, out var timestamp))
        {
            _logger.LogDebug("Invalid timestamp value in DingTalk webhook");
            return false;
        }

        // 防重放攻击：时间戳不超过 1 小时（钉钉官方要求）
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (Math.Abs(nowMs - timestamp) > 3600_000)
        {
            _logger.LogDebug("DingTalk request timestamp is too old ({Diff}ms)", Math.Abs(nowMs - timestamp));
            return false;
        }

        // 计算签名: HmacSHA256(timestamp + "\n" + appSecret, appSecret) → Base64
        var stringToSign = $"{timestampStr}\n{_options.AppSecret}";
        var keyBytes = Encoding.UTF8.GetBytes(_options.AppSecret!);
        var dataBytes = Encoding.UTF8.GetBytes(stringToSign);

        using var hmac = new HMACSHA256(keyBytes);
        var hashBytes = hmac.ComputeHash(dataBytes);
        var computedSign = Convert.ToBase64String(hashBytes);

        // 使用固定时间比较防止时序攻击
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedSign),
            Encoding.UTF8.GetBytes(sign));
    }

    /// <inheritdoc />
    public string Platform => Name;

    /// <inheritdoc />
    public async Task<WebhookProcessResult> ProcessWebhookAsync(
        string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
    {
        // 钉钉无 URL 验证握手 - 直接验签。配置了 VerifyWebhookSignature（默认 true）时，
        // 缺少 timestamp/sign 头或签名不匹配一律拒绝。
        if (_options.VerifyWebhookSignature)
        {
            if (!ValidateDingtalkSignature(new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)))
            {
                return WebhookProcessResult.Rejected("Invalid DingTalk signature");
            }
        }

        await HandleEventCoreAsync(rawBody, ct);
        return WebhookProcessResult.Accepted();
    }

    /// <remarks>
    /// 钉钉机器人回调格式:
    /// {
    ///   "conversationId": "xxx",
    ///   "chatbotCorpId": "xxx",
    ///   "chatbotUserId": "xxx",
    ///   "msgId": "xxx",
    ///   "senderNick": "xxx",
    ///   "isAdmin": false,
    ///   "senderStaffId": "xxx",
    ///   "sessionWebhookExpiredTime": 1613635652738,
    ///   "createAt": 1613635652738,
    ///   "senderCorpId": "xxx",
    ///   "conversationType": "1",  // 1=单聊, 2=群聊
    ///   "msgtype": "text",
    ///   "text": { "content": "xxx" },
    ///   "sessionWebhook": "https://oapi.dingtalk.com/robot/sendBySession?session=xxx"
    /// }
    /// </remarks>
    private async Task HandleEventCoreAsync(string eventJson, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(eventJson);
            var root = doc.RootElement;

            // 获取会话 ID（作为 ChatId）
            var conversationId = root.TryGetProperty("conversationId", out var convEl)
                ? convEl.GetString() ?? ""
                : "";

            // 发送者 ID
            var senderId = root.TryGetProperty("senderStaffId", out var senderEl)
                ? senderEl.GetString() ?? ""
                : "";

            // 组织 ID
            var corpId = root.TryGetProperty("senderCorpId", out var corpEl)
                ? corpEl.GetString()
                : null;

            // 组织白名单
            if (!IsOrganizationAllowed(corpId))
            {
                _logger.LogDebug("DingTalk message from non-allowed organization {CorpId}, ignoring", corpId ?? "(none)");
                return;
            }

            // 用户白名单
            if (!IsUserAllowed(senderId))
            {
                _logger.LogDebug("DingTalk message from non-allowed user {UserId}, ignoring", senderId);
                return;
            }

            // 消息类型（仅处理 text）
            var msgType = root.TryGetProperty("msgtype", out var typeEl)
                ? typeEl.GetString()
                : null;
            if (msgType != "text") return;

            // 提取文本内容
            var text = "";
            if (root.TryGetProperty("text", out var textObj) &&
                textObj.TryGetProperty("content", out var contentEl))
            {
                text = contentEl.GetString()?.Trim() ?? "";
            }

            if (string.IsNullOrWhiteSpace(text)) return;

            var conversationType = root.TryGetProperty("conversationType", out var convTypeEl)
                ? convTypeEl.GetString()
                : null;

            // 回复要知道走单聊还是群聊接口、单聊发给哪个 staffId：ChatId（conversationId）只是线程映射的键
            var metadata = new Dictionary<string, object>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(conversationType))
                metadata[DingtalkConversationMetadata.ConversationType] = conversationType;
            if (!string.IsNullOrWhiteSpace(senderId))
                metadata[DingtalkConversationMetadata.SenderStaffId] = senderId;

            var isCommand = text.StartsWith('/');
            var inbound = new InboundMessage(
                ChannelName: Name,
                ChatId: conversationId,
                UserId: senderId,
                Text: text,
                Type: isCommand ? InboundMessageType.Command : InboundMessageType.Chat,
                Metadata: metadata,
                PeerKind: conversationType switch
                {
                    DingtalkConversationMetadata.SingleChat => ChannelPeerKinds.User,
                    DingtalkConversationMetadata.GroupChat => ChannelPeerKinds.Group,
                    _ => null
                });

            await _bus.PublishInboundAsync(inbound, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process DingTalk event");
        }
    }

    public Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        Check.NotNull(message);

        // 先定收件人再分块：定不下来是确定性的失败，不该被当成可重试的发送失败退避重试几轮
        var target = ResolveReplyTarget(message);

        return ChannelSendHelper.SendChunkedWithRetryAsync(
            message.Text,
            _options.MaxMessageLength,
            _options.MaxRetries,
            (chunk, token) => PostMessageAsync(target, chunk, token),
            _logger,
            Name,
            ct);
    }

    /// <summary>
    /// 按入站回调带来的会话类型选接口与收件人。缺元数据时失败关闭：
    /// 拿 conversationId 当员工 id 发，钉钉回 200 却什么都不投递，日志里看起来一切正常。
    /// </summary>
    private static DingtalkReplyTarget ResolveReplyTarget(OutboundMessage message)
    {
        var conversationType = ReadMetadata(message, DingtalkConversationMetadata.ConversationType);

        if (conversationType == DingtalkConversationMetadata.GroupChat)
        {
            if (string.IsNullOrWhiteSpace(message.ChatId))
                throw new InvalidOperationException("DingTalk group reply has no conversation id to send to.");

            return new DingtalkReplyTarget(IsGroup: true, message.ChatId);
        }

        if (conversationType == DingtalkConversationMetadata.SingleChat)
        {
            var staffId = ReadMetadata(message, DingtalkConversationMetadata.SenderStaffId);
            if (string.IsNullOrWhiteSpace(staffId))
                throw new InvalidOperationException("DingTalk 1:1 reply has no sender staff id to send to.");

            return new DingtalkReplyTarget(IsGroup: false, staffId);
        }

        throw new InvalidOperationException(
            "DingTalk reply target is unknown: the outbound message does not carry the conversation type of the inbound callback.");
    }

    private static string? ReadMetadata(OutboundMessage message, string key)
        => message.Metadata != null && message.Metadata.TryGetValue(key, out var value) ? value?.ToString() : null;

    /// <param name="IsGroup">群聊走 groupMessages/send，单聊走 oToMessages/batchSend</param>
    /// <param name="Recipient">群聊是 openConversationId，单聊是员工 staffId</param>
    private sealed record DingtalkReplyTarget(bool IsGroup, string Recipient);

    public ValueTask DisposeAsync()
        => _tokenRefresher.DisposeAsync();

    /// <summary>
    /// 通过钉钉机器人 API 发送 Markdown 消息：群聊发到会话，单聊发给发送者。
    /// </summary>
    private async Task PostMessageAsync(DingtalkReplyTarget target, string content, CancellationToken ct)
    {
        var token = await _tokenRefresher.GetTokenAsync(ct);
        var client = _httpClientFactory.CreateClient("Tnzi.AI.Dingtalk");

        const string msgKey = "sampleMarkdown";
        var msgParam = JsonSerializer.Serialize(new
        {
            title = "Reply",
            text = content
        });

        object payload = target.IsGroup
            ? new { robotCode = _options.RobotCode, openConversationId = target.Recipient, msgKey, msgParam }
            : new { robotCode = _options.RobotCode, userIds = new[] { target.Recipient }, msgKey, msgParam };
        var path = target.IsGroup ? "/v1.0/robot/groupMessages/send" : "/v1.0/robot/oToMessages/batchSend";

        // Set the access-token header per-request - the named HttpClient is pooled and its
        // DefaultRequestHeaders must not be mutated across concurrent sends (.Add throws on a
        // duplicate header name and the token changes on refresh).
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{NewApiBaseUrl}{path}")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("x-acs-dingtalk-access-token", token);

        var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("DingTalk send failed: {StatusCode} {Body}", response.StatusCode, responseBody);
            throw new HttpRequestException($"DingTalk API returned {response.StatusCode}");
        }

        if (!target.IsGroup)
        {
            EnsureRecipientAccepted(responseBody);
        }
    }

    /// <summary>
    /// 单聊批量发送对收件人问题回 200：无效的 staffId 列进 <c>invalidStaffIdList</c>，被限流的列进
    /// <c>flowControlledStaffIdList</c>。只看状态码会把「一条都没投递」记成成功。
    /// </summary>
    private void EnsureRecipientAccepted(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(responseBody);
        }
        catch (JsonException ex)
        {
            // 状态码已是成功；读不懂响应体不改判，留一条日志
            _logger.LogDebug(ex, "DingTalk send response is not JSON: {Body}", responseBody);
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (HasEntries(root, "invalidStaffIdList"))
            {
                _logger.LogWarning("DingTalk rejected the reply recipient as an invalid staff id: {Body}", responseBody);
                throw new HttpRequestException("DingTalk rejected the reply recipient as an invalid staff id.");
            }

            if (HasEntries(root, "flowControlledStaffIdList"))
            {
                throw new HttpRequestException("DingTalk throttled the reply to this recipient.");
            }
        }
    }

    private static bool HasEntries(JsonElement root, string property)
        => root.TryGetProperty(property, out var list)
           && list.ValueKind == JsonValueKind.Array
           && list.GetArrayLength() > 0;

    /// <summary>
    /// 调用钉钉 v2.0 API 刷新 access_token。
    /// </summary>
    private async Task<(string Token, int ExpiresInSeconds)> RefreshAccessTokenAsync(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("Tnzi.AI.Dingtalk");
        // 使用 v2.0 API，凭据通过 POST body 传递，避免 appSecret 泄漏在 URL query string / HTTP 日志中
        var response = await client.PostAsJsonAsync(
            $"{NewApiBaseUrl}/v2.0/oauth2/accessToken",
            new { appKey = _options.AppKey, appSecret = _options.AppSecret }, ct);

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // v2.0 API 返回 accessToken / expireIn（无 errcode）
        if (!root.TryGetProperty("accessToken", out var tokenEl) || string.IsNullOrWhiteSpace(tokenEl.GetString()))
        {
            throw new HttpRequestException($"DingTalk v2.0 accessToken response missing accessToken field: {json}");
        }

        var token = tokenEl.GetString()!;
        var expireIn = root.TryGetProperty("expireIn", out var expEl) ? expEl.GetInt32() : 7200;
        return (token, expireIn);
    }
}
