namespace Tnzi.AI.Workflow;

/// <summary>
/// 工作流执行选项 — 控制检查点和断点续执行行为
/// </summary>
[ExperimentalApi(Reason = "Workflow execution options are in preview")]
public class WorkflowExecutionOptions
{
    /// <summary>
    /// 执行实例 ID（为 null 时自动生成）
    /// </summary>
    public string? ExecutionId { get; set; }

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
}
