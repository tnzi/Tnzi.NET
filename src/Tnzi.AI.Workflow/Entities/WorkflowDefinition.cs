namespace Tnzi.AI.Workflow.Entities;

/// <summary>
/// 工作流定义实体
/// </summary>
public class WorkflowDefinition : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 工作流名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 工作流描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 工作流步骤定义（JSON）
    /// </summary>
    public string Steps { get; set; } = "[]";

    /// <summary>
    /// 执行模式：Sequential, Parallel
    /// </summary>
    public WorkflowExecutionMode ExecutionMode { get; set; } = WorkflowExecutionMode.Sequential;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 额外配置（JSON）
    /// </summary>
    public string? Configuration { get; set; }
}
