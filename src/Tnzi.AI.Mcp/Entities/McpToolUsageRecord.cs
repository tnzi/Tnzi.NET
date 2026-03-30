namespace Tnzi.AI.Entities;

/// <summary>
/// MCP 工具使用记录 — 记录每次工具调用的耗时、成功/失败、错误信息等
/// </summary>
public class McpToolUsageRecord : CreationAuditedEntity<long>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 工具名称
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// 调用耗时（毫秒）
    /// </summary>
    public long DurationMs { get; set; }

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// 错误信息（失败时记录）
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 调用方 API Key ID（用于追踪调用来源）
    /// </summary>
    public string? CallerApiKeyId { get; set; }
}
