
namespace Tnzi.AI.Channels.Adapters.Feishu;

/// <summary>
/// 飞书频道适配器 - 通过 HTTP REST API 收发消息，支持流式卡片更新。
/// </summary>
/// <remarks>
/// 消息流程（对标 DeerFlow FeishuChannel）：
/// 1. 用户发消息 → Webhook 接收
/// 2. Bot 回复: "Processing..."
/// 3. Agent 处理 → 返回结果
/// 4. 飞书 .NET 生态较弱，直接使用 HTTP API 比依赖第三方 SDK 更可靠。
/// </remarks>
public class FeishuChannelAdapter : IChannelAdapter, IInboundWebhookAdapter
{
    private const string BaseUrl = "https://open.feishu.cn/open-apis";
    private const string HeaderTimestamp = "X-Lark-Request-Timestamp";
    private const string HeaderNonce = "X-Lark-Request-Nonce";
    private const string HeaderSignature = "X-Lark-Signature";
    private const int SignatureWindowSeconds = 300;

    private readonly ILogger<FeishuChannelAdapter> _logger;
    private readonly IChannelMessageBus _bus;
    private readonly FeishuAdapterOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HashSet<string> _allowedUsers;
    private readonly TokenRefresher _tokenRefresher;

    public string Name => "feishu";
    public bool SupportsStreaming => true;

    /// <summary>此渠道 Bot 实例归属的租户（来自 adapter options；null = 单租户/全局）</summary>
    public Guid? TenantId => _options.TenantId;

    public FeishuChannelAdapter(
        ILogger<FeishuChannelAdapter> logger,
        IChannelMessageBus bus,
        IHttpClientFactory httpClientFactory,
        IOptions<ChannelsModuleOptions> options)
    {
        _logger = Check.NotNull(logger);
        _bus = Check.NotNull(bus);
        _httpClientFactory = Check.NotNull(httpClientFactory);
        _options = Check.NotNull(options).Value.Feishu;

        if (string.IsNullOrWhiteSpace(_options.AppId))
            throw new ArgumentException("Feishu AppId is required when adapter is enabled");
        if (string.IsNullOrWhiteSpace(_options.AppSecret))
            throw new ArgumentException("Feishu AppSecret is required when adapter is enabled");

        _allowedUsers = [.. _options.AllowedUserIds];
        _tokenRefresher = new TokenRefresher(RefreshTenantAccessTokenAsync, _logger, Name);
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Feishu channel adapter started (webhook mode)");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Feishu channel adapter stopped");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle Feishu webhook event with request headers for signature verification.
    /// Invoked by ASP.NET controller. Rejects the event if EncryptKey is configured
    /// but signature verification fails or headers are missing.
    /// </summary>
    /// <param name="eventJson">Raw request body</param>
    /// <param name="headers">HTTP request headers (X-Lark-Request-Timestamp / X-Lark-Request-Nonce / X-Lark-Signature)</param>
    /// <param name="ct">Cancellation token</param>
    public Task HandleEventAsync(string eventJson, IDictionary<string, string>? headers, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(_options.EncryptKey))
        {
            if (headers is null || !ValidateFeishuSignature(eventJson, headers))
            {
                _logger.LogWarning("Feishu webhook signature verification failed or headers missing, rejecting event");
                return Task.CompletedTask;
            }

            if (!TryDecryptEnvelope(eventJson, out var plaintext))
            {
                _logger.LogWarning("Feishu webhook body is not a valid encrypted envelope, rejecting event");
                return Task.CompletedTask;
            }

            return HandleEventCoreAsync(plaintext, ct);
        }

        return HandleEventCoreAsync(eventJson, ct);
    }

    /// <summary>
    /// Handle Feishu webhook event (no-headers compatibility overload).
    /// Rejects the event if EncryptKey is configured since signature verification
    /// cannot be performed without headers.
    /// </summary>
    public Task HandleEventAsync(string eventJson, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(_options.EncryptKey))
        {
            _logger.LogWarning("Feishu EncryptKey is configured but HandleEventAsync called without headers, rejecting event");
            return Task.CompletedTask;
        }

        return HandleEventCoreAsync(eventJson, ct);
    }

    /// <summary>
    /// Validate Feishu webhook signature per Lark Open Platform event v2 spec:
    /// compare SHA256(timestamp + nonce + encrypt_key + body) against the header
    /// after a 5-minute replay window check. Uses a constant-time byte comparison
    /// and tolerates any hex casing / length mismatch without throwing.
    /// </summary>
    internal bool ValidateFeishuSignature(string body, IDictionary<string, string> headers)
    {
        if (!headers.TryGetValue(HeaderTimestamp, out var timestampStr) || string.IsNullOrWhiteSpace(timestampStr))
        {
            _logger.LogDebug("Missing {Header} header", HeaderTimestamp);
            return false;
        }

        if (!headers.TryGetValue(HeaderNonce, out var nonce) || string.IsNullOrWhiteSpace(nonce))
        {
            _logger.LogDebug("Missing {Header} header", HeaderNonce);
            return false;
        }

        if (!headers.TryGetValue(HeaderSignature, out var signature) || string.IsNullOrWhiteSpace(signature))
        {
            _logger.LogDebug("Missing {Header} header", HeaderSignature);
            return false;
        }

        if (!long.TryParse(timestampStr, out var timestamp))
        {
            _logger.LogDebug("Invalid {Header} value", HeaderTimestamp);
            return false;
        }

        var drift = Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestamp);
        if (drift > SignatureWindowSeconds)
        {
            _logger.LogDebug("Feishu request timestamp is too old ({Diff}s)", drift);
            return false;
        }

        var stringToSign = timestampStr + nonce + _options.EncryptKey + body;
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(stringToSign));

        // Parse the incoming hex (case-insensitive, length-validating) and compare
        // raw 32-byte arrays. Convert.FromHexString throws FormatException on bad
        // input; swallowing keeps the adapter robust against malformed headers and
        // prevents CryptographicException from FixedTimeEquals when lengths differ.
        byte[] actualHash;
        try
        {
            actualHash = Convert.FromHexString(signature);
        }
        catch (FormatException)
        {
            _logger.LogDebug("Feishu signature header is not valid hex");
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }

    /// <inheritdoc />
    public string Platform => Name;

    /// <inheritdoc />
    public async Task<WebhookProcessResult> ProcessWebhookAsync(
        string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
    {
        // 验签：配置了 EncryptKey 时强制校验 X-Lark-Signature（SHA256(timestamp+nonce+key+body)）+ 时间窗。
        // 签名算在原始 body（密文信封）上，所以先验签、后解密。
        // ★ 在飞书那一侧，配置 Encrypt Key 就等于打开事件加密：此后每一个推送（含 url_verification）
        // 的 body 都是 {"encrypt":"..."}，X-Lark-Signature 也只在配置了该密钥时才存在。
        // 此前本适配器要求 EncryptKey 却只解析明文 —— 唯一能过验签的形状恰恰是它解析不了的形状，
        // 挑战找不到 challenge、事件找不到 event，两边都答 200 空 body，飞书的 URL 验证就此失败。
        if (!string.IsNullOrWhiteSpace(_options.EncryptKey))
        {
            if (!ValidateFeishuSignature(rawBody, new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)))
            {
                return WebhookProcessResult.Rejected("Invalid Feishu signature");
            }

            // 配置了密钥而 body 不是密文信封：不是飞书会发出的东西（只能是持钥者伪造），失败关闭。
            if (!TryDecryptEnvelope(rawBody, out var plaintext))
            {
                _logger.LogWarning("Feishu webhook body is not a valid encrypted envelope; rejecting");
                return WebhookProcessResult.Rejected("Feishu webhook body is not an encrypted envelope");
            }

            rawBody = plaintext;
        }
        else
        {
            // 未配置 EncryptKey 无法验签 —— 拒绝，绝不放行（与 Discord/Slack 同形状）。
            // ★ 此前这里没有 else 分支：漏配一个密钥，端点就从"验签的"变成"谁都能投递的"，
            // 而外观与配置正确时完全一致。
            _logger.LogWarning("Feishu EncryptKey is not configured; rejecting unverifiable webhook");
            return WebhookProcessResult.Rejected("Feishu EncryptKey not configured");
        }

        // URL 验证握手（url_verification）：校验 token（若配置）后原样回显 challenge。
        if (TryGetFeishuChallenge(rawBody, out var challenge, out var tokenMatches))
        {
            if (!tokenMatches)
            {
                return WebhookProcessResult.Rejected("Feishu verification token mismatch");
            }
            return WebhookProcessResult.Challenge(JsonSerializer.Serialize(new { challenge }));
        }

        await HandleEventCoreAsync(rawBody, ct);
        return WebhookProcessResult.Accepted();
    }

    /// <summary>
    /// 解开飞书「事件加密」信封 <c>{"encrypt":"base64"}</c>：key = SHA256(EncryptKey)，
    /// 密文 = IV(16 字节) ‖ AES-256-CBC(PKCS7) 密文。非信封 / 解不开 / 解出的不是 JSON 一律返回 false。
    /// </summary>
    internal bool TryDecryptEnvelope(string rawBody, out string plaintext)
    {
        plaintext = string.Empty;
        string? encrypted;
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("encrypt", out var encEl)
                || encEl.ValueKind != JsonValueKind.String)
            {
                return false;
            }
            encrypted = encEl.GetString();
        }
        catch (JsonException)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(encrypted)) return false;

        try
        {
            var payload = Convert.FromBase64String(encrypted);
            if (payload.Length <= 16 || (payload.Length - 16) % 16 != 0) return false;

            using var aes = Aes.Create();
            aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(_options.EncryptKey!));
            var decrypted = aes.DecryptCbc(payload.AsSpan(16), payload.AsSpan(0, 16), PaddingMode.PKCS7);
            var text = Encoding.UTF8.GetString(decrypted);

            // 解出来的必须是 JSON 对象，否则就是密钥不对而恰好通过了填充校验
            using var check = JsonDocument.Parse(text);
            if (check.RootElement.ValueKind != JsonValueKind.Object) return false;

            plaintext = text;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException or DecoderFallbackException)
        {
            _logger.LogDebug(ex, "Feishu encrypted envelope could not be decrypted");
            return false;
        }
    }

    private bool TryGetFeishuChallenge(string body, out string? challenge, out bool tokenMatches)
    {
        challenge = null;
        tokenMatches = true;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("challenge", out var chEl))
            {
                return false;
            }

            challenge = chEl.GetString();
            if (challenge == null) return false;

            // 若配置了 VerificationToken，则校验握手中的 token。
            if (!string.IsNullOrWhiteSpace(_options.VerificationToken) &&
                root.TryGetProperty("token", out var tokenEl))
            {
                var token = tokenEl.GetString();
                tokenMatches = string.Equals(token, _options.VerificationToken, StringComparison.Ordinal);
            }
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task HandleEventCoreAsync(string eventJson, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(eventJson);
            var root = doc.RootElement;

            // URL verification challenge
            if (root.TryGetProperty("challenge", out _))
                return; // Controller layer handles challenge response

            if (!root.TryGetProperty("event", out var eventObj)) return;
            if (!eventObj.TryGetProperty("message", out var msgObj)) return;

            var chatId = msgObj.GetProperty("chat_id").GetString() ?? "";
            var senderId = eventObj.TryGetProperty("sender", out var sender)
                ? sender.GetProperty("sender_id").GetProperty("open_id").GetString() ?? ""
                : "";

            // User allowlist
            if (_allowedUsers.Count > 0 && !_allowedUsers.Contains(senderId))
            {
                _logger.LogDebug("Feishu message from non-allowed user {UserId}, ignoring", senderId);
                return;
            }

            var msgType = msgObj.GetProperty("message_type").GetString();
            if (msgType != "text") return;

            using var content = JsonDocument.Parse(msgObj.GetProperty("content").GetString() ?? "{}");
            var text = content.RootElement.GetProperty("text").GetString() ?? "";

            var isCommand = text.StartsWith('/');
            var inbound = new InboundMessage(
                ChannelName: Name,
                ChatId: chatId,
                UserId: senderId,
                Text: text,
                Type: isCommand ? InboundMessageType.Command : InboundMessageType.Chat);

            await _bus.PublishInboundAsync(inbound, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process Feishu event");
        }
    }

    public async Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        var token = await _tokenRefresher.GetTokenAsync(ct);
        var client = _httpClientFactory.CreateClient(ResilientHttpClientNames.Fallback);

        var payload = JsonSerializer.Serialize(new
        {
            receive_id = message.ChatId,
            msg_type = "text",
            content = JsonSerializer.Serialize(new { text = message.Text })
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/im/v1/messages?receive_id_type=chat_id");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Feishu send failed: {StatusCode} {Body}", response.StatusCode, body);
        }
    }

    public ValueTask DisposeAsync()
        => _tokenRefresher.DisposeAsync();

    /// <summary>
    /// 调用飞书 API 刷新 tenant_access_token。
    /// </summary>
    private async Task<(string Token, int ExpiresInSeconds)> RefreshTenantAccessTokenAsync(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(ResilientHttpClientNames.Fallback);
        var response = await client.PostAsJsonAsync(
            $"{BaseUrl}/auth/v3/tenant_access_token/internal",
            new { app_id = _options.AppId, app_secret = _options.AppSecret }, ct);

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var token = root.GetProperty("tenant_access_token").GetString()!;
        var expire = root.GetProperty("expire").GetInt32();
        return (token, expire);
    }
}
