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

    /// <summary>Optional user ID for quota check (workflow run does not deduct tokens; only pre-check when provided)</summary>
    public Guid? UserId { get; set; }
}

/// <summary>
/// 工作流执行结果 DTO
/// </summary>
public class WorkflowExecutionResultDto
{
    /// <summary>Workflow output (final output text)</summary>
    public string Output { get; set; } = string.Empty;
    /// <summary>Execution status</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Per-step results (DAG mode only; null for Sequential/Parallel)</summary>
    public List<WorkflowStepResultDto>? StepResults { get; set; }
}
