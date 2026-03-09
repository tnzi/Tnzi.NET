namespace Tnzi.AI.Domain;

/// <summary>
/// 工作流执行实例实体 — 持久化工作流的检查点状态
/// </summary>
public class WorkflowExecution : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

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
    /// 执行状态: running, completed, failed, paused, awaiting_approval
    /// </summary>
    public string Status { get; set; } = "running";

    /// <summary>
    /// 完成时间
    /// </summary>
    public DateTime? CompletedTime { get; set; }

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime UpdatedTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 等待审批的步骤 ID 列表（JSON 数组，HITL 场景下使用）
    /// </summary>
    public string StepsAwaitingApproval { get; set; } = "[]";
}
