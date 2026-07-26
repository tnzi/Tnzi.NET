namespace Tnzi.AI.Mcp.Options;

/// <summary>
/// MCP Server 配置选项 - 将 Tnzi.AI Agent 暴露为 MCP Server 供外部客户端调用
/// </summary>
[ConfigSection("AI:McpServer")]
[RuntimeSettingGroup(Key = "ai-mcp-server", Module = "AI", DisplayName = "MCP Server",
    I18nKey = "admin.modules.system.settings.groups.aiMcpServer",
    Icon = "mdi:server-network-outline", Order = 190)]
public class McpServerOptions
{
    /// <summary>
    /// 是否启用 MCP Server（默认关闭）
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// HTTP/SSE 端点路径
    /// </summary>
    public string Endpoint { get; set; } = "/mcp";

    /// <summary>
    /// 暴露的 Agent ID 列表（配置时预注册）
    /// </summary>
    public List<Guid> ExposedAgentIds { get; set; } = [];

    /// <summary>
    /// 是否要求认证（默认开启）
    /// </summary>
    [RuntimeSetting(Label = "Require Authentication", I18n = "admin.modules.system.settings.fields.mcpServerRequireAuthentication",
        Type = SettingFieldType.Boolean, Subsection = "Authentication",
        Description = "SECURITY BOUNDARY. When OFF, every request to the MCP endpoint is accepted without an API key, exposing all wired agents/tools to anyone who can reach the endpoint. Keep this ON in any shared or internet-reachable deployment.")]
    public bool RequireAuthentication { get; set; } = true;

    /// <summary>
    /// 允许的 API Key 列表（简单认证方式）
    /// </summary>
    public List<string> AllowedApiKeys { get; set; } = [];

    /// <summary>
    /// 是否按租户分区速率限制键（默认开启）。
    /// <para>
    /// 当 <c>RateLimitPerTenant = true</c> 时，<c>X-Tenant-Id</c> 请求头的值会被追加到
    /// 速率限制键（client key）中，使各租户拥有独立的速率限制配额。
    /// </para>
    /// <para>
    /// <b>注意 - 此选项仅做限流分区，不做执行隔离：</b> Agent 调用运行在根 DI 容器创建的
    /// scope 中，没有 <c>ICurrentTenant.Change</c>，因此 MCP Server 对 Agent 执行而言是
    /// 单租户的。若需要真正的按租户执行隔离，须在应用层另行实现（此字段不提供该能力，
    /// 故命名为 RateLimitPerTenant 而非 TenantIsolation 以免误导）。
    /// </para>
    /// </summary>
    [RuntimeSetting(Label = "Rate-limit per Tenant", I18n = "admin.modules.system.settings.fields.mcpServerRateLimitPerTenant",
        Type = SettingFieldType.Boolean, Subsection = "Rate Limiting",
        Description = "Partition rate-limit buckets by the (untrusted) X-Tenant-Id header. This is rate-limit partitioning only, NOT execution or data isolation.")]
    public bool RateLimitPerTenant { get; set; } = true;

    /// <summary>
    /// 是否启用审计日志（默认开启）。
    /// <para>
    /// 仅控制经 <c>IUsageLogService</c>（operation type <c>McpToolCall</c>）写入的审计日志，
    /// <b>不影响</b>工具运营统计（见 <see cref="EnableToolAnalytics"/>）。
    /// </para>
    /// </summary>
    [RuntimeSetting(Label = "Enable Audit Log", I18n = "admin.modules.system.settings.fields.mcpServerEnableAuditLog",
        Type = SettingFieldType.Boolean, Subsection = "Logging",
        Description = "Write per-call audit entries via IUsageLogService (operation type McpToolCall). Independent of tool analytics.")]
    public bool EnableAuditLog { get; set; } = true;

    /// <summary>
    /// 是否启用工具运营统计（默认开启）。
    /// <para>
    /// 控制经 <c>IMcpToolAnalyticsService.RecordUsageAsync</c> 写入的 per-tool 统计
    /// （调用量/P95 延迟/错误率/唯一调用方）。此开关独立于 <see cref="EnableAuditLog"/>：
    /// 关闭审计日志不会同时关闭运营统计，反之亦然。
    /// </para>
    /// </summary>
    [RuntimeSetting(Label = "Enable Tool Analytics", I18n = "admin.modules.system.settings.fields.mcpServerEnableToolAnalytics",
        Type = SettingFieldType.Boolean, Subsection = "Logging",
        Description = "Record per-tool operational stats (call count, P95 latency, error rate, unique callers). Independent of the audit log.")]
    public bool EnableToolAnalytics { get; set; } = true;

    /// <summary>
    /// 每分钟速率限制（每客户端，默认 600）
    /// </summary>
    [RuntimeSetting(Label = "Rate Limit (per minute)", I18n = "admin.modules.system.settings.fields.mcpServerRateLimitPerMinute",
        Type = SettingFieldType.Int, Min = 0, Subsection = "Rate Limiting",
        Description = "Per-client sliding-window request cap per minute. 0 disables rate limiting.")]
    public int RateLimitPerMinute { get; set; } = 600;

    /// <summary>
    /// Allow API key in query string (?apiKey=...). Default: false.
    /// Query strings commonly leak into access logs, proxy caches, browser history,
    /// and referrer headers - they are NOT a safe place for credentials. Enable this
    /// flag ONLY for transitional compatibility with legacy clients; prefer the
    /// X-Api-Key header or Authorization: Bearer. Every query-string key extraction
    /// emits a warning log entry when this is enabled.
    /// </summary>
    [RuntimeSetting(Label = "Allow API Key in Query String", I18n = "admin.modules.system.settings.fields.mcpServerAllowApiKeyInQuery",
        Type = SettingFieldType.Boolean, Subsection = "Authentication",
        Description = "SECURITY RISK. When ON, API keys may be passed as ?apiKey=... . Query strings leak into access logs, proxy caches, browser history, and referrer headers. Enable ONLY for transitional legacy-client compatibility; prefer the X-Api-Key header.")]
    public bool AllowApiKeyInQuery { get; set; } = false;

    /// <summary>
    /// Hard cap on the rate-limit tracking dictionary. Prevents unbounded memory
    /// growth under key-space flood attacks (many unique IPs/keys per second).
    /// When the dict reaches this size, opportunistic eviction is forced on the
    /// next CheckRateLimit call regardless of the usual eviction interval.
    /// </summary>
    [RuntimeSetting(Label = "Rate-limit Tracking Max Entries", I18n = "admin.modules.system.settings.fields.mcpServerRateLimitTrackingMaxEntries",
        Type = SettingFieldType.Int, Min = 1, Subsection = "Rate Limiting",
        Description = "Hard cap on the rate-limit tracking dictionary to bound memory under key-space flood attacks.")]
    public int RateLimitTrackingMaxEntries { get; set; } = 10_000;
}
