namespace Tnzi.AI.Middleware;

/// <summary>
/// Todo/计划模式中间件（Order = 420, 在 ContextInjection 之后）
/// Plan Mode 下注入当前 Todo 状态提醒，并在结果中包含 Todos
/// </summary>
public class TodoMiddleware : IAiMiddleware
{
    private readonly IAgentExecutionContextAccessor _contextAccessor;
    private readonly IOptionsMonitor<TodoOptions> _options;
    private readonly ILogger<TodoMiddleware> _logger;

    public int Order => AiMiddlewareOrders.Todo;

    public TodoMiddleware(IAgentExecutionContextAccessor contextAccessor, IOptionsMonitor<TodoOptions> options, ILogger<TodoMiddleware> logger)
    {
        _contextAccessor = Check.NotNull(contextAccessor);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        if (!context.Request.PlanMode || !_options.CurrentValue.Enabled)
            return await next(context, cancellationToken);

        // Before: 注入已有 Todo 状态提醒
        InjectTodoReminder(context);

        var result = await next(context, cancellationToken);

        // After: 从 accessor Properties 中提取 Todos 附加到结果
        var todos = TakeTodosFromAccessor();
        if (todos != null)
        {
            return result.CloneWith(todos: todos);
        }

        return result;
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!context.Request.PlanMode || !_options.CurrentValue.Enabled)
        {
            await foreach (var chunk in next(context, cancellationToken))
                yield return chunk;
            yield break;
        }

        InjectTodoReminder(context);

        await foreach (var chunk in next(context, cancellationToken))
        {
            yield return chunk;
        }

        // 在流末尾发送 Todos 状态
        var todos = TakeTodosFromAccessor();
        if (todos != null)
        {
            yield return new AgentStreamChunk
            {
                Todos = todos,
                EventType = MiddlewareEventTypes.TodosUpdated
            };
        }
    }

    /// <summary>
    /// 注入 Todo 状态提醒到系统消息
    /// </summary>
    private void InjectTodoReminder(AiMiddlewareContext context)
    {
        // 从 context.Properties 或 accessor Properties 读取已有 Todos
        var todos = GetTodosFromContext(context) ?? GetTodosFromAccessor();
        if (todos is not { Count: > 0 }) return;

        var sb = new StringBuilder();
        sb.AppendLine("<system_reminder>");
        sb.AppendLine("Current task plan status:");
        foreach (var todo in todos.OrderBy(t => t.Order))
        {
            var statusIcon = todo.Status switch
            {
                TodoStatus.Completed => "[x]",
                TodoStatus.InProgress => "[~]",
                TodoStatus.Skipped => "[-]",
                _ => "[ ]"
            };
            sb.AppendLine($"  {statusIcon} {todo.Content}");
        }

        var completed = todos.Count(t => t.Status == TodoStatus.Completed);
        sb.AppendLine($"Progress: {completed}/{todos.Count} completed.");
        sb.AppendLine("Update the todo list via write_todos as you complete tasks.");
        sb.AppendLine("</system_reminder>");

        context.Messages.Add(new ChatMessage(ChatRole.System, sb.ToString()));
        _logger.LogDebug("Injected todo reminder with {Count} items", todos.Count);
    }

    private static List<TodoItemDto>? GetTodosFromContext(AiMiddlewareContext context)
    {
        if (context.Properties.TryGetValue(ContextPropertyKeys.Todos, out var obj) && obj is List<TodoItemDto> todos)
            return todos;
        return null;
    }

    private List<TodoItemDto>? GetTodosFromAccessor()
    {
        if (_contextAccessor.Properties.TryGetValue(ContextPropertyKeys.Todos, out var obj) && obj is List<TodoItemDto> todos)
            return todos;
        return null;
    }

    private List<TodoItemDto>? TakeTodosFromAccessor()
    {
        var todos = GetTodosFromAccessor();
        if (todos != null)
        {
            _contextAccessor.Properties.Remove(ContextPropertyKeys.Todos);
        }

        return todos;
    }
}
