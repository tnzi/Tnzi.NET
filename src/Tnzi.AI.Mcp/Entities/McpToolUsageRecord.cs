namespace Tnzi.AI.Entities;

/// <summary>
/// MCP 工具使用记录 — 记录每次工具调用的耗时、成功/失败、错误信息等
/// </summary>
public class McpToolUsageRecord : CreationAuditedEntity<long>
{
    /// <summary>
    /// 租户标识（分析维度字段，<b>非隔离边界</b>）。
    /// <para>
    /// 此字段刻意不实现 <c>IMultiTenant</c>：MCP 工具调用的审计写入发生在根 scope、
    /// 无租户上下文，框架的租户过滤器/自动填充对它没有意义。值（若有）来自不可信的
    /// <c>X-Tenant-Id</c> 请求头，仅用于统计分组，绝不可作为数据隔离或授权依据。
    /// </para>
    /// </summary>
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
