using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace Tnzi.AI.Channels.Adapters.Slack;

/// <summary>
/// Slack 频道适配器 — 通过 HTTP REST API 收发消息，支持 Webhook 事件接收。
/// </summary>
/// <remarks>
/// 使用纯 HTTP API 调用（无第三方 Slack SDK 依赖）：
/// - chat.postMessage: 发送消息
/// - files.upload: 发送文件
/// - 事件接收通过 Webhook（Event Subscriptions）由 Controller 调用 HandleEventAsync
/// </remarks>
public class SlackChannelAdapter : IChannelAdapter, IInboundWebhookAdapter
{
    private const string BaseUrl = "https://slack.com/api";

    private readonly ILogger<SlackChannelAdapter> _logger;
    private readonly IChannelMessageBus _bus;
    private readonly SlackAdapterOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HashSet<string> _allowedChannels;
    private readonly HashSet<string> _allowedUsers;

    public string Name => "slack";
    public bool SupportsStreaming => false;
    public bool SupportsFileAttachment => true;

    /// <summary>此渠道 Bot 实例归属的租户（来自 adapter options；null = 单租户/全局）</summary>
    public Guid? TenantId => _options.TenantId;

    public SlackChannelAdapter(
        ILogger<SlackChannelAdapter> logger,
        IChannelMessageBus bus,
        IHttpClientFactory httpClientFactory,
        IOptions<ChannelsModuleOptions> options)
    {
        _logger = Check.NotNull(logger);
        _bus = Check.NotNull(bus);
        _httpClientFactory = Check.NotNull(httpClientFactory);
        _options = Check.NotNull(options).Value.Slack;

        if (string.IsNullOrWhiteSpace(_options.BotToken))
            throw new ArgumentException("Slack BotToken is required when adapter is enabled");

        _allowedChannels = [.. _options.AllowedChannels];
        _allowedUsers = [.. _options.AllowedUsers];
    }

    /// <summary>检查用户是否被允许（空白名单=不限制）</summary>
    public bool IsUserAllowed(string userId)
    {
        return _allowedUsers.Count == 0 || _allowedUsers.Contains(userId);
    }

    /// <summary>检查频道是否被允许（空白名单=不限制）</summary>
    public bool IsChannelAllowed(string channelId)
    {
        return _allowedChannels.Count == 0 || _allowedChannels.Contains(channelId);
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Slack channel adapter started (webhook mode)");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Slack channel adapter stopped");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 处理 Slack Events API Webhook 回调（由 ASP.NET Controller 调用），
    /// 带 HTTP 请求头用于签名验证。
    /// </summary>
    /// <param name="eventJson">请求 body 原文</param>
    /// <param name="headers">HTTP 请求头（需包含 X-Slack-Signature 和 X-Slack-Request-Timestamp）</param>
    /// <param name="ct">取消令牌</param>
    public Task HandleEventAsync(string eventJson, IDictionary<string, string>? headers, CancellationToken ct = default)
    {
        // 签名验证（如果配置了 SigningSecret 且提供了 headers）
        if (!string.IsNullOrWhiteSpace(_options.SigningSecret) && headers != null)
        {
            if (!ValidateSlackSignature(eventJson, headers))
            {
                _logger.LogWarning("Slack webhook signature validation failed, rejecting event");
                return Task.CompletedTask;
            }
        }
        else if (!string.IsNullOrWhiteSpace(_options.SigningSecret) && headers == null)
        {
            _logger.LogWarning("Slack SigningSecret is configured but no headers provided for verification, rejecting event");
            return Task.CompletedTask;
        }

        return HandleEventCoreAsync(eventJson, ct);
    }

    /// <summary>
    /// 处理 Slack Events API Webhook 回调（由 ASP.NET Controller 调用）。
    /// 不含签名验证的兼容重载 — 仅在未配置 SigningSecret 时安全。
    /// </summary>
    public Task HandleEventAsync(string eventJson, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(_options.SigningSecret))
        {
            _logger.LogWarning("Slack SigningSecret is configured but HandleEventAsync called without headers, rejecting event");
            return Task.CompletedTask;
        }

        return HandleEventCoreAsync(eventJson, ct);
    }

    /// <summary>
    /// 验证 Slack Webhook 签名（HMAC-SHA256）。
    /// </summary>
    /// <remarks>
    /// Slack 签名验证流程:
    /// 1. 获取 X-Slack-Request-Timestamp，检查时间差不超过 5 分钟（防重放）
    /// 2. 构造 basestring = "v0:{timestamp}:{body}"
    /// 3. 用 SigningSecret 计算 HMAC-SHA256
    /// 4. 比较 "v0={hex_hash}" 与 X-Slack-Signature
    /// </remarks>
    internal bool ValidateSlackSignature(string body, IDictionary<string, string> headers)
    {
        if (!headers.TryGetValue("X-Slack-Signature", out var signature) || string.IsNullOrWhiteSpace(signature))
        {
            _logger.LogDebug("Missing X-Slack-Signature header");
            return false;
        }

        if (!headers.TryGetValue("X-Slack-Request-Timestamp", out var timestampStr) || string.IsNullOrWhiteSpace(timestampStr))
        {
            _logger.LogDebug("Missing X-Slack-Request-Timestamp header");
            return false;
        }

        // 防重放攻击：时间戳不超过 5 分钟
        if (!long.TryParse(timestampStr, out var timestamp))
        {
            _logger.LogDebug("Invalid X-Slack-Request-Timestamp value");
            return false;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - timestamp) > 300)
        {
            _logger.LogDebug("Slack request timestamp is too old ({Diff}s)", Math.Abs(now - timestamp));
            return false;
        }

        // 计算 HMAC-SHA256
        var baseString = $"v0:{timestampStr}:{body}";
        var keyBytes = Encoding.UTF8.GetBytes(_options.SigningSecret!);
        var baseBytes = Encoding.UTF8.GetBytes(baseString);

        using var hmac = new HMACSHA256(keyBytes);
        var hashBytes = hmac.ComputeHash(baseBytes);
        var computedSignature = "v0=" + Convert.ToHexStringLower(hashBytes);

        // 使用固定时间比较防止时序攻击
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedSignature),
            Encoding.UTF8.GetBytes(signature));
    }

    /// <inheritdoc />
    public string Platform => Name;

    /// <inheritdoc />
    public async Task<WebhookProcessResult> ProcessWebhookAsync(
        string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
    {
        // URL 验证 challenge（Slack Events API 首次验证）— 在验签前回显（Slack 规范允许）。
        if (TryGetSlackChallenge(rawBody, out var challenge))
        {
            return WebhookProcessResult.Challenge(challenge!, "text/plain");
        }

        // 验签：配置了 SigningSecret 时强制校验 HMAC-SHA256 + 5 分钟时间窗。
        if (!string.IsNullOrWhiteSpace(_options.SigningSecret))
        {
            if (!ValidateSlackSignature(rawBody, ToMutable(headers)))
            {
                return WebhookProcessResult.Rejected("Invalid Slack signature");
            }
        }

        await HandleEventCoreAsync(rawBody, ct);
        return WebhookProcessResult.Accepted();
    }

    private static bool TryGetSlackChallenge(string body, out string? challenge)
    {
        challenge = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("type", out var typeEl) &&
                typeEl.GetString() == "url_verification" &&
                doc.RootElement.TryGetProperty("challenge", out var chEl))
            {
                challenge = chEl.GetString();
                return challenge != null;
            }
        }
        catch (JsonException) { /* not a challenge */ }
        return false;
    }

    private static Dictionary<string, string> ToMutable(IReadOnlyDictionary<string, string> headers)
        => new(headers, StringComparer.OrdinalIgnoreCase);

    private async Task HandleEventCoreAsync(string eventJson, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(eventJson);
            var root = doc.RootElement;

            // URL 验证 challenge（Slack Events API 首次验证）
            if (root.TryGetProperty("challenge", out _))
                return; // Controller 层处理 challenge 响应

            if (!root.TryGetProperty("event", out var eventObj)) return;

            var eventType = eventObj.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
            if (eventType != "message") return;

            // 忽略 bot 自身消息（避免死循环）
            if (eventObj.TryGetProperty("bot_id", out _)) return;

            // 忽略子类型消息（message_changed, message_deleted 等）
            if (eventObj.TryGetProperty("subtype", out _)) return;

            var channelId = eventObj.TryGetProperty("channel", out var ch) ? ch.GetString() ?? "" : "";
            var userId = eventObj.TryGetProperty("user", out var u) ? u.GetString() ?? "" : "";
            var text = eventObj.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            var threadTs = eventObj.TryGetProperty("thread_ts", out var ts) ? ts.GetString() : null;
            var messageTs = eventObj.TryGetProperty("ts", out var mts) ? mts.GetString() : null;

            if (string.IsNullOrWhiteSpace(text)) return;

            // 频道白名单
            if (!IsChannelAllowed(channelId))
            {
                _logger.LogDebug("Slack message from non-allowed channel {ChannelId}, ignoring", channelId);
                return;
            }

            // 用户白名单
            if (!IsUserAllowed(userId))
            {
                _logger.LogDebug("Slack message from non-allowed user {UserId}, ignoring", userId);
                return;
            }

            var isCommand = text.StartsWith('/');
            var inbound = new InboundMessage(
                ChannelName: Name,
                ChatId: channelId,
                UserId: userId,
                Text: text,
                Type: isCommand ? InboundMessageType.Command : InboundMessageType.Chat,
                ThreadTs: threadTs ?? messageTs);

            await _bus.PublishInboundAsync(inbound, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process Slack event");
        }
    }

    public Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        return ChannelSendHelper.SendChunkedWithRetryAsync(
            message.Text,
            _options.MaxMessageLength,
            _options.MaxRetries,
            (chunk, token) => PostMessageAsync(message.ChatId, chunk, message.ThreadTs, token),
            _logger,
            Name,
            ct);
    }

    public async Task<bool> SendFileAsync(OutboundMessage message, ResolvedAttachment attachment, CancellationToken ct = default)
    {
        if (!System.IO.File.Exists(attachment.ActualPath))
        {
            _logger.LogWarning("Attachment file not found: {Path}", attachment.ActualPath);
            return false;
        }

        if (attachment.Size > _options.MaxFileSize)
        {
            _logger.LogWarning("Attachment too large ({Size} bytes): {FileName}", attachment.Size, attachment.FileName);
            return false;
        }

        try
        {
            await UploadFileAsync(message.ChatId, attachment, message.ThreadTs, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to upload file to Slack: {FileName}", attachment.FileName);
            return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 调用 Slack chat.postMessage API
    /// </summary>
    private async Task PostMessageAsync(string channel, string text, string? threadTs, CancellationToken ct)
    {
        var client = CreateAuthorizedClient();

        var payload = new Dictionary<string, object?>
        {
            ["channel"] = channel,
            ["text"] = text
        };

        if (!string.IsNullOrWhiteSpace(threadTs))
            payload["thread_ts"] = threadTs;

        var response = await client.PostAsJsonAsync($"{BaseUrl}/chat.postMessage", payload, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Slack chat.postMessage HTTP error: {StatusCode} {Body}", response.StatusCode, responseBody);
            throw new HttpRequestException($"Slack API returned {response.StatusCode}");
        }

        // Slack API 可能返回 200 但 ok=false
        using var doc = JsonDocument.Parse(responseBody);
        if (!doc.RootElement.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean())
        {
            var error = doc.RootElement.TryGetProperty("error", out var errProp) ? errProp.GetString() : "unknown";
            _logger.LogWarning("Slack chat.postMessage API error: {Error}", error);
            throw new HttpRequestException($"Slack API error: {error}");
        }
    }

    /// <summary>
    /// 调用 Slack files.upload API
    /// </summary>
    private async Task UploadFileAsync(string channel, ResolvedAttachment attachment, string? threadTs, CancellationToken ct)
    {
        var client = CreateAuthorizedClient();

        await using var stream = System.IO.File.OpenRead(attachment.ActualPath);
        using var content = new MultipartFormDataContent();

        content.Add(new StringContent(channel), "channels");
        content.Add(new StreamContent(stream), "file", attachment.FileName);

        if (!string.IsNullOrWhiteSpace(attachment.FileName))
            content.Add(new StringContent(attachment.FileName), "filename");

        if (!string.IsNullOrWhiteSpace(threadTs))
            content.Add(new StringContent(threadTs), "thread_ts");

        var response = await client.PostAsync($"{BaseUrl}/files.upload", content, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Slack files.upload HTTP error: {StatusCode} {Body}", response.StatusCode, responseBody);
            throw new HttpRequestException($"Slack files.upload returned {response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        if (!doc.RootElement.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean())
        {
            var error = doc.RootElement.TryGetProperty("error", out var errProp) ? errProp.GetString() : "unknown";
            _logger.LogWarning("Slack files.upload API error: {Error}", error);
            throw new HttpRequestException($"Slack files.upload error: {error}");
        }
    }

    /// <summary>
    /// 创建带 Bearer Token 授权头的 HttpClient
    /// </summary>
    private HttpClient CreateAuthorizedClient()
    {
        var client = _httpClientFactory.CreateClient("Tnzi.AI.Slack");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.BotToken);
        return client;
    }
}
