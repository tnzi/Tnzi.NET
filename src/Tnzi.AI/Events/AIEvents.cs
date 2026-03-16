using Tnzi.EventBus;

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
