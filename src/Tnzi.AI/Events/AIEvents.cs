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
/// 用量上限拦下一轮时发布（5 小时窗口或每周窗口到上限）。可用于告警、通知管理员。
/// </summary>
/// <remarks>
/// 只在真的到上限时发布：计量子系统自身的故障（<c>AI_USAGE_LIMIT_CHECK_FAILED</c>）不发，那会让超限统计里混进基础设施故障。
/// </remarks>
public class UsageLimitReachedEvent : EventBase
{
    /// <summary>User ID</summary>
    public Guid UserId { get; set; }
    /// <summary>The window that is at its limit.</summary>
    public UsageLimitWindow Window { get; set; }
    /// <summary>The window's limit (USD).</summary>
    public decimal LimitUsd { get; set; }
    /// <summary>What the window has used (USD).</summary>
    public decimal UsedUsd { get; set; }
    /// <summary>When the window resets (UTC); null when the limit is zero.</summary>
    public DateTime? ResetsAt { get; set; }
}

/// <summary>
/// 一个窗口的用量越过接近上限的提示阈值（<c>AI:UsageLimits:WarningThreshold</c>）或上限本身时发布，每次越过各发一次。
/// 可用于发送预警通知。
/// </summary>
public class UsageLimitThresholdReachedEvent : EventBase
{
    /// <summary>User ID</summary>
    public Guid UserId { get; set; }
    /// <summary>The window that crossed the threshold.</summary>
    public UsageLimitWindow Window { get; set; }
    /// <summary>True when the window reached its limit (the next turn is refused); false for the warning threshold.</summary>
    public bool LimitReached { get; set; }
    /// <summary>Used share of the limit (0-100, may exceed 100).</summary>
    public decimal UsedPercent { get; set; }
    /// <summary>When the window resets (UTC).</summary>
    public DateTime? ResetsAt { get; set; }
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
/// Agent 运行取消事件 - 运行被调用方或 kill_agent / 管理端 cancel 取消时发布（与 AgentRunCompletedEvent /
/// AgentRunFailedEvent 互斥）。超时不算取消：那是运行没干完，走 AgentRunFailedEvent。
/// </summary>
public class AgentRunCancelledEvent : EventBase
{
    /// <summary>Run ID</summary>
    public Guid? RunId { get; set; }
    /// <summary>Agent ID (null for agent-less chat)</summary>
    public Guid? AgentId { get; set; }
    /// <summary>User ID</summary>
    public Guid? UserId { get; set; }
    /// <summary>Thread ID</summary>
    public Guid? ThreadId { get; set; }
    /// <summary>Why the run was cancelled (as recorded on the run)</summary>
    public string Reason { get; set; } = string.Empty;
    /// <summary>Execution duration in milliseconds before cancellation</summary>
    public long DurationMs { get; set; }
    /// <summary>Whether this was a streaming call</summary>
    public bool IsStreaming { get; set; }
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
