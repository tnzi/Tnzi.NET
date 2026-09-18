
namespace Tnzi.AI.Channels.Adapters.Discord;

/// <summary>
/// Discord 频道适配器 - 通过 HTTP REST API 收发消息，入站走 Interactions 回调（斜杠命令）。
/// </summary>
/// <remarks>
/// 使用纯 HTTP API 调用（无 Discord.NET SDK 依赖）：
/// - 入站：Discord 签过名的 HTTP 回调<b>只会投递 Interaction</b>（type=2 斜杠命令等），频道消息
///   （MESSAGE_CREATE）只在 WebSocket Gateway 上分发，框架不带 Gateway 客户端。斜杠命令三秒内先答
///   type=5（延迟应答），回复凭交互令牌 PATCH 回原消息。消费方须在 Discord 开发者门户注册斜杠命令：
///   带一个字符串参数的命令（如 <c>/ask prompt</c>）映射为聊天，无参数命令（<c>/new</c> / <c>/status</c> / <c>/help</c>）映射为命令路由。
/// - 出站：带交互令牌 → PATCH /webhooks/{app}/{token}/messages/@original（后续分块 POST /webhooks/{app}/{token}）；
///   否则 POST /channels/{id}/messages（纯文本；文件附件管线已于 2026-06-20 移除）
/// - <see cref="HandleEventAsync(string, CancellationToken)"/> 还接受 Gateway 分发帧（MESSAGE_CREATE），
///   供自带 Gateway 客户端的消费方转发；<see cref="ProcessWebhookAsync"/> 不接受该形状（Discord 从不会 POST 它）
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

    /// <summary>此渠道 Bot 实例归属的租户（来自 adapter options；null = 单租户/全局）</summary>
    public Guid? TenantId => _options.TenantId;

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
    /// 不含签名验证的兼容重载 - 仅在未配置 PublicKey 时安全。
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
            // 未配置 PublicKey 无法验签 - 拒绝（外部回调必须可验证）。
            _logger.LogWarning("Discord PublicKey is not configured; rejecting unverifiable webhook");
            return WebhookProcessResult.Rejected("Discord PublicKey not configured");
        }

        // 验签通过后按 Interaction 类型分发：PING（type=1）→ PONG；APPLICATION_COMMAND（type=2）→
        // 发布入站 + 延迟应答（type=5，Discord 要求三秒内应答，agent 回复远不止三秒）。
        // ★ 此前这里只认 Gateway 分发帧 { "t": "MESSAGE_CREATE", "d": {...} }：那是 WebSocket 的形状，
        // Discord 从不会 POST 它；真实的斜杠命令验签通过后被静默忽略、答 200 空 body，
        // 用户看到的是 "The application did not respond"。
        switch (ReadInteractionType(rawBody))
        {
            case InteractionTypePing:
                return WebhookProcessResult.Challenge("{\"type\":1}");
            case InteractionTypeApplicationCommand:
                // 只有真的进了入站管线才延迟应答：不在允许名单里（或载荷没法用）的交互此前也答 type=5 却从不跟进，
                // 用户看到 "thinking..." 十五分钟直到令牌过期。拒绝当场答 type=4 的临时消息（flags 64，只有本人可见）。
                return await HandleInteractionAsync(rawBody, ct) switch
                {
                    InteractionOutcome.Published => WebhookProcessResult.Challenge("{\"type\":5}"),
                    InteractionOutcome.NotAllowed => WebhookProcessResult.Challenge(EphemeralReply("You are not allowed to use this bot here.")),
                    _ => WebhookProcessResult.Challenge(EphemeralReply("This command could not be processed."))
                };
            default:
                _logger.LogDebug("Discord webhook payload is not a supported interaction; ignoring");
                return WebhookProcessResult.Accepted();
        }
    }

    private const int InteractionTypePing = 1;
    private const int InteractionTypeApplicationCommand = 2;
    private const int InteractionCallbackChannelMessage = 4;
    private const int MessageFlagEphemeral = 64;

    /// <summary>一条交互的去向：进了入站管线 / 被允许名单拒绝 / 载荷用不了（缺字段、解析失败）。</summary>
    private enum InteractionOutcome
    {
        Published,
        NotAllowed,
        Unusable
    }

    /// <summary>type=4 + flags 64：当场回一条只有发起人看得见的消息。</summary>
    private static string EphemeralReply(string content)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = InteractionCallbackChannelMessage,
            ["data"] = new Dictionary<string, object?> { ["content"] = content, ["flags"] = MessageFlagEphemeral }
        });
    private const int OptionTypeSubCommand = 1;
    private const int OptionTypeSubCommandGroup = 2;
    private const int OptionTypeString = 3;

    /// <summary>读取 Interaction.type；不是 Interaction（无数字 type）时返回 null。</summary>
    private static int? ReadInteractionType(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            // ValueKind 必须先判定：Gateway 载荷里 type 可能是非数字，裸 GetInt32() 会抛异常
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("type", out var t)
                   && t.ValueKind == JsonValueKind.Number
                ? t.GetInt32()
                : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// 把一条 APPLICATION_COMMAND 交互映射为入站消息：字符串参数拼成聊天文本，无字符串参数的命令
    /// 映射为 <c>/{name}</c> 走命令路由；交互令牌与应用 ID 随 Metadata 带出，供出站按交互回复。
    /// </summary>
    private async Task<InteractionOutcome> HandleInteractionAsync(string interactionJson, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(interactionJson);
            var root = doc.RootElement;

            var channelId = root.TryGetProperty("channel_id", out var ch) ? ch.GetString() ?? "" : "";
            var guildId = root.TryGetProperty("guild_id", out var g) ? g.GetString() : null;
            var token = root.TryGetProperty("token", out var tk) ? tk.GetString() : null;
            var applicationId = root.TryGetProperty("application_id", out var app) ? app.GetString() : null;
            var userId = ReadInteractionUserId(root);

            if (string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(userId)) return InteractionOutcome.Unusable;
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return InteractionOutcome.Unusable;

            var commandName = data.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(commandName)) return InteractionOutcome.Unusable;

            var stringValues = new List<string>();
            CollectStringOptions(data, stringValues);
            var text = stringValues.Count > 0 ? string.Join(" ", stringValues) : "/" + commandName;

            if (guildId != null && !IsGuildAllowed(guildId))
            {
                _logger.LogDebug("Discord interaction from non-allowed guild {GuildId}, rejecting", guildId);
                return InteractionOutcome.NotAllowed;
            }

            if (!IsChannelAllowed(channelId))
            {
                _logger.LogDebug("Discord interaction from non-allowed channel {ChannelId}, rejecting", channelId);
                return InteractionOutcome.NotAllowed;
            }

            if (!IsUserAllowed(userId))
            {
                _logger.LogDebug("Discord interaction from non-allowed user {UserId}, rejecting", userId);
                return InteractionOutcome.NotAllowed;
            }

            Dictionary<string, object>? metadata = null;
            if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(applicationId))
            {
                metadata = new Dictionary<string, object>
                {
                    [DiscordInteractionMetadata.Token] = token,
                    [DiscordInteractionMetadata.ApplicationId] = applicationId
                };
            }

            var inbound = new InboundMessage(
                ChannelName: Name,
                ChatId: channelId,
                UserId: userId,
                Text: text,
                Type: text.StartsWith('/') ? InboundMessageType.Command : InboundMessageType.Chat,
                Metadata: metadata);

            await _bus.PublishInboundAsync(inbound, ct);
            return InteractionOutcome.Published;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process Discord interaction");
            return InteractionOutcome.Unusable;
        }
    }

    /// <summary>群内交互带 member.user，私信交互带 user。</summary>
    private static string ReadInteractionUserId(JsonElement root)
    {
        if (root.TryGetProperty("member", out var member)
            && member.TryGetProperty("user", out var memberUser)
            && memberUser.TryGetProperty("id", out var mid))
        {
            return mid.GetString() ?? "";
        }

        return root.TryGetProperty("user", out var user) && user.TryGetProperty("id", out var uid)
            ? uid.GetString() ?? ""
            : "";
    }

    /// <summary>递归收集命令参数里的字符串值（子命令 / 子命令组会再嵌一层 options）。</summary>
    private static void CollectStringOptions(JsonElement node, List<string> values)
    {
        if (!node.TryGetProperty("options", out var options) || options.ValueKind != JsonValueKind.Array) return;

        foreach (var option in options.EnumerateArray())
        {
            var type = option.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : 0;
            if (type is OptionTypeSubCommand or OptionTypeSubCommandGroup)
            {
                CollectStringOptions(option, values);
            }
            else if (type == OptionTypeString && option.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String)
            {
                var value = v.GetString();
                if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
            }
        }
    }

    private async Task HandleEventCoreAsync(string eventJson, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(eventJson);
            var root = doc.RootElement;

            // Interaction（type 为数字）：PING 由控制器层应答；斜杠命令按交互映射；其它交互类型忽略。
            if (root.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.Number)
            {
                if (typeEl.GetInt32() == InteractionTypeApplicationCommand)
                {
                    await HandleInteractionAsync(eventJson, ct);
                }
                return;
            }

            // Gateway 分发帧: { "t": "MESSAGE_CREATE", "d": { ... } } —— 只有自带 Gateway 客户端的消费方会转发进来
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

            // Discord 线程：thread 的 channel_id 本身就是线程，ChatId 已把回复路由进线程；
            // ThreadTs 只承担 message_reference 引用，必须是这条消息自己的 id
            //（message_reference.channel_id 是频道 id，拿它当 message_id 回复必错）。
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
                ThreadTs: messageId);

            await _bus.PublishInboundAsync(inbound, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process Discord event");
        }
    }

    public Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        Func<string, CancellationToken, Task> sendChunk;
        if (TryGetInteraction(message.Metadata, out var applicationId, out var interactionToken))
        {
            // 交互回复：第一块 PATCH 延迟应答的原消息，后续分块作为 follow-up 追加。
            // 「已填过原消息」只在 PATCH 成功后才置位：发送前就翻转会让第一块的瞬时失败重试成 POST follow-up，
            // 答案变成一条后续消息，原来的斜杠命令停在 "thinking..." 直到令牌过期。
            var originalFilled = false;
            sendChunk = async (chunk, token) =>
            {
                await PostInteractionReplyAsync(applicationId, interactionToken, chunk, editOriginal: !originalFilled, token);
                originalFilled = true;
            };
        }
        else
        {
            sendChunk = (chunk, token) => PostMessageAsync(message.ChatId, chunk, message.ThreadTs, token);
        }

        return ChannelSendHelper.SendChunkedWithRetryAsync(
            message.Text,
            _options.MaxMessageLength,
            _options.MaxRetries,
            sendChunk,
            _logger,
            Name,
            ct);
    }

    private static bool TryGetInteraction(Dictionary<string, object>? metadata, out string applicationId, out string interactionToken)
    {
        applicationId = string.Empty;
        interactionToken = string.Empty;
        if (metadata is null) return false;
        if (!metadata.TryGetValue(DiscordInteractionMetadata.ApplicationId, out var app) || app is not string appId || string.IsNullOrWhiteSpace(appId)) return false;
        if (!metadata.TryGetValue(DiscordInteractionMetadata.Token, out var tok) || tok is not string token || string.IsNullOrWhiteSpace(token)) return false;
        applicationId = appId;
        interactionToken = token;
        return true;
    }

    /// <summary>
    /// 凭交互令牌回复：<c>PATCH /webhooks/{app}/{token}/messages/@original</c> 填充延迟应答，
    /// <c>POST /webhooks/{app}/{token}</c> 追加后续分块。Webhook 端点以令牌鉴权，不带 Bot 授权头。
    /// </summary>
    private async Task PostInteractionReplyAsync(string applicationId, string interactionToken, string content, bool editOriginal, CancellationToken ct)
    {
        var client = CreateClient();
        var url = editOriginal
            ? $"{BaseUrl}/webhooks/{applicationId}/{interactionToken}/messages/@original"
            : $"{BaseUrl}/webhooks/{applicationId}/{interactionToken}";

        using var request = new HttpRequestMessage(editOriginal ? HttpMethod.Patch : HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new Dictionary<string, object?> { ["content"] = content })
        };

        var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Discord interaction reply ({Method}) HTTP error: {StatusCode} {Body}",
                request.Method, response.StatusCode, responseBody);
            throw new HttpRequestException($"Discord API returned {response.StatusCode}");
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
        var client = CreateClient();

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

        // Set Authorization per-request - the named HttpClient is pooled and its
        // DefaultRequestHeaders must not be mutated across concurrent sends.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/channels/{channelId}/messages")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bot", _options.BotToken) },
            Content = JsonContent.Create(payload)
        };

        var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Discord POST /channels/{ChannelId}/messages HTTP error: {StatusCode} {Body}",
                channelId, response.StatusCode, responseBody);
            throw new HttpRequestException($"Discord API returned {response.StatusCode}");
        }
    }

    /// <summary>
    /// 获取命名 HttpClient（Authorization 头由每次请求单独设置，不触碰池化客户端的默认头）。
    /// </summary>
    private HttpClient CreateClient()
        => _httpClientFactory.CreateClient("Tnzi.AI.Discord");
}
