using System.Net.Http.Headers;
using System.Net.Http.Json;

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
public class FeishuChannelAdapter : IChannelAdapter
{
    private const string BaseUrl = "https://open.feishu.cn/open-apis";

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
    /// 处理飞书 Webhook 事件（由 ASP.NET Controller 调用）
    /// </summary>
    public async Task HandleEventAsync(string eventJson, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(eventJson);
            var root = doc.RootElement;

            // URL 验证 challenge
            if (root.TryGetProperty("challenge", out _))
                return; // Controller 层处理 challenge 响应

            if (!root.TryGetProperty("event", out var eventObj)) return;
            if (!eventObj.TryGetProperty("message", out var msgObj)) return;

            var chatId = msgObj.GetProperty("chat_id").GetString() ?? "";
            var senderId = eventObj.TryGetProperty("sender", out var sender)
                ? sender.GetProperty("sender_id").GetProperty("open_id").GetString() ?? ""
                : "";

            // 用户白名单
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

            await _bus.PublishInboundAsync(inbound);
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
