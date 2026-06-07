using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace Tnzi.AI.Channels.Adapters.Discord;

/// <summary>
/// Discord 频道适配器 — 通过 HTTP REST API 收发消息，支持 Webhook/Gateway 事件接收。
/// </summary>
/// <remarks>
/// 使用纯 HTTP API 调用（无 Discord.NET SDK 依赖）：
/// - POST /channels/{id}/messages: 发送消息
/// - 文件上传: multipart/form-data
/// - 事件接收通过 Webhook/Gateway 由 Controller 调用 HandleEventAsync
/// </remarks>
public class DiscordChannelAdapter : IChannelAdapter, IInboundWebhookAdapter
{
    private const string BaseUrl = "https://discord.com/api/v10";

    private readonly ILogger<DiscordChannelAdapter> _logger;
    private readonly IChannelMessageBus _bus;
    private readonly DiscordAdapterOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HashSet<string> _allowedGuilds;
    private readonly HashSet<string> _allowedChannels;
    private readonly HashSet<string> _allowedUsers;

    public string Name => "discord";
    public bool SupportsStreaming => false;
    public bool SupportsFileAttachment => true;

    public DiscordChannelAdapter(
        ILogger<DiscordChannelAdapter> logger,
        IChannelMessageBus bus,
        IHttpClientFactory httpClientFactory,
        IOptions<ChannelsModuleOptions> options)
    {
        _logger = Check.NotNull(logger);
        _bus = Check.NotNull(bus);
        _httpClientFactory = Check.NotNull(httpClientFactory);
        _options = Check.NotNull(options).Value.Discord;

        if (string.IsNullOrWhiteSpace(_options.BotToken))
            throw new ArgumentException("Discord BotToken is required when adapter is enabled");

        _allowedGuilds = [.. _options.AllowedGuilds];
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

    /// <summary>检查 Guild 是否被允许（空白名单=不限制）</summary>
    public bool IsGuildAllowed(string guildId)
    {
        return _allowedGuilds.Count == 0 || _allowedGuilds.Contains(guildId);
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Discord channel adapter started (webhook mode)");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Discord channel adapter stopped");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 处理 Discord Gateway/Webhook 事件（由 ASP.NET Controller 调用），
    /// 带 HTTP 请求头用于签名验证。
    /// </summary>
    /// <param name="eventJson">请求 body 原文</param>
    /// <param name="headers">HTTP 请求头（需包含 X-Signature-Ed25519 和 X-Signature-Timestamp）</param>
    /// <param name="ct">取消令牌</param>
    public Task HandleEventAsync(string eventJson, IDictionary<string, string>? headers, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(_options.PublicKey) && headers != null)
        {
            if (!ValidateDiscordSignature(eventJson, headers))
            {
                _logger.LogWarning("Discord webhook signature validation failed, rejecting event");
                return Task.CompletedTask;
            }
        }
        else if (!string.IsNullOrWhiteSpace(_options.PublicKey) && headers == null)
        {
            _logger.LogWarning("Discord PublicKey is configured but no headers provided for verification, rejecting event");
            return Task.CompletedTask;
        }

        return HandleEventCoreAsync(eventJson, ct);
    }

    /// <summary>
    /// 处理 Discord Gateway/Webhook 事件（由 ASP.NET Controller 调用）。
    /// 不含签名验证的兼容重载 — 仅在未配置 PublicKey 时安全。
    /// </summary>
    public Task HandleEventAsync(string eventJson, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(_options.PublicKey))
        {
            _logger.LogWarning("Discord PublicKey is configured but HandleEventAsync called without headers, rejecting event");
            return Task.CompletedTask;
        }

        return HandleEventCoreAsync(eventJson, ct);
    }

    /// <summary>
    /// 验证 Discord Webhook 签名（Ed25519）。
    /// </summary>
    /// <remarks>
    /// Discord 签名验证流程:
    /// 1. 获取 X-Signature-Ed25519（hex-encoded 签名）和 X-Signature-Timestamp
    /// 2. 构造待验证数据 = timestamp + body
    /// 3. 使用 PublicKey 验证 Ed25519 签名
    /// </remarks>
    internal bool ValidateDiscordSignature(string body, IDictionary<string, string> headers)
    {
        if (!headers.TryGetValue("X-Signature-Ed25519", out var signature) || string.IsNullOrWhiteSpace(signature))
        {
            _logger.LogDebug("Missing X-Signature-Ed25519 header");
            return false;
        }

        if (!headers.TryGetValue("X-Signature-Timestamp", out var timestamp) || string.IsNullOrWhiteSpace(timestamp))
        {
            _logger.LogDebug("Missing X-Signature-Timestamp header");
            return false;
        }

        try
        {
            var publicKeyBytes = Convert.FromHexString(_options.PublicKey!);
            var signatureBytes = Convert.FromHexString(signature);
            var messageBytes = Encoding.UTF8.GetBytes(timestamp + body);

            if (publicKeyBytes.Length != 32 || signatureBytes.Length != 64)
            {
                _logger.LogDebug("Invalid Ed25519 key or signature length");
                return false;
            }

            // Timestamp freshness check (reject requests older than 5 minutes)
            if (!long.TryParse(timestamp, out var ts))
            {
                _logger.LogDebug("Invalid X-Signature-Timestamp value");
                return false;
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (Math.Abs(now - ts) > 300)
            {
                _logger.LogDebug("Discord request timestamp is too old ({Diff}s)", Math.Abs(now - ts));
                return false;
            }

            // Ed25519 signature verification via NSec.Cryptography (libsodium-backed).
            // Discord signs (timestamp + rawBody) with the application's Ed25519 private key;
            // we verify with the configured hex-encoded public key.
            var algorithm = NSec.Cryptography.SignatureAlgorithm.Ed25519;
            var publicKey = NSec.Cryptography.PublicKey.Import(
                algorithm, publicKeyBytes, NSec.Cryptography.KeyBlobFormat.RawPublicKey);

            return algorithm.Verify(publicKey, messageBytes, signatureBytes);
        }
        catch (FormatException ex)
        {
            _logger.LogDebug(ex, "Invalid hex encoding in Discord signature or public key");
            return false;
        }
        catch (Exception ex)
        {
            // NSec throws on malformed key material; treat any verification failure as rejection.
            _logger.LogDebug(ex, "Discord Ed25519 signature verification failed");
            return false;
        }
    }

    /// <inheritdoc />
    public string Platform => Name;

    /// <inheritdoc />
    public async Task<WebhookProcessResult> ProcessWebhookAsync(
        string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
    {
        // Discord 要求：先验签（包括 PING 在内的所有请求），失败一律拒绝。
        if (!string.IsNullOrWhiteSpace(_options.PublicKey))
        {
            if (!ValidateDiscordSignature(rawBody, new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)))
            {
                return WebhookProcessResult.Rejected("Invalid Discord Ed25519 signature");
            }
        }
        else
        {
            // 未配置 PublicKey 无法验签 — 拒绝（外部回调必须可验证）。
            _logger.LogWarning("Discord PublicKey is not configured; rejecting unverifiable webhook");
            return WebhookProcessResult.Rejected("Discord PublicKey not configured");
        }

        // 验签通过后再处理 Interactions PING（type=1）→ PONG（type=1）。
        if (IsPing(rawBody))
        {
            return WebhookProcessResult.Challenge("{\"type\":1}");
        }

        await HandleEventCoreAsync(rawBody, ct);
        return WebhookProcessResult.Accepted();
    }

    private static bool IsPing(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.Number && t.GetInt32() == 1;
        }
        catch (JsonException) { return false; }
    }

    private async Task HandleEventCoreAsync(string eventJson, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(eventJson);
            var root = doc.RootElement;

            // Discord Interactions 验证 ping（type=1）
            if (root.TryGetProperty("type", out var typeEl) && typeEl.GetInt32() == 1)
                return; // Controller 层处理 ping 响应

            // Gateway 事件格式: { "t": "MESSAGE_CREATE", "d": { ... } }
            var eventType = root.TryGetProperty("t", out var tEl) ? tEl.GetString() : null;
            if (eventType != "MESSAGE_CREATE") return;

            if (!root.TryGetProperty("d", out var data)) return;

            // 忽略 bot 消息（避免死循环）
            if (data.TryGetProperty("author", out var author) &&
                author.TryGetProperty("bot", out var botProp) &&
                botProp.GetBoolean())
                return;

            var channelId = data.TryGetProperty("channel_id", out var ch) ? ch.GetString() ?? "" : "";
            var userId = author.TryGetProperty("id", out var uid) ? uid.GetString() ?? "" : "";
            var text = data.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
            var guildId = data.TryGetProperty("guild_id", out var g) ? g.GetString() : null;
            var messageId = data.TryGetProperty("id", out var mid) ? mid.GetString() : null;

            // Discord 线程：message_reference 表示回复，thread 的 channel 本身就是线程 ID
            var threadId = data.TryGetProperty("message_reference", out var msgRef) &&
                           msgRef.TryGetProperty("channel_id", out var refCh)
                ? refCh.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(text)) return;

            // Guild 白名单
            if (guildId != null && !IsGuildAllowed(guildId))
            {
                _logger.LogDebug("Discord message from non-allowed guild {GuildId}, ignoring", guildId);
                return;
            }

            // 频道白名单
            if (!IsChannelAllowed(channelId))
            {
                _logger.LogDebug("Discord message from non-allowed channel {ChannelId}, ignoring", channelId);
                return;
            }

            // 用户白名单
            if (!IsUserAllowed(userId))
            {
                _logger.LogDebug("Discord message from non-allowed user {UserId}, ignoring", userId);
                return;
            }

            var isCommand = text.StartsWith('/');
            var inbound = new InboundMessage(
                ChannelName: Name,
                ChatId: channelId,
                UserId: userId,
                Text: text,
                Type: isCommand ? InboundMessageType.Command : InboundMessageType.Chat,
                ThreadTs: threadId ?? messageId);

            await _bus.PublishInboundAsync(inbound, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process Discord event");
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
            _logger.LogWarning(ex, "Failed to upload file to Discord: {FileName}", attachment.FileName);
            return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 调用 Discord REST API 发送消息
    /// </summary>
    private async Task PostMessageAsync(string channelId, string content, string? threadTs, CancellationToken ct)
    {
        var client = CreateAuthorizedClient();

        var payload = new Dictionary<string, object?>
        {
            ["content"] = content
        };

        // Discord 线程回复使用 message_reference
        if (!string.IsNullOrWhiteSpace(threadTs))
        {
            payload["message_reference"] = new Dictionary<string, object>
            {
                ["message_id"] = threadTs
            };
        }

        var response = await client.PostAsJsonAsync($"{BaseUrl}/channels/{channelId}/messages", payload, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Discord POST /channels/{ChannelId}/messages HTTP error: {StatusCode} {Body}",
                channelId, response.StatusCode, responseBody);
            throw new HttpRequestException($"Discord API returned {response.StatusCode}");
        }
    }

    /// <summary>
    /// 调用 Discord REST API 上传文件（multipart/form-data）
    /// </summary>
    private async Task UploadFileAsync(string channelId, ResolvedAttachment attachment, string? threadTs, CancellationToken ct)
    {
        var client = CreateAuthorizedClient();

        await using var stream = System.IO.File.OpenRead(attachment.ActualPath);
        using var content = new MultipartFormDataContent();

        content.Add(new StreamContent(stream), "files[0]", attachment.FileName);

        // 可选的 JSON payload（用于 message_reference）
        if (!string.IsNullOrWhiteSpace(threadTs))
        {
            var payloadJson = JsonSerializer.Serialize(new
            {
                message_reference = new { message_id = threadTs }
            });
            content.Add(new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json"), "payload_json");
        }

        var response = await client.PostAsync($"{BaseUrl}/channels/{channelId}/messages", content, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Discord file upload HTTP error: {StatusCode} {Body}", response.StatusCode, responseBody);
            throw new HttpRequestException($"Discord file upload returned {response.StatusCode}");
        }
    }

    /// <summary>
    /// 创建带 Bot Token 授权头的 HttpClient
    /// </summary>
    private HttpClient CreateAuthorizedClient()
    {
        var client = _httpClientFactory.CreateClient("Tnzi.AI.Discord");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", _options.BotToken);
        return client;
    }
}
