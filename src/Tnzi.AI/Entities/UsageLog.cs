namespace Tnzi.AI.Entities;

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
    /// 预估成本（美元），根据模型成本率和 Token 用量在写入时计算
    /// </summary>
    public decimal? EstimatedCostUsd { get; set; }

    /// <summary>
    /// 缓存命中的输入 Token 数（Anthropic/OpenAI prompt caching）
    /// </summary>
    public int CachedInputTokens { get; set; }

    /// <summary>
    /// 缓存创建的输入 Token 数（首次缓存写入的 Token，Anthropic cache_creation_input_tokens）
    /// </summary>
    public int CacheCreationTokens { get; set; }

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
