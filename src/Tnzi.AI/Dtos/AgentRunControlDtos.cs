namespace Tnzi.AI.Dtos;

/// <summary>
/// AgentRun 运行时控制状态 DTO
/// </summary>
public class AgentRunControlStateDto
{
    /// <summary>Run ID</summary>
    public Guid RunId { get; set; }

    /// <summary>Agent ID</summary>
    public Guid? AgentId { get; set; }

    /// <summary>Thread ID</summary>
    public Guid? ThreadId { get; set; }

    /// <summary>Workflow definition ID</summary>
    public Guid? WorkflowDefinitionId { get; set; }

    /// <summary>Workflow execution ID</summary>
    public string? WorkflowExecutionId { get; set; }

    /// <summary>Execution mode</summary>
    public AgentExecutionMode ExecutionMode { get; set; }

    /// <summary>Run status</summary>
    public AgentRunStatus Status { get; set; }

    /// <summary>Workflow 原始状态（如可用）</summary>
    public string? WorkflowStatus { get; set; }

    /// <summary>Input summary</summary>
    public string InputSummary { get; set; } = string.Empty;

    /// <summary>Output summary</summary>
    public string? OutputSummary { get; set; }

    /// <summary>Error</summary>
    public string? Error { get; set; }

    /// <summary>Waiting approval node names</summary>
    public List<string> AwaitingApprovalNodeNames { get; set; } = [];

    /// <summary>Failed node names</summary>
    public List<string> FailedNodeNames { get; set; } = [];

    /// <summary>Pending workflow interrupt</summary>
    public WorkflowInterruptDto? PendingInterrupt { get; set; }

    /// <summary>Can cancel</summary>
    public bool CanCancel { get; set; }

    /// <summary>Can resume</summary>
    public bool CanResume { get; set; }

    /// <summary>Can send input</summary>
    public bool CanSendInput { get; set; }

    /// <summary>Need user action</summary>
    public bool RequiresUserAction { get; set; }

    /// <summary>Whether wait should stop at this state</summary>
    public bool IsTerminal { get; set; }

    /// <summary>Creation time</summary>
    public DateTime CreationTime { get; set; }

    /// <summary>Last modification time</summary>
    public DateTime? LastModificationTime { get; set; }
}

/// <summary>
/// Wait AgentRun 输入参数
/// </summary>
public class WaitAgentRunInput
{
    /// <summary>Maximum wait seconds</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Polling interval milliseconds</summary>
    public int PollIntervalMs { get; set; } = 1000;
}

/// <summary>
/// Wait AgentRun 返回结果
/// </summary>
public class AgentRunWaitResultDto
{
    /// <summary>Current state when wait exits</summary>
    public AgentRunControlStateDto State { get; set; } = null!;

    /// <summary>Whether wait ended by timeout</summary>
    public bool TimedOut { get; set; }

    /// <summary>Number of polls performed</summary>
    public int PollCount { get; set; }

    /// <summary>Total waited milliseconds</summary>
    public long WaitedMs { get; set; }
}

/// <summary>
/// 发送额外输入到 AgentRun
/// </summary>
public class SendAgentRunInput
{
    /// <summary>Free-form user message for resume</summary>
    public string? Message { get; set; }

    /// <summary>Workflow interrupt step ID (optional)</summary>
    public string? WorkflowStepId { get; set; }

    /// <summary>Workflow structured input</summary>
    public Dictionary<string, object>? WorkflowInput { get; set; }
}

/// <summary>
/// 启动后台 AgentRun 输入
/// </summary>
public class SpawnAgentRunInput
{
    /// <summary>首条用户消息</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>已存在的 Agent ID</summary>
    public Guid? AgentId { get; set; }

    /// <summary>子 Agent 类型模板名称</summary>
    public string? SubAgentType { get; set; }

    /// <summary>直接指定 Provider</summary>
    public string? Provider { get; set; }

    /// <summary>直接指定 Model</summary>
    public string? Model { get; set; }

    /// <summary>直接指定工具组</summary>
    public List<string>? ToolGroups { get; set; }

    /// <summary>关联线程 ID</summary>
    public Guid? ThreadId { get; set; }

    /// <summary>当前用户 ID</summary>
    public Guid? UserId { get; set; }

    /// <summary>附加元数据</summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// AgentRun 简洁列表项 DTO（用于 list_agent_runs 工具）
/// </summary>
public class AgentRunListItemDto
{
    /// <summary>Run ID</summary>
    public Guid RunId { get; set; }

    /// <summary>Agent ID</summary>
    public Guid? AgentId { get; set; }

    /// <summary>Run status</summary>
    public AgentRunStatus Status { get; set; }

    /// <summary>Input summary</summary>
    public string InputSummary { get; set; } = string.Empty;

    /// <summary>Creation time</summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 子 Agent 类型 DTO
/// </summary>
public class SubAgentTypeDto
{
    /// <summary>Name</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Description</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Tool groups</summary>
    public List<string> ToolGroups { get; set; } = [];

    /// <summary>Excluded tool groups</summary>
    public List<string> ExcludedToolGroups { get; set; } = [];

    /// <summary>Max turns</summary>
    public int MaxTurns { get; set; }

    /// <summary>Default instructions</summary>
    public string? Instructions { get; set; }

    /// <summary>Default model</summary>
    public string? DefaultModel { get; set; }

    /// <summary>Default approval mode</summary>
    public ToolApprovalMode? DefaultApprovalMode { get; set; }

    /// <summary>Capability tags</summary>
    public List<string> CapabilityTags { get; set; } = [];
}
