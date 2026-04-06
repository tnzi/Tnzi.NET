namespace Tnzi.AI.Dtos;

/// <summary>
/// Agent 运行实例 DTO
/// </summary>
public class AgentRunDto
{
    /// <summary>Run ID</summary>
    public Guid Id { get; set; }
    /// <summary>Agent ID</summary>
    public Guid? AgentId { get; set; }
    /// <summary>Thread ID</summary>
    public Guid? ThreadId { get; set; }
    /// <summary>Workflow definition ID</summary>
    public Guid? WorkflowDefinitionId { get; set; }
    /// <summary>Workflow execution ID</summary>
    public string? WorkflowExecutionId { get; set; }
    /// <summary>Run status</summary>
    public AgentRunStatus Status { get; set; }
    /// <summary>Execution mode</summary>
    public AgentExecutionMode ExecutionMode { get; set; }
    /// <summary>Input summary</summary>
    public string InputSummary { get; set; } = string.Empty;
    /// <summary>Output summary</summary>
    public string? OutputSummary { get; set; }
    /// <summary>Total input tokens</summary>
    public int TotalInputTokens { get; set; }
    /// <summary>Total output tokens</summary>
    public int TotalOutputTokens { get; set; }
    /// <summary>Duration in milliseconds</summary>
    public long DurationMs { get; set; }
    /// <summary>Error message</summary>
    public string? Error { get; set; }
    /// <summary>Creation time</summary>
    public DateTime CreationTime { get; set; }
    /// <summary>Last modification time</summary>
    public DateTime? LastModificationTime { get; set; }
}

/// <summary>
/// Agent 运行节点 DTO
/// </summary>
public class AgentRunNodeDto
{
    /// <summary>Node ID</summary>
    public Guid Id { get; set; }
    /// <summary>Run ID</summary>
    public Guid RunId { get; set; }
    /// <summary>Node type (agent/review/approval/router/synthesize/...)</summary>
    public string NodeType { get; set; } = string.Empty;
    /// <summary>Node name (workflow step name)</summary>
    public string NodeName { get; set; } = string.Empty;
    /// <summary>Agent ID (for agent nodes)</summary>
    public Guid? AgentId { get; set; }
    /// <summary>Node status</summary>
    public AgentRunNodeStatus Status { get; set; }
    /// <summary>Input summary</summary>
    public string? InputSummary { get; set; }
    /// <summary>Output content</summary>
    public string? Output { get; set; }
    /// <summary>Input tokens</summary>
    public int InputTokens { get; set; }
    /// <summary>Output tokens</summary>
    public int OutputTokens { get; set; }
    /// <summary>Duration in milliseconds</summary>
    public long DurationMs { get; set; }
    /// <summary>Error message</summary>
    public string? Error { get; set; }
    /// <summary>Retry count</summary>
    public int RetryCount { get; set; }
    /// <summary>Execution order</summary>
    public int OrderIndex { get; set; }
    /// <summary>Creation time</summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// Agent 运行轨迹 DTO
/// </summary>
public class AgentRunTraceDto
{
    /// <summary>Trace ID</summary>
    public Guid Id { get; set; }
    /// <summary>Run ID</summary>
    public Guid RunId { get; set; }
    /// <summary>Node ID</summary>
    public Guid? NodeId { get; set; }
    /// <summary>Event type (llm_call, tool_call, handoff, route, review, approval, error)</summary>
    public string EventType { get; set; } = string.Empty;
    /// <summary>Event data (JSON)</summary>
    public string? EventData { get; set; }
    /// <summary>Duration in milliseconds</summary>
    public long DurationMs { get; set; }
    /// <summary>Creation time</summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// Agent 运行查询 DTO
/// </summary>
public class AgentRunQueryDto : PagedQueryDto
{
    protected override int DefaultPageSize => 20;

    /// <summary>Filter by agent ID</summary>
    public Guid? AgentId { get; set; }
    /// <summary>Filter by workflow definition ID</summary>
    public Guid? WorkflowDefinitionId { get; set; }
    /// <summary>Filter by status</summary>
    public AgentRunStatus? Status { get; set; }
    /// <summary>Filter by execution mode</summary>
    public AgentExecutionMode? ExecutionMode { get; set; }
    /// <summary>Filter by start time (inclusive)</summary>
    public DateTime? StartTime { get; set; }
    /// <summary>Filter by end time (inclusive)</summary>
    public DateTime? EndTime { get; set; }
}

/// <summary>
/// Agent 运行统计 DTO
/// </summary>
public class AgentRunStatsDto
{
    /// <summary>总运行数</summary>
    public int TotalRuns { get; set; }

    /// <summary>等待执行数</summary>
    public int PendingRuns { get; set; }

    /// <summary>执行中数</summary>
    public int RunningRuns { get; set; }

    /// <summary>等待审批数</summary>
    public int AwaitingApprovalRuns { get; set; }

    /// <summary>等待用户澄清数</summary>
    public int RequiresClarificationRuns { get; set; }

    /// <summary>执行完成数</summary>
    public int CompletedRuns { get; set; }

    /// <summary>执行失败数</summary>
    public int FailedRuns { get; set; }

    /// <summary>已取消数</summary>
    public int CancelledRuns { get; set; }

    /// <summary>总输入 Token</summary>
    public long TotalInputTokens { get; set; }

    /// <summary>总输出 Token</summary>
    public long TotalOutputTokens { get; set; }

    /// <summary>平均耗时（毫秒）</summary>
    public long AverageDurationMs { get; set; }

    /// <summary>成功率（0-1）</summary>
    public decimal SuccessRate { get; set; }

    /// <summary>统计时间</summary>
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 审批请求 DTO
/// </summary>
public class ApproveRunDto
{
    /// <summary>Approval comment</summary>
    [MaxLength(1000)]
    public string? Comment { get; set; }
}

/// <summary>
/// 拒绝请求 DTO
/// </summary>
public class RejectRunDto
{
    /// <summary>Rejection comment</summary>
    [MaxLength(1000)]
    public string? Comment { get; set; }
}

