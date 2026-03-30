namespace Tnzi.AI.Dtos;

/// <summary>
/// Todo 任务项
/// </summary>
public class TodoItemDto
{
    /// <summary>任务内容</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>任务状态</summary>
    public TodoStatus Status { get; set; } = TodoStatus.Pending;

    /// <summary>排序序号</summary>
    public int Order { get; set; }
}
