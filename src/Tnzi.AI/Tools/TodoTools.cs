namespace Tnzi.AI.Tools;

/// <summary>
/// Todo 任务追踪工具 - AI Agent 用于在 Plan Mode 下追踪任务进度
/// </summary>
/// <remarks>
/// <para>
/// 每次 <c>write_todos</c> 给的是<b>完整列表</b>，持久化的语义因此是「最后一次写入胜出」：库里留下的必须是最新那份，
/// 从列表里去掉的项要从库里删掉。持久化在后台进行、不拖慢工具返回，按运行合并：同一运行在前一次落库期间又写了几次，
/// 只保留最新那份，前一次落完立刻接着落它。此前的做法是抢不到锁就直接放弃，被放弃的恰好是最新状态。
/// </para>
/// <para>
/// 后台落库在新建的作用域里跑，那时请求可能已经结束：租户在调用时快照下来，落库前显式切换过去，
/// 不依赖环境上下文还在不在。
/// </para>
/// </remarks>
[AIToolGroup("todo")]
public class TodoTools : IAIToolProvider
{
    private readonly IAgentExecutionContextAccessor _contextAccessor;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly ICurrentTenant? _currentTenant;
    private readonly IOptionsMonitor<TodoOptions>? _options;
    private readonly ILogger<TodoTools> _logger;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, TodoSnapshot> _pending = [];
    private Task _drain = Task.CompletedTask;
    private bool _draining;

    public TodoTools(
        IAgentExecutionContextAccessor contextAccessor,
        IServiceScopeFactory? scopeFactory = null,
        ICurrentTenant? currentTenant = null,
        ILogger<TodoTools>? logger = null,
        IOptionsMonitor<TodoOptions>? options = null)
    {
        _contextAccessor = Check.NotNull(contextAccessor);
        _scopeFactory = scopeFactory;
        _currentTenant = currentTenant;
        _logger = logger ?? NullLogger<TodoTools>.Instance;
        _options = options;
    }

    /// <summary>
    /// Write or update the todo list for the current task plan.
    /// Use this to track progress on multi-step tasks.
    /// </summary>
    /// <remarks>不标 IsConcurrencySafe：每次写的是完整列表、最后一次胜出，并行时哪一份最后落笔不确定；写入的运行属性包也不是线程安全的。</remarks>
    [AIFunction("write_todos",
        "Write or update the todo list for tracking task progress in plan mode. Include ALL items with their current status.")]
    public string WriteTodos(
        [Description("Complete list of todo items with status (Pending/InProgress/Completed/Skipped)")] List<TodoItemDto> items)
    {
        var snapshot = items?.ToList() ?? [];

        // 超过 AI:Todo:MaxItems 的列表整份拒收（不截断：截掉哪几项该由模型决定），上一份列表原样保留
        var maxItems = (_options?.CurrentValue ?? new TodoOptions()).MaxItems;
        if (snapshot.Count > maxItems)
        {
            return $"Error: the todo list has {snapshot.Count} items but at most {maxItems} are allowed. "
                + $"Merge or drop items and call write_todos again with no more than {maxItems} items.";
        }

        // 将 Todos 存储到共享属性包，TodoMiddleware 在 next 完成后读取
        _contextAccessor.Properties[ContextPropertyKeys.Todos] = snapshot;

        // 清空也要落库：列表是完整的，空列表意味着库里这次运行的任务全部作废。
        SchedulePersist(snapshot);

        if (snapshot.Count == 0)
            return "Todo list cleared.";

        var completed = snapshot.Count(i => i.Status == TodoStatus.Completed);
        var total = snapshot.Count;
        return $"Updated {total} todo item(s). Progress: {completed}/{total} completed.";
    }

    /// <summary>当前排队的后台落库全部完成时结束（测试与关停时用）。</summary>
    internal Task WhenPersistedAsync()
    {
        lock (_gate)
        {
            return _drain;
        }
    }

    private void SchedulePersist(List<TodoItemDto> items)
    {
        if (_scopeFactory is null) return;

        var runId = _contextAccessor.Properties.TryGetValue(ContextPropertyKeys.CurrentRunId, out var val)
            ? val as Guid?
            : null;
        if (runId is null) return;

        lock (_gate)
        {
            // 同一运行只留最新一份：排在后面的写入覆盖尚未落库的那份。
            _pending[runId.Value] = new TodoSnapshot(runId.Value, items, _currentTenant?.Id);
            if (_draining) return;

            _draining = true;
            _drain = Task.Run(DrainAsync);
        }
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            TodoSnapshot next;
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    _draining = false;
                    return;
                }

                var runId = _pending.Keys.First();
                next = _pending[runId];
                _pending.Remove(runId);
            }

            try
            {
                await PersistAsync(next);
            }
            catch (Exception ex)
            {
                // 落库失败不影响 agent 主流程（工具早已返回），但必须留痕：丢掉的是这次运行的任务进度。
                _logger.LogWarning(ex, "Failed to persist {Count} todo item(s) for run {RunId}", next.Items.Count, next.RunId);
            }
        }
    }

    private async Task PersistAsync(TodoSnapshot snapshot)
    {
        using var scope = _scopeFactory!.CreateScope();
        var taskService = scope.ServiceProvider.GetService<IAgentTaskService>();
        if (taskService is null) return;

        var tenant = scope.ServiceProvider.GetService<ICurrentTenant>();
        using (tenant?.Change(snapshot.TenantId))
        {
            await taskService.SyncFromTodosAsync(snapshot.RunId, snapshot.Items);
        }
    }

    private sealed record TodoSnapshot(Guid RunId, List<TodoItemDto> Items, Guid? TenantId);
}
