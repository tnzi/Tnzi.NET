using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace Tnzi.AI.Channels.Adapters.Feishu;

/// <summary>
/// 飞书频道适配器 — 通过 HTTP REST API 收发消息，支持流式卡片更新。
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

    private string? _tenantAccessToken;
    private DateTime _tokenExpiry = DateTime.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public string Name => "feishu";
    public bool SupportsStreaming => true;

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
        // 注：本适配器处理已解密的 JSON（plaintext / 已由前置网关解密）；若启用了"事件加密"
        // （body 为 {"encrypt":"..."}），需在前置层先用 EncryptKey 做 AES 解密后再转发。
        if (!string.IsNullOrWhiteSpace(_options.EncryptKey))
        {
            if (!ValidateFeishuSignature(rawBody, new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)))
            {
                return WebhookProcessResult.Rejected("Invalid Feishu signature");
            }
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
        var token = await GetTenantAccessTokenAsync(ct);
        var client = _httpClientFactory.CreateClient("Tnzi.AI.Resilient");

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

    public Task<bool> SendFileAsync(OutboundMessage message, ResolvedAttachment attachment, CancellationToken ct = default)
        => Task.FromResult(false);

    public ValueTask DisposeAsync()
    {
        _tokenLock.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 获取 tenant_access_token（双重检查锁定，有效期 2 小时，提前 5 分钟刷新）
    /// </summary>
    private async Task<string> GetTenantAccessTokenAsync(CancellationToken ct)
    {
        if (_tenantAccessToken != null && DateTime.UtcNow < _tokenExpiry)
            return _tenantAccessToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_tenantAccessToken != null && DateTime.UtcNow < _tokenExpiry)
                return _tenantAccessToken;

            var client = _httpClientFactory.CreateClient("Tnzi.AI.Resilient");
            var response = await client.PostAsJsonAsync(
                $"{BaseUrl}/auth/v3/tenant_access_token/internal",
                new { app_id = _options.AppId, app_secret = _options.AppSecret }, ct);

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            _tenantAccessToken = root.GetProperty("tenant_access_token").GetString()!;
            var expire = root.GetProperty("expire").GetInt32();
            _tokenExpiry = DateTime.UtcNow.AddSeconds(expire - 300);

            _logger.LogDebug("Feishu tenant access token refreshed, expires in {Seconds}s", expire);
            return _tenantAccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}
