namespace Tnzi.AI.Workflow;

/// <summary>
/// 工作流执行选项 - 控制检查点和断点续执行行为
/// </summary>
[ExperimentalApi(Reason = "Workflow execution options are in preview")]
public class WorkflowExecutionOptions
{
    /// <summary>
    /// 执行实例 ID（为 null 时自动生成）
    /// </summary>
    public string? ExecutionId { get; set; }

    /// <summary>
    /// 关联的工作流定义 ID（用于 Run 追踪和恢复）
    /// </summary>
    public Guid? WorkflowDefinitionId { get; set; }

    /// <summary>
    /// 关联的 AgentRun ID（恢复已有 Run 时使用）
    /// </summary>
    public Guid? RunId { get; set; }

    /// <summary>
    /// 是否从检查点恢复执行
    /// </summary>
    public bool Resume { get; set; }

    /// <summary>
    /// 检查点存储实例（为 null 时不保存检查点）
    /// </summary>
    public IWorkflowCheckpointStore? CheckpointStore { get; set; }

    /// <summary>
    /// 工作流中断处理器（为 null 时带 RequiresApproval 的步骤将保存检查点并暂停）
    /// </summary>
    [ExperimentalApi(Reason = "Workflow HITL is in preview")]
    public IWorkflowInterruptHandler? InterruptHandler { get; set; }

    /// <summary>
    /// 恢复时的目标步骤 ID（与 ResumeData 配合使用）
    /// </summary>
    [ExperimentalApi(Reason = "Generic workflow interrupt is in preview")]
    public string? ResumeStepId { get; set; }

    /// <summary>
    /// 恢复时的外部输入数据（传递给中断步骤的 ResumeData）
    /// </summary>
    [ExperimentalApi(Reason = "Generic workflow interrupt is in preview")]
    public Dictionary<string, object>? ResumeData { get; set; }
}
