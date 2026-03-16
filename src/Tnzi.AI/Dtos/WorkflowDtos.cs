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
    /// <summary>Status (running, completed, failed, paused, awaiting_approval)</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Completed step IDs</summary>
    public List<string> CompletedStepIds { get; set; } = [];
    /// <summary>Steps awaiting approval</summary>
    public List<string> StepsAwaitingApproval { get; set; } = [];
    /// <summary>Created time</summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>Last updated time</summary>
    public DateTime UpdatedAt { get; set; }
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
