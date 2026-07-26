namespace Tnzi.AI.Events;

/// <summary>
/// Agent 运行完成事件 - 每次 AI 运行（chat/agent/workflow）完成时发布。
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
/// 配额超限事件 - 当用户配额不足被拒绝时发布。
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
/// 配额预警阈值触达事件 - 当用户配额使用率达到预警或严重阈值时发布。
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
/// Thread 删除事件 - 当会话线程被删除时发布。
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
/// Guardrail 拦截事件 - 当输入或输出被 Guardrail 拒绝时发布。
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

/// <summary>
/// Agent 运行开始事件 - 每次 AI 运行开始执行时发布（含 Agent 解析完成后、进入策略执行前）。
/// </summary>
public class AgentRunStartedEvent : EventBase
{
    /// <summary>Run ID</summary>
    public Guid? RunId { get; set; }
    /// <summary>Agent ID (null for agent-less chat)</summary>
    public Guid? AgentId { get; set; }
    /// <summary>User ID</summary>
    public Guid? UserId { get; set; }
    /// <summary>Thread ID</summary>
    public Guid? ThreadId { get; set; }
    /// <summary>AI provider used</summary>
    public string? Provider { get; set; }
    /// <summary>Model used</summary>
    public string? Model { get; set; }
    /// <summary>Whether this is a streaming call</summary>
    public bool IsStreaming { get; set; }
    /// <summary>Execution mode (Single, Handoff, AgentAsTools, Router)</summary>
    public string ExecutionMode { get; set; } = string.Empty;
}

/// <summary>
/// 工具调用执行完成事件 - 每次 AI 工具调用完成后发布（成功或失败）。可用于监控、审计、性能分析。
/// </summary>
public class ToolCallExecutedEvent : EventBase
{
    /// <summary>Run ID</summary>
    public Guid? RunId { get; set; }
    /// <summary>Thread ID</summary>
    public Guid? ThreadId { get; set; }
    /// <summary>Name of the tool that was called</summary>
    public string ToolName { get; set; } = string.Empty;
    /// <summary>Tool group the tool belongs to (null if ungrouped)</summary>
    public string? ToolGroup { get; set; }
    /// <summary>Execution duration in milliseconds</summary>
    public long DurationMs { get; set; }
    /// <summary>Whether the tool call succeeded</summary>
    public bool Success { get; set; }
    /// <summary>Error message if the call failed (null on success)</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 子 Agent 启动事件 - 当 SubAgentExecutionService.SpawnAsync 成功创建并启动子 Agent 时发布。
/// </summary>
public class SubAgentSpawnedEvent : EventBase
{
    /// <summary>Parent run ID that spawned this sub-agent</summary>
    public Guid? ParentRunId { get; set; }
    /// <summary>Child run ID of the newly spawned sub-agent</summary>
    public Guid ChildRunId { get; set; }
    /// <summary>Type identifier of the sub-agent (e.g. general-purpose, bash, researcher)</summary>
    public string? SubAgentType { get; set; }
    /// <summary>Agent ID used for the sub-agent run</summary>
    public Guid? AgentId { get; set; }
    /// <summary>Thread ID the sub-agent run belongs to</summary>
    public Guid? ThreadId { get; set; }
}

/// <summary>
/// 工具审批请求事件 - 当 ApprovalToolWrapper 进入 Ask 流程时发布。可用于审计、审批追踪、控制面展示。
/// </summary>
public class ApprovalRequestedEvent : EventBase
{
    /// <summary>Run ID that triggered the approval request</summary>
    public Guid? RunId { get; set; }
    /// <summary>Name of the tool pending approval</summary>
    public string ToolName { get; set; } = string.Empty;
    /// <summary>Approval decision outcome (Ask, Allow, Deny)</summary>
    public string Decision { get; set; } = string.Empty;
    /// <summary>Reason for the decision (null if not provided)</summary>
    public string? Reason { get; set; }
    /// <summary>User ID associated with the run</summary>
    public Guid? UserId { get; set; }
}

/// <summary>
/// Agent 运行失败事件 - 当 AI 运行因异常终止时发布（与 AgentRunCompletedEvent 互斥）。可用于告警、自动重试策略判定、运维监控。
/// </summary>
public class AgentRunFailedEvent : EventBase
{
    /// <summary>Run ID</summary>
    public Guid? RunId { get; set; }
    /// <summary>Agent ID (null for agent-less chat)</summary>
    public Guid? AgentId { get; set; }
    /// <summary>User ID</summary>
    public Guid? UserId { get; set; }
    /// <summary>Thread ID</summary>
    public Guid? ThreadId { get; set; }
    /// <summary>Error message describing the failure</summary>
    public string ErrorMessage { get; set; } = string.Empty;
    /// <summary>Full type name of the exception (null if not exception-based)</summary>
    public string? ExceptionType { get; set; }
    /// <summary>Execution duration in milliseconds before failure</summary>
    public long DurationMs { get; set; }
    /// <summary>Whether this was a streaming call</summary>
    public bool IsStreaming { get; set; }
}
