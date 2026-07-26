namespace Tnzi.AI.Entities;

/// <summary>
/// MCP Server 注册实体 - 数据库驱动的外部 MCP Server 客户端注册条目
/// </summary>
/// <remarks>
/// 该实体用于编录 Tnzi 可连接的外部 MCP Server（client-side registration），
/// 与 <see cref="Tnzi.AI.Mcp.Options.McpServerOptions"/> 描述的 server-hosting 配置无关。
/// 凭证（AuthToken/ApiKey）通过 <c>IDataProtectionProvider</c> 加密存储于
/// <see cref="AuthTokenEncrypted"/>。
///
/// 运行时绑定：启用的注册条目由 <c>IMcpServerCatalog</c> 物化为 <c>McpServerConfig</c>，
/// 与 <c>AI:Mcp:Servers</c> 部署配置合并后供 MCP 客户端运行时消费（同名时 DB 条目优先）。
///
/// 信任边界：注册表条目仅允许 HTTP 系 transport（sse / streamable-http / http）。
/// stdio（本机子进程）只能通过部署配置（AI:Mcp options）声明 - 运行时录入
/// 启动命令等价于远程命令执行，故注册表层面禁止。
/// </remarks>
public class McpServerRegistration : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// Single source of truth for the IDataProtectionProvider purpose string used to
    /// encrypt <see cref="AuthTokenEncrypted"/>. McpServerRegistryService (writer) and
    /// McpServerCatalog (reader) both reference this constant so a rename cannot
    /// silently desynchronize the two sides.
    /// </summary>
    public const string AuthTokenProtectorPurpose = "Tnzi.AI.Mcp.ServerCredential";

    /// <summary>
    /// Display name (unique among non-deleted rows; partitioned per tenant when multi-tenancy is enabled)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// MCP server endpoint URL - 远端 HTTP/SSE 端点（必须为绝对 http(s) URI）
    /// </summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Transport mode: "sse" / "streamable-http" / "http"（stdio 不允许，见类注释）
    /// </summary>
    public string Transport { get; set; } = string.Empty;

    /// <summary>
    /// Auth token / API key ciphertext - 由 IDataProtectionProvider 加密。
    /// [AuditIgnore]：虽为密文，仍不进实体级审计（缩小密文外泄面）。
    /// </summary>
    [AuditIgnore]
    public string? AuthTokenEncrypted { get; set; }

    /// <summary>
    /// Auth type: "bearer" / "api-key" / "none" / "oauth"
    /// </summary>
    public string? AuthType { get; set; }

    /// <summary>
    /// Priority - 多个 MCP Server 暴露同名工具时的排序权重
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
    /// Tags - JSON-serialized string array, used for categorization / filtering
    /// </summary>
    public string? Tags { get; set; }
}
