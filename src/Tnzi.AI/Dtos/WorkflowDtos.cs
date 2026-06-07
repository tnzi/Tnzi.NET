namespace Tnzi.AI.Dtos;

/// <summary>
/// 工作流定义 DTO
/// </summary>
public class WorkflowDefinitionDto
{
    /// <summary>Workflow ID</summary>
    public Guid Id { get; set; }
    /// <summary>Workflow name</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Workflow description</summary>
    public string? Description { get; set; }
    /// <summary>Workflow steps</summary>
    public List<WorkflowStepDto> Steps { get; set; } = new();
    /// <summary>Execution mode</summary>
    public WorkflowExecutionMode ExecutionMode { get; set; } = WorkflowExecutionMode.Sequential;
    /// <summary>Whether enabled</summary>
    public bool IsEnabled { get; set; }
    /// <summary>Creation time</summary>
    public DateTime CreationTime { get; set; }
    /// <summary>Last modification time</summary>
    public DateTime? LastModificationTime { get; set; }
}

/// <summary>
/// 创建工作流 DTO
/// </summary>
public class CreateWorkflowDefinitionDto
{
    /// <summary>Workflow name</summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = null!;

    /// <summary>Workflow description</summary>
    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>Workflow steps</summary>
    [Required]
    public List<WorkflowStepDto> Steps { get; set; } = null!;

    /// <summary>Execution mode</summary>
    public WorkflowExecutionMode ExecutionMode { get; set; } = WorkflowExecutionMode.Sequential;

    /// <summary>Whether enabled</summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// 更新工作流 DTO
/// </summary>
public class UpdateWorkflowDefinitionDto
{
    /// <summary>Workflow name</summary>
    [MaxLength(200)]
    public string? Name { get; set; }

    /// <summary>Workflow description</summary>
    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>Workflow steps</summary>
    public List<WorkflowStepDto>? Steps { get; set; }

    /// <summary>Execution mode</summary>
    public WorkflowExecutionMode? ExecutionMode { get; set; }

    /// <summary>Whether enabled</summary>
    public bool? IsEnabled { get; set; }
}

/// <summary>
/// 工作流步骤 DTO
/// </summary>
public class WorkflowStepDto
{
    /// <summary>Step unique ID (required for DAG mode dependency references)</summary>
    public string? StepId { get; set; }

    /// <summary>Agent ID for this step</summary>
    public Guid? AgentId { get; set; }

    /// <summary>Step order (used in Sequential/Parallel mode)</summary>
    public int Order { get; set; }

    /// <summary>Predecessor step IDs (DAG mode: all dependencies must complete before this step runs)</summary>
    public List<string>? DependsOn { get; set; }

    /// <summary>Condition expression (empty = unconditional; supports {{stepId}} template variables)</summary>
    public string? Condition { get; set; }

    /// <summary>Custom provider (overrides Agent default)</summary>
    public string? Provider { get; set; }

    /// <summary>Custom model (overrides Agent default)</summary>
    public string? Model { get; set; }

    /// <summary>Custom instructions (overrides Agent default; supports {{stepId}} template variables)</summary>
    public string? Instructions { get; set; }

    /// <summary>Maximum retry attempts on failure (0 = no retry, default)</summary>
    [Range(0, 10)]
    public int MaxRetries { get; set; }

    /// <summary>Delay in seconds between retries (default: 2, uses exponential backoff)</summary>
    [Range(1, 300)]
    public int RetryDelaySeconds { get; set; } = 2;

    /// <summary>Step execution timeout in seconds (null = use Agent default)</summary>
    [Range(1, 3600)]
    public int? TimeoutSeconds { get; set; }

    /// <summary>Whether this step requires human approval before downstream steps can proceed</summary>
    public bool RequiresApproval { get; set; }

    /// <summary>
    /// Error handling policy when this node fails (Fail = stop workflow, Skip = skip node + continue, Continue = use error as output + continue).
    /// Defaults to Fail for backward compatibility.
    /// </summary>
    public NodeErrorPolicy OnError { get; set; } = NodeErrorPolicy.Fail;

    /// <summary>Extra configuration key-value pairs (e.g., nodeType, loopId)</summary>
    public Dictionary<string, string>? Configuration { get; set; }
}

/// <summary>
/// 工作流步骤执行结果 DTO（DAG 模式下逐步返回）
/// </summary>
public class WorkflowStepResultDto
{
    /// <summary>Step ID</summary>
    public string StepId { get; set; } = string.Empty;
    /// <summary>Step output</summary>
    public string Output { get; set; } = string.Empty;
    /// <summary>Whether this step was skipped (condition not met)</summary>
    public bool Skipped { get; set; }
}

/// <summary>
/// 运行工作流请求 DTO
/// </summary>
public class RunWorkflowRequestDto
{
    /// <summary>Workflow input</summary>
    [Required]
    [MaxLength(10000)]
    public string Input { get; set; } = null!;

    /// <summary>Optional user ID for quota reservation and settlement (tokens are reserved before execution and settled after completion)</summary>
    public Guid? UserId { get; set; }
}

/// <summary>
/// 工作流执行结果 DTO
/// </summary>
public class WorkflowExecutionResultDto
{
    /// <summary>Execution ID (set for resumable DAG runs)</summary>
    public string? ExecutionId { get; set; }
    /// <summary>Associated run ID when runtime tracking is enabled</summary>
    public Guid? RunId { get; set; }
    /// <summary>Workflow output (final output text)</summary>
    public string Output { get; set; } = string.Empty;
    /// <summary>Execution status</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Per-step results (DAG mode only; null for Sequential/Parallel)</summary>
    public List<WorkflowStepResultDto>? StepResults { get; set; }
}

/// <summary>
/// 工作流执行状态 DTO
/// </summary>
public class WorkflowExecutionStatusDto
{
    /// <summary>Execution ID</summary>
    public string ExecutionId { get; set; } = string.Empty;
    /// <summary>Status (running, completed, failed, cancelled, paused, awaiting_approval)</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Completed step IDs</summary>
    public List<string> CompletedStepIds { get; set; } = [];
    /// <summary>Steps awaiting approval</summary>
    public List<string> StepsAwaitingApproval { get; set; } = [];
    /// <summary>Created time</summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>Last updated time</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>待处理信号数量</summary>
    public int PendingSignalCount { get; set; }

    /// <summary>当前等待原因</summary>
    public string? CurrentWaitReason { get; set; }
}

/// <summary>
/// 工作流定义查询 DTO
/// </summary>
public class WorkflowDefinitionQueryDto : PagedQueryDto
{
    protected override int DefaultPageSize => 20;

    /// <summary>Keyword filter (name or description)</summary>
    public string? Keyword { get; set; }

    /// <summary>Filter by enabled status</summary>
    public bool? IsEnabled { get; set; }

    /// <summary>Filter by execution mode</summary>
    public WorkflowExecutionMode? ExecutionMode { get; set; }
}

/// <summary>
/// 审批/拒绝工作流步骤请求 DTO
/// </summary>
public class WorkflowStepApprovalDto
{
    /// <summary>Optional feedback or reason</summary>
    [MaxLength(2000)]
    public string? Feedback { get; set; }
}

/// <summary>
/// 克隆工作流请求 DTO
/// </summary>
public class CloneWorkflowRequestDto
{
    /// <summary>新工作流名称（为空则使用 "{原名} (Copy)"）</summary>
    [MaxLength(200)]
    public string? NewName { get; set; }
}

/// <summary>
/// 工作流执行历史查询 DTO
/// </summary>
public class WorkflowExecutionQueryDto : PagedQueryDto
{
    protected override int DefaultPageSize => 20;

    /// <summary>Filter by workflow definition ID</summary>
    public Guid? WorkflowDefinitionId { get; set; }

    /// <summary>Filter by status</summary>
    public WorkflowExecutionStatus? Status { get; set; }
}

/// <summary>
/// 工作流执行历史摘要 DTO
/// </summary>
public class WorkflowExecutionSummaryDto
{
    /// <summary>Database entity ID</summary>
    public Guid Id { get; set; }

    /// <summary>Execution ID (business ID)</summary>
    public string ExecutionId { get; set; } = string.Empty;

    /// <summary>Associated workflow definition ID</summary>
    public Guid? WorkflowDefinitionId { get; set; }

    /// <summary>Execution status</summary>
    public WorkflowExecutionStatus Status { get; set; }

    /// <summary>Number of completed steps</summary>
    public int CompletedStepCount { get; set; }

    /// <summary>Number of steps awaiting approval</summary>
    public int AwaitingApprovalCount { get; set; }

    /// <summary>Execution start time</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>Execution duration in milliseconds</summary>
    public long? DurationMs { get; set; }

    /// <summary>Creation time</summary>
    public DateTime CreationTime { get; set; }

    /// <summary>Completed time</summary>
    public DateTime? CompletedTime { get; set; }

    /// <summary>Last updated time</summary>
    public DateTime UpdatedTime { get; set; }

    /// <summary>待处理信号数量</summary>
    public int PendingSignalCount { get; set; }

    /// <summary>当前等待原因</summary>
    public string? CurrentWaitReason { get; set; }
}

/// <summary>
/// 工作流执行详情 DTO
/// </summary>
public class WorkflowExecutionDetailDto : WorkflowExecutionSummaryDto
{
    /// <summary>Initial input</summary>
    public string InitialInput { get; set; } = string.Empty;

    /// <summary>Completed step IDs</summary>
    public List<string> CompletedStepIds { get; set; } = [];

    /// <summary>Steps awaiting approval</summary>
    public List<string> StepsAwaitingApproval { get; set; } = [];

    /// <summary>Step outputs (stepId → output text)</summary>
    public Dictionary<string, string> StepOutputs { get; set; } = new();

    /// <summary>待处理执行信号</summary>
    [ExperimentalApi(Reason = "Workflow mailbox and signals are in preview")]
    public List<WorkflowExecutionSignal> PendingSignals { get; set; } = [];
}

/// <summary>
/// 工作流统计 DTO
/// </summary>
public class WorkflowStatsDto
{
    /// <summary>Total workflow definitions</summary>
    public int TotalWorkflows { get; set; }

    /// <summary>Enabled workflows</summary>
    public int EnabledWorkflows { get; set; }

    /// <summary>Disabled workflows</summary>
    public int DisabledWorkflows { get; set; }

    /// <summary>Count by execution mode</summary>
    public Dictionary<string, int> ByExecutionMode { get; set; } = new();

    /// <summary>Total executions</summary>
    public int TotalExecutions { get; set; }

    /// <summary>Running executions</summary>
    public int RunningExecutions { get; set; }

    /// <summary>Completed executions</summary>
    public int CompletedExecutions { get; set; }

    /// <summary>Failed executions</summary>
    public int FailedExecutions { get; set; }
}

/// <summary>
/// 工作流验证结果 DTO
/// </summary>
public class WorkflowValidationResultDto
{
    /// <summary>Whether the workflow is valid</summary>
    public bool IsValid { get; set; }

    /// <summary>Validation errors</summary>
    public List<string> Errors { get; set; } = [];

    /// <summary>Validation warnings</summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// 工作流流式事件 DTO
/// </summary>
public class WorkflowStreamEventDto
{
    /// <summary>Execution ID (set for resumable DAG runs)</summary>
    public string? ExecutionId { get; set; }

    /// <summary>事件类型（step/completed/error）</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>当前步骤 ID（仅单步骤事件时提供）</summary>
    public string? StepId { get; set; }

    /// <summary>工作流/步骤状态</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>当前事件输出内容</summary>
    public string? Output { get; set; }

    /// <summary>结构化步骤结果（DAG 或最终汇总时提供）</summary>
    public List<WorkflowStepResultDto>? StepResults { get; set; }

    /// <summary>是否为终止事件</summary>
    public bool IsDone { get; set; }

    /// <summary>错误消息（仅错误事件时提供）</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 工作流定义版本 DTO
/// </summary>
public class WorkflowDefinitionVersionDto
{
    /// <summary>Version entity ID</summary>
    public Guid Id { get; set; }

    /// <summary>Associated workflow definition ID</summary>
    public Guid WorkflowDefinitionId { get; set; }

    /// <summary>Version number</summary>
    public int VersionNumber { get; set; }

    /// <summary>Change description</summary>
    public string? ChangeDescription { get; set; }

    /// <summary>Full definition snapshot (JSON)</summary>
    public string? Definition { get; set; }

    /// <summary>Creation time</summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 工作流执行统计 DTO
/// </summary>
public class WorkflowExecutionStatsDto
{
    /// <summary>Workflow definition ID</summary>
    public Guid WorkflowId { get; set; }

    /// <summary>Total execution count</summary>
    public int TotalExecutions { get; set; }

    /// <summary>Average duration in milliseconds</summary>
    public double? AvgDurationMs { get; set; }

    /// <summary>Minimum duration in milliseconds</summary>
    public long? MinDurationMs { get; set; }

    /// <summary>Maximum duration in milliseconds</summary>
    public long? MaxDurationMs { get; set; }

    /// <summary>95th percentile duration in milliseconds</summary>
    public long? P95DurationMs { get; set; }

    /// <summary>Success rate (0.0 to 1.0)</summary>
    public double SuccessRate { get; set; }
}

/// <summary>
/// 更新工作流 DTO（扩展版本说明字段）
/// </summary>
public class UpdateWorkflowDefinitionWithVersionDto : UpdateWorkflowDefinitionDto
{
    /// <summary>Optional change description for version history</summary>
    [MaxLength(500)]
    public string? ChangeDescription { get; set; }
}

/// <summary>
/// 恢复工作流版本请求 DTO
/// </summary>
public class RestoreWorkflowVersionRequestDto
{
    /// <summary>Optional change description for the restore operation</summary>
    [MaxLength(500)]
    public string? ChangeDescription { get; set; }
}

/// <summary>
/// 工作流中断信息 DTO — 描述等待中的中断
/// </summary>
[ExperimentalApi(Reason = "Generic workflow interrupt is in preview")]
public class WorkflowInterruptDto
{
    /// <summary>中断发生的步骤 ID</summary>
    public string StepId { get; set; } = string.Empty;

    /// <summary>中断原因</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>中断类型（Approval, HumanInput, ExternalEvent）</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>请求的输入字段定义</summary>
    public Dictionary<string, object>? RequestedInput { get; set; }

    /// <summary>中断超时时间（秒）</summary>
    public double? TimeoutSeconds { get; set; }

    /// <summary>执行 ID</summary>
    public string ExecutionId { get; set; } = string.Empty;
}

/// <summary>
/// 恢复中断的工作流输入 DTO
/// </summary>
[ExperimentalApi(Reason = "Generic workflow interrupt is in preview")]
public class ResumeWorkflowInputDto
{
    /// <summary>中断步骤 ID（用于验证匹配）</summary>
    [Required]
    public string StepId { get; set; } = null!;

    /// <summary>外部提供的输入数据</summary>
    [Required]
    public Dictionary<string, object> Input { get; set; } = null!;
}
