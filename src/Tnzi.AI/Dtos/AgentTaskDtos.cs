namespace Tnzi.AI.Dtos;

/// <summary>
/// AgentTask 输出 DTO
/// </summary>
public class AgentTaskDto
{
    /// <summary>任务 ID</summary>
    public Guid Id { get; set; }

    /// <summary>关联的 AgentRun ID</summary>
    public Guid RunId { get; set; }

    /// <summary>父任务 ID</summary>
    public Guid? ParentTaskId { get; set; }

    /// <summary>任务标题/内容</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>任务状态</summary>
    public AgentTaskStatus Status { get; set; }

    /// <summary>排序序号</summary>
    public int OrderIndex { get; set; }

    /// <summary>任务结果/输出</summary>
    public string? Result { get; set; }

    /// <summary>完成时间</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>创建时间</summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 按状态分页查询 AgentTask（<c>GET admin/ai/tasks/by-status</c>）。
/// </summary>
/// <remarks>
/// 已完成的任务随运行持续累积，一次读出某个状态的全部任务会随时间无界增长，所以按页取。
/// </remarks>
public class AgentTaskStatusQueryDto : PagedQueryDto
{
    /// <summary>任务状态</summary>
    public AgentTaskStatus Status { get; set; }
}
