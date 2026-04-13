namespace Tnzi.AI.Tools;

/// <summary>
/// Todo 任务追踪工具 — AI Agent 用于在 Plan Mode 下追踪任务进度
/// </summary>
[AIToolGroup("todo")]
public class TodoTools
{
    private static readonly SemaphoreSlim _persistLock = new(1, 1);
    private readonly IAgentExecutionContextAccessor _contextAccessor;
    private readonly IServiceScopeFactory? _scopeFactory;

    public TodoTools(IAgentExecutionContextAccessor contextAccessor, IServiceScopeFactory? scopeFactory = null)
    {
        _contextAccessor = Check.NotNull(contextAccessor);
        _scopeFactory = scopeFactory;
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

        // 异步持久化到数据库（fire-and-forget，不影响工具返回）
        if (_scopeFactory != null && items is { Count: > 0 })
        {
            var runId = _contextAccessor.Properties.TryGetValue(ContextPropertyKeys.CurrentRunId, out var val)
                ? val as Guid? : null;
            if (runId.HasValue)
            {
                var capturedRunId = runId.Value;
                var capturedItems = items.ToList();
                _ = Task.Run(async () =>
                {
                    if (!_persistLock.Wait(0)) return; // Skip if another persist is in progress
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var taskService = scope.ServiceProvider.GetService<IAgentTaskService>();
                        if (taskService != null)
                        {
                            await taskService.SyncFromTodosAsync(capturedRunId, capturedItems);
                        }
                    }
                    catch { /* 持久化失败不影响主流程 */ }
                    finally
                    {
                        _persistLock.Release();
                    }
                });
            }
        }

        var completed = items.Count(i => i.Status == TodoStatus.Completed);
        var total = items.Count;
        return $"Updated {total} todo item(s). Progress: {completed}/{total} completed.";
    }
}
