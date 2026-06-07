namespace Tnzi.AI.Dtos;

/// <summary>
/// MCP Server 注册 DTO（输出形状）— 永不暴露明文或密文凭证
/// </summary>
public class McpServerRegistrationDto
{
    /// <summary>Registration id</summary>
    public Guid Id { get; set; }

    /// <summary>Display name</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>MCP server endpoint URL</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>Transport mode — stdio / sse / streamable-http</summary>
    public string Transport { get; set; } = string.Empty;

    /// <summary>Command (stdio transport only)</summary>
    public string? Command { get; set; }

    /// <summary>Arguments — JSON-serialized string array</summary>
    public string? Arguments { get; set; }

    /// <summary>Auth type — bearer / api-key / none / oauth</summary>
    public string? AuthType { get; set; }

    /// <summary>Whether an auth token is configured (true = ciphertext present)</summary>
    public bool HasAuthToken { get; set; }

    /// <summary>Priority — for ordering when multiple servers expose same tool</summary>
    public int Priority { get; set; }

    /// <summary>Whether enabled</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Description</summary>
    public string? Description { get; set; }

    /// <summary>Tags — JSON-serialized string array</summary>
    public string? Tags { get; set; }

    /// <summary>Creation time</summary>
    public DateTime CreationTime { get; set; }

    /// <summary>Last modification time</summary>
    public DateTime? LastModificationTime { get; set; }
}

/// <summary>
/// MCP Server 注册分页查询 DTO
/// </summary>
public class McpServerRegistrationQueryDto : PagedQueryDto
{
    /// <summary>按 Transport 过滤（可选）</summary>
    public string? Transport { get; set; }

    /// <summary>仅返回启用项（可选）</summary>
    public bool? IsEnabled { get; set; }

    /// <summary>关键字过滤（匹配 Name / Description）</summary>
    public string? Keyword { get; set; }
}

/// <summary>
/// MCP Server 注册创建请求 DTO
/// </summary>
public class CreateMcpServerRegistrationDto
{
    /// <summary>Display name</summary>
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>MCP server endpoint URL</summary>
    [Required]
    [StringLength(500)]
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>Transport mode — stdio / sse / streamable-http</summary>
    [Required]
    [StringLength(50)]
    public string Transport { get; set; } = string.Empty;

    /// <summary>Command (stdio transport only)</summary>
    [StringLength(500)]
    public string? Command { get; set; }

    /// <summary>Arguments — JSON-serialized string array</summary>
    public string? Arguments { get; set; }

    /// <summary>Auth token (plaintext) — encrypted at rest</summary>
    public string? AuthToken { get; set; }

    /// <summary>Auth type — bearer / api-key / none / oauth</summary>
    [StringLength(50)]
    public string? AuthType { get; set; }

    /// <summary>Priority</summary>
    public int Priority { get; set; }

    /// <summary>Whether enabled</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Description</summary>
    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>Tags — JSON-serialized string array</summary>
    public string? Tags { get; set; }
}

/// <summary>
/// MCP Server 注册更新请求 DTO — 字段可选；AuthToken 为 null 时保留现有密文
/// </summary>
public class UpdateMcpServerRegistrationDto
{
    /// <summary>Display name</summary>
    [StringLength(100)]
    public string? Name { get; set; }

    /// <summary>MCP server endpoint URL</summary>
    [StringLength(500)]
    public string? ServerUrl { get; set; }

    /// <summary>Transport mode</summary>
    [StringLength(50)]
    public string? Transport { get; set; }

    /// <summary>Command</summary>
    [StringLength(500)]
    public string? Command { get; set; }

    /// <summary>Arguments — JSON-serialized string array</summary>
    public string? Arguments { get; set; }

    /// <summary>
    /// Auth token (plaintext). null = 保持现有；空字符串 = 清除凭证；非空 = 加密替换
    /// </summary>
    public string? AuthToken { get; set; }

    /// <summary>Auth type</summary>
    [StringLength(50)]
    public string? AuthType { get; set; }

    /// <summary>Priority</summary>
    public int? Priority { get; set; }

    /// <summary>Whether enabled</summary>
    public bool? IsEnabled { get; set; }

    /// <summary>Description</summary>
    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>Tags — JSON-serialized string array</summary>
    public string? Tags { get; set; }
}

/// <summary>
/// MCP Server 连接测试结果 DTO
/// </summary>
public class McpServerTestResultDto
{
    /// <summary>Whether the probe succeeded</summary>
    public bool Success { get; set; }

    /// <summary>Optional message (error detail or status)</summary>
    public string? Message { get; set; }

    /// <summary>Probe latency in milliseconds</summary>
    public long LatencyMs { get; set; }
}
