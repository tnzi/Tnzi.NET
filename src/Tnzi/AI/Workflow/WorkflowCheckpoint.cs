namespace Tnzi.AI.Workflow;

/// <summary>
/// 工作流检查点模型 — 保存工作流执行的中间状态，支持断点续执行
/// </summary>
public class WorkflowCheckpoint
{
    /// <summary>
    /// 执行实例唯一标识
    /// </summary>
    public string ExecutionId { get; set; } = string.Empty;

    /// <summary>
    /// 已完成的步骤 ID 集合
    /// </summary>
    public HashSet<string> CompletedStepIds { get; set; } = [];

    /// <summary>
    /// 各步骤的输出（Key: StepId, Value: 步骤输出）
    /// </summary>
    public Dictionary<string, WorkflowStepOutput> StepOutputs { get; set; } = new();

    /// <summary>
    /// 工作流初始输入
    /// </summary>
    public string InitialInput { get; set; } = string.Empty;

    /// <summary>
    /// 检查点创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 检查点最后更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// 执行状态: running, completed, failed, paused, awaiting_approval
    /// </summary>
    public string Status { get; set; } = "running";

    /// <summary>
    /// 等待审批的步骤 ID 集合（HITL 场景下使用）
    /// </summary>
    [ExperimentalApi(Reason = "Workflow HITL is in preview")]
    public HashSet<string> StepsAwaitingApproval { get; set; } = [];
}
