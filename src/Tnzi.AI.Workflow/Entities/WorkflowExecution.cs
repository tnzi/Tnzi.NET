namespace Tnzi.AI.Workflow.Entities;

/// <summary>
/// 工作流执行实例实体 — 持久化工作流的检查点状态
/// </summary>
public class WorkflowExecution : CreationAuditedEntity<Guid>, IMultiTenant, IConcurrencyStamp
{
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 乐观并发标记（每次更新时由框架 AuditPropertyHelper 自动变更）
    /// </summary>
    public string ConcurrencyStamp { get; set; } = string.Empty;

    /// <summary>
    /// 执行实例唯一标识（业务 ID，区别于数据库主键 Id）
    /// </summary>
    public string ExecutionId { get; set; } = string.Empty;

    /// <summary>
    /// 关联的工作流定义 ID（可选）
    /// </summary>
    public Guid? WorkflowDefinitionId { get; set; }

    /// <summary>
    /// 工作流初始输入
    /// </summary>
    public string InitialInput { get; set; } = string.Empty;

    /// <summary>
    /// 已完成的步骤 ID 列表（JSON 数组）
    /// </summary>
    public string CompletedSteps { get; set; } = "[]";

    /// <summary>
    /// 各步骤的输出（JSON 对象：Key → StepId, Value → 输出文本）
    /// </summary>
    public string StepOutputs { get; set; } = "{}";

    /// <summary>
    /// 执行状态
    /// </summary>
    public WorkflowExecutionStatus Status { get; set; } = WorkflowExecutionStatus.Running;

    /// <summary>
    /// 开始执行时间
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// 完成时间
    /// </summary>
    public DateTime? CompletedTime { get; set; }

    /// <summary>
    /// 执行耗时（毫秒）
    /// </summary>
    public long? DurationMs { get; set; }

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime UpdatedTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 待处理信号数量（输入/取消/恢复等）
    /// </summary>
    public int PendingSignalCount { get; set; }

    /// <summary>
    /// 当前等待原因（approval / input / external_signal 等）
    /// </summary>
    public string? CurrentWaitReason { get; set; }

    /// <summary>
    /// 当前等待中的通用中断（JSON）
    /// </summary>
    public string? PendingInterruptJson { get; set; }

    /// <summary>
    /// 待处理执行信号（JSON 数组）
    /// </summary>
    public string PendingSignalsJson { get; set; } = "[]";

    /// <summary>
    /// 等待审批的步骤 ID 列表（JSON 数组，HITL 场景下使用）
    /// </summary>
    public string StepsAwaitingApproval { get; set; } = "[]";
}
