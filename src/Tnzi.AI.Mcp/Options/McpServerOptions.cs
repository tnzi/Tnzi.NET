namespace Tnzi.AI.Mcp.Options;

/// <summary>
/// MCP Server 配置选项 — 将 Tnzi.AI Agent 暴露为 MCP Server 供外部客户端调用
/// </summary>
public class McpServerOptions
{
    /// <summary>
    /// 是否启用 MCP Server（默认关闭）
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 传输模式: "stdio" 或 "sse"
    /// </summary>
    public string Transport { get; set; } = "stdio";

    /// <summary>
    /// HTTP/SSE 端点路径（仅在非 stdio 传输时使用）
    /// </summary>
    public string Endpoint { get; set; } = "/mcp";

    /// <summary>
    /// 暴露的 Agent ID 列表（配置时预注册）
    /// </summary>
    public List<Guid> ExposedAgentIds { get; set; } = [];

    /// <summary>
    /// 是否要求认证（默认开启）
    /// </summary>
    public bool RequireAuthentication { get; set; } = true;

    /// <summary>
    /// 允许的 API Key 列表（简单认证方式）
    /// </summary>
    public List<string> AllowedApiKeys { get; set; } = [];

    /// <summary>
    /// 是否启用租户隔离（默认开启）
    /// </summary>
    public bool TenantIsolation { get; set; } = true;

    /// <summary>
    /// 是否启用审计日志（默认开启）
    /// </summary>
    public bool EnableAuditLog { get; set; } = true;

    /// <summary>
    /// 每分钟速率限制（每客户端，默认 600）
    /// </summary>
    public int RateLimitPerMinute { get; set; } = 600;

    /// <summary>
    /// 是否启用流式响应
    /// </summary>
    public bool EnableStreaming { get; set; } = true;

    /// <summary>
    /// Allow API key in query string (?apiKey=...). Default: false.
    /// Query strings commonly leak into access logs, proxy caches, browser history,
    /// and referrer headers — they are NOT a safe place for credentials. Enable this
    /// flag ONLY for transitional compatibility with legacy clients; prefer the
    /// X-Api-Key header or Authorization: Bearer. Every query-string key extraction
    /// emits a warning log entry when this is enabled.
    /// </summary>
    public bool AllowApiKeyInQuery { get; set; } = false;

    /// <summary>
    /// Hard cap on the rate-limit tracking dictionary. Prevents unbounded memory
    /// growth under key-space flood attacks (many unique IPs/keys per second).
    /// When the dict reaches this size, opportunistic eviction is forced on the
    /// next CheckRateLimit call regardless of the usual eviction interval.
    /// </summary>
    public int RateLimitTrackingMaxEntries { get; set; } = 10_000;
}
