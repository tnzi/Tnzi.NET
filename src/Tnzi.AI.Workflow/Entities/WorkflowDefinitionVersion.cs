namespace Tnzi.AI.Workflow.Entities;

/// <summary>
/// 工作流定义版本快照实体 - 每次工作流更新时自动创建的定义快照
/// </summary>
public class WorkflowDefinitionVersion : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 关联的工作流定义 ID
    /// </summary>
    public Guid WorkflowDefinitionId { get; set; }

    /// <summary>
    /// 版本号（自增，从 1 开始）
    /// </summary>
    public int VersionNumber { get; set; }

    /// <summary>
    /// 完整定义快照（JSON 序列化的工作流定义）
    /// </summary>
    public string Definition { get; set; } = string.Empty;

    /// <summary>
    /// 变更说明（可选）
    /// </summary>
    public string? ChangeDescription { get; set; }

    /// <summary>
    /// 导航属性
    /// </summary>
    public virtual WorkflowDefinition? WorkflowDefinition { get; set; }
}
