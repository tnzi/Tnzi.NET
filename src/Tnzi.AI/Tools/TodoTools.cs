namespace Tnzi.AI.Tools;

/// <summary>
/// Todo 任务追踪工具 — AI Agent 用于在 Plan Mode 下追踪任务进度
/// </summary>
[AIToolGroup("todo")]
public class TodoTools
{
    private readonly IAgentExecutionContextAccessor _contextAccessor;

    public TodoTools(IAgentExecutionContextAccessor contextAccessor)
    {
        _contextAccessor = Check.NotNull(contextAccessor);
    }

    /// <summary>
    /// Write or update the todo list for the current task plan.
    /// Use this to track progress on multi-step tasks.
    /// </summary>
    [AIFunction("write_todos",
        Description = "Write or update the todo list for tracking task progress in plan mode. Include ALL items with their current status.",
        IsConcurrencySafe = true)]
    public string WriteTodos(
        [Description("Complete list of todo items with status (Pending/InProgress/Completed/Skipped)")] List<TodoItemDto> items)
    {
        // 将 Todos 存储到共享属性包，TodoMiddleware 在 next 完成后读取
        _contextAccessor.Properties[ContextPropertyKeys.Todos] = items ?? [];

        if (items is not { Count: > 0 })
            return "Todo list cleared.";

        var completed = items.Count(i => i.Status == TodoStatus.Completed);
        var total = items.Count;
        return $"Updated {total} todo item(s). Progress: {completed}/{total} completed.";
    }
}
