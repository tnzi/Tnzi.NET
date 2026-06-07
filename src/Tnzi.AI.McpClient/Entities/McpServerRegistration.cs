namespace Tnzi.AI.Entities;

/// <summary>
/// MCP Server 注册实体 — 数据库驱动的外部 MCP Server 客户端注册条目
/// </summary>
/// <remarks>
/// 该实体用于编录 Tnzi 可连接的外部 MCP Server（client-side registration），
/// 与 <see cref="Tnzi.AI.Mcp.Options.McpServerOptions"/> 描述的 server-hosting 配置无关。
/// 凭证（AuthToken/ApiKey）通过 <c>IDataProtectionProvider</c> 加密存储于
/// <see cref="AuthTokenEncrypted"/>。
///
/// 注意：本阶段（Phase 5 backend prereq）仅提供 CRUD 表面，MCP 运行时加载路径
/// 仍读取 <c>McpServerOptions</c> 配置；实体 → 运行时绑定为后续工作。
/// </remarks>
public class McpServerRegistration : FullAuditedEntity<Guid>
{
    /// <summary>
    /// Display name (unique among non-deleted rows)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// MCP server endpoint URL — 远端 HTTP/SSE 端点；stdio 传输时也可填占位 URL
    /// </summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Transport mode: "stdio" / "sse" / "streamable-http"
    /// </summary>
    public string Transport { get; set; } = string.Empty;

    /// <summary>
    /// Command to launch (stdio transport only) — e.g. "node" or "python"
    /// </summary>
    public string? Command { get; set; }

    /// <summary>
    /// Arguments for the stdio command — JSON-serialized string array
    /// </summary>
    public string? Arguments { get; set; }

    /// <summary>
    /// Auth token / API key ciphertext — 由 IDataProtectionProvider 加密
    /// </summary>
    public string? AuthTokenEncrypted { get; set; }

    /// <summary>
    /// Auth type: "bearer" / "api-key" / "none" / "oauth"
    /// </summary>
    public string? AuthType { get; set; }

    /// <summary>
    /// Priority — 多个 MCP Server 暴露同名工具时的排序权重
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Whether enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Description (nullable)
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Tags — JSON-serialized string array, used for categorization / filtering
    /// </summary>
    public string? Tags { get; set; }
}
