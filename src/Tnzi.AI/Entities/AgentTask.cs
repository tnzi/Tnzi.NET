namespace Tnzi.AI.Entities;

/// <summary>
/// Agent 任务实体 - 持久化 AI Agent 的任务追踪（Todo 项）
/// </summary>
public class AgentTask : CreationAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>关联的 AgentRun ID</summary>
    public Guid RunId { get; set; }

    /// <summary>父任务 ID（支持任务树结构）</summary>
    public Guid? ParentTaskId { get; set; }

    /// <summary>任务标题/内容</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>任务状态</summary>
    public AgentTaskStatus Status { get; set; } = AgentTaskStatus.Pending;

    /// <summary>排序序号</summary>
    public int OrderIndex { get; set; }

    /// <summary>任务结果/输出</summary>
    public string? Result { get; set; }

    /// <summary>完成时间</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>租户 ID</summary>
    public Guid? TenantId { get; set; }
}

/// <summary>
/// Agent 任务状态枚举
/// </summary>
public enum AgentTaskStatus
{
    /// <summary>待处理</summary>
    Pending = 0,

    /// <summary>进行中</summary>
    InProgress = 1,

    /// <summary>已完成</summary>
    Completed = 2,

    /// <summary>已跳过</summary>
    Skipped = 3
}
