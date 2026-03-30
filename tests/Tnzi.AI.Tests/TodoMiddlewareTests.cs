namespace Tnzi.AI.Tests;

public class TodoMiddlewareTests
{
    [Fact]
    public void Order_Is420()
    {
        var middleware = CreateMiddleware();
        Assert.Equal(420, middleware.Order);
    }

    [Fact]
    public async Task InvokeAsync_PlanModeDisabled_SkipsInjection()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext(planMode: false);
        var messageCountBefore = context.Messages.Count;

        await middleware.InvokeAsync(context,
            (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "ok" }),
            CancellationToken.None);

        Assert.Equal(messageCountBefore, context.Messages.Count);
    }

    [Fact]
    public async Task InvokeAsync_PlanModeWithTodos_InjectsReminder()
    {
        var accessor = new AgentExecutionContextAccessor();
        var middleware = CreateMiddleware(accessor);
        var context = CreateContext(planMode: true);

        // 预设 Todos 到 context.Properties（模拟已有进度）
        var todos = new List<TodoItemDto>
        {
            new() { Content = "Design schema", Status = TodoStatus.Completed, Order = 1 },
            new() { Content = "Write tests", Status = TodoStatus.InProgress, Order = 2 },
            new() { Content = "Implement service", Status = TodoStatus.Pending, Order = 3 }
        };
        context.Properties["Todos"] = todos;

        await middleware.InvokeAsync(context,
            (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "ok" }),
            CancellationToken.None);

        var systemMessages = context.Messages
            .Where(m => m.Role == ChatRole.System)
            .Select(m => m.Text ?? "")
            .ToList();

        Assert.Contains(systemMessages, m => m.Contains("<system_reminder>") && m.Contains("Write tests"));
    }

    [Fact]
    public async Task InvokeAsync_PlanModeNoTodos_NoInjection()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext(planMode: true);
        var messageCountBefore = context.Messages.Count;

        await middleware.InvokeAsync(context,
            (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "ok" }),
            CancellationToken.None);

        // 无 Todo 时不注入提醒
        var systemTexts = string.Join(" ", context.Messages
            .Where(m => m.Role == ChatRole.System)
            .Select(m => m.Text ?? ""));
        Assert.DoesNotContain("<system_reminder>", systemTexts);
    }

    [Fact]
    public async Task InvokeAsync_Result_IncludesTodos()
    {
        var accessor = new AgentExecutionContextAccessor();
        var middleware = CreateMiddleware(accessor);
        var context = CreateContext(planMode: true);

        var result = await middleware.InvokeAsync(context,
            (ctx, ct) =>
            {
                // 模拟 TodoTools 在 next 执行期间写入 Todos
                accessor.Properties["Todos"] = new List<TodoItemDto>
                {
                    new() { Content = "Task A", Status = TodoStatus.Completed, Order = 1 }
                };
                return Task.FromResult(new AgentRunResult { Response = "done" });
            },
            CancellationToken.None);

        Assert.NotNull(result.Todos);
        Assert.Single(result.Todos);
        Assert.Equal("Task A", result.Todos[0].Content);
    }

    [Fact]
    public async Task InvokeAsync_Disabled_PassesThrough()
    {
        var opts = new TodoOptions { Enabled = false };
        var middleware = new TodoMiddleware(
            new AgentExecutionContextAccessor(),
            Microsoft.Extensions.Options.Options.Create(opts),
            NullLogger<TodoMiddleware>.Instance);
        var context = CreateContext(planMode: true);

        var result = await middleware.InvokeAsync(context,
            (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "ok" }),
            CancellationToken.None);

        Assert.Equal("ok", result.Response);
        Assert.Null(result.Todos);
    }

    [Fact]
    public async Task InvokeAsync_TodoStatusIcons_CorrectFormat()
    {
        var accessor = new AgentExecutionContextAccessor();
        var middleware = CreateMiddleware(accessor);
        var context = CreateContext(planMode: true);

        var todos = new List<TodoItemDto>
        {
            new() { Content = "Done task", Status = TodoStatus.Completed, Order = 1 },
            new() { Content = "Active task", Status = TodoStatus.InProgress, Order = 2 },
            new() { Content = "Skipped task", Status = TodoStatus.Skipped, Order = 3 },
            new() { Content = "Pending task", Status = TodoStatus.Pending, Order = 4 }
        };
        context.Properties["Todos"] = todos;

        await middleware.InvokeAsync(context,
            (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "ok" }),
            CancellationToken.None);

        var systemText = context.Messages
            .Where(m => m.Role == ChatRole.System)
            .Select(m => m.Text ?? "")
            .FirstOrDefault(m => m.Contains("<system_reminder>")) ?? "";

        Assert.Contains("[x] Done task", systemText);
        Assert.Contains("[~] Active task", systemText);
        Assert.Contains("[-] Skipped task", systemText);
        Assert.Contains("[ ] Pending task", systemText);
    }

    private static TodoMiddleware CreateMiddleware(IAgentExecutionContextAccessor? accessor = null)
    {
        return new TodoMiddleware(
            accessor ?? new AgentExecutionContextAccessor(),
            Microsoft.Extensions.Options.Options.Create(new TodoOptions()),
            NullLogger<TodoMiddleware>.Instance);
    }

    private static AiMiddlewareContext CreateContext(bool planMode)
    {
        return new AiMiddlewareContext
        {
            Request = new AgentRunRequest
            {
                UserMessage = "Hello",
                ThreadId = Guid.NewGuid(),
                PlanMode = planMode
            },
            Agent = AgentResolution.Success(null!, "TestProvider", "test-model", null),
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            Messages = []
        };
    }
}
