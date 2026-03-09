namespace Tnzi.AI.Domain;

/// <summary>
/// AI 使用日志实体 - 记录 API 调用和 Token 使用
/// </summary>
public class UsageLog : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 关联的 Agent ID（可选）
    /// </summary>
    public Guid? AgentId { get; set; }

    /// <summary>
    /// 关联的线程 ID（可选）
    /// </summary>
    public Guid? ThreadId { get; set; }

    /// <summary>
    /// 提供商名称
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// 模型名称
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// 操作类型: Chat, ChatStreaming, AgentRun, WorkflowRun
    /// </summary>
    public string OperationType { get; set; } = string.Empty;

    /// <summary>
    /// 输入 Token 数
    /// </summary>
    public int InputTokens { get; set; }

    /// <summary>
    /// 输出 Token 数
    /// </summary>
    public int OutputTokens { get; set; }

    /// <summary>
    /// 总 Token 数
    /// </summary>
    public int TotalTokens { get; set; }

    /// <summary>
    /// 请求耗时（毫秒）
    /// </summary>
    public long DurationMs { get; set; }

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// 错误信息（如果失败）
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 请求 IP 地址
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// 用户代理
    /// </summary>
    public string? UserAgent { get; set; }
}
