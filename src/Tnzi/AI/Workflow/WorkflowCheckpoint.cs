namespace Tnzi.AI.Workflow;

/// <summary>
/// 工作流执行状态枚举
/// </summary>
public enum WorkflowExecutionStatus
{
    /// <summary>执行中</summary>
    Running,

    /// <summary>执行完成</summary>
    Completed,

    /// <summary>执行失败</summary>
    Failed,

    /// <summary>执行已取消</summary>
    Cancelled,

    /// <summary>已暂停</summary>
    Paused,

    /// <summary>等待人工审批</summary>
    AwaitingApproval,

    /// <summary>等待外部输入（通用中断：人工输入、外部事件等）</summary>
    AwaitingInput
}

/// <summary>
/// 工作流检查点模型 - 保存工作流执行的中间状态，支持断点续执行
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
    /// 执行状态
    /// </summary>
    public WorkflowExecutionStatus Status { get; set; } = WorkflowExecutionStatus.Running;

    /// <summary>
    /// 等待审批的步骤 ID 集合（HITL 场景下使用）
    /// </summary>
    [ExperimentalApi(Reason = "Workflow HITL is in preview")]
    public HashSet<string> StepsAwaitingApproval { get; set; } = [];

    /// <summary>
    /// 当前等待中的通用中断描述（序列化为 JSON 存储）
    /// </summary>
    [ExperimentalApi(Reason = "Generic workflow interrupt is in preview")]
    public string? PendingInterruptJson { get; set; }
}
