namespace Tnzi.AI.Events;

/// <summary>
/// Agent 运行完成事件 — 每次 AI 运行（chat/agent/workflow）完成时发布。
/// 可用于外部分析、通知、Webhook 等辅助操作。
/// </summary>
public class AgentRunCompletedEvent : EventBase
{
    /// <summary>Run ID</summary>
    public Guid? RunId { get; set; }
    /// <summary>Thread ID</summary>
    public Guid? ThreadId { get; set; }
    /// <summary>Agent ID (null for agent-less chat)</summary>
    public Guid? AgentId { get; set; }
    /// <summary>User ID</summary>
    public Guid? UserId { get; set; }
    /// <summary>AI provider used</summary>
    public string? Provider { get; set; }
    /// <summary>Model used</summary>
    public string? Model { get; set; }
    /// <summary>Total tokens consumed (input + output)</summary>
    public long TotalTokens { get; set; }
    /// <summary>Execution duration in milliseconds</summary>
    public long DurationMs { get; set; }
    /// <summary>Run status (Completed, Failed, Cancelled)</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Finish reason (stop, guardrail_rejected, etc.)</summary>
    public string? FinishReason { get; set; }
    /// <summary>Whether this was a streaming call</summary>
    public bool IsStreaming { get; set; }
}

/// <summary>
/// 配额超限事件 — 当用户配额不足被拒绝时发布。
/// 可用于告警、通知管理员、自动扩容等。
/// </summary>
public class QuotaExceededEvent : EventBase
{
    /// <summary>User ID</summary>
    public Guid UserId { get; set; }
    /// <summary>Remaining daily quota</summary>
    public long RemainingDailyQuota { get; set; }
    /// <summary>Remaining monthly quota</summary>
    public long RemainingMonthlyQuota { get; set; }
    /// <summary>Estimated tokens requested</summary>
    public long EstimatedTokens { get; set; }
    /// <summary>Rejection reason</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 配额预警阈值触达事件 — 当用户配额使用率达到预警或严重阈值时发布。
/// 可用于发送预警通知、Dashboard 展示等。
/// </summary>
public class QuotaThresholdReachedEvent : EventBase
{
    /// <summary>User ID</summary>
    public Guid UserId { get; set; }
    /// <summary>Warning level (Warning or Critical)</summary>
    public string Level { get; set; } = string.Empty;
    /// <summary>Daily usage percentage (0-1)</summary>
    public decimal DailyUsagePercentage { get; set; }
    /// <summary>Monthly usage percentage (0-1)</summary>
    public decimal MonthlyUsagePercentage { get; set; }
    /// <summary>Remaining daily quota</summary>
    public long RemainingDailyQuota { get; set; }
    /// <summary>Remaining monthly quota</summary>
    public long RemainingMonthlyQuota { get; set; }
}

/// <summary>
/// Thread 删除事件 — 当会话线程被删除时发布。
/// 用于触发级联清理（消息、运行记录、产物、沙箱资源、IM 映射等）。
/// </summary>
public class ThreadDeletedEvent : EventBase
{
    /// <summary>Thread ID</summary>
    public Guid ThreadId { get; set; }
    /// <summary>User ID (thread creator)</summary>
    public Guid? UserId { get; set; }
    /// <summary>Agent ID (if thread is bound to an agent)</summary>
    public Guid? AgentId { get; set; }
}

/// <summary>
/// Guardrail 拦截事件 — 当输入或输出被 Guardrail 拒绝时发布。
/// 可用于安全监控、审计、报表等。
/// </summary>
public class GuardrailRejectionEvent : EventBase
{
    /// <summary>User ID</summary>
    public Guid? UserId { get; set; }
    /// <summary>Thread ID</summary>
    public Guid? ThreadId { get; set; }
    /// <summary>Guardrail name that triggered the rejection</summary>
    public string GuardrailName { get; set; } = string.Empty;
    /// <summary>Rejection reason</summary>
    public string Reason { get; set; } = string.Empty;
    /// <summary>Whether this was an input or output guardrail</summary>
    public string Direction { get; set; } = string.Empty; // "input" or "output"
}
