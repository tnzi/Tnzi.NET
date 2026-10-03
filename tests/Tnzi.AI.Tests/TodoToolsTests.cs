namespace Tnzi.AI.Tests;

public class TodoToolsTests
{
    [Fact]
    public void WriteTodos_StoresInAccessorProperties()
    {
        var accessor = new AgentExecutionContextAccessor();
        var tools = new TodoTools(accessor);

        var items = new List<TodoItemDto>
        {
            new() { Content = "Write tests", Status = TodoStatus.Pending, Order = 1 },
            new() { Content = "Implement", Status = TodoStatus.Pending, Order = 2 }
        };

        var result = tools.WriteTodos(items);

        Assert.Contains("2 todo", result, StringComparison.OrdinalIgnoreCase);
        Assert.True(accessor.Properties.ContainsKey("Todos"));
        var stored = accessor.Properties["Todos"] as List<TodoItemDto>;
        Assert.NotNull(stored);
        Assert.Equal(2, stored.Count);
    }

    [Fact]
    public void WriteTodos_EmptyList_ReturnsMessage()
    {
        var accessor = new AgentExecutionContextAccessor();
        var tools = new TodoTools(accessor);

        var result = tools.WriteTodos([]);

        Assert.Contains("cleared", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WriteTodos_WithCompletedItems_ShowsProgress()
    {
        var accessor = new AgentExecutionContextAccessor();
        var tools = new TodoTools(accessor);

        var items = new List<TodoItemDto>
        {
            new() { Content = "Task A", Status = TodoStatus.Completed, Order = 1 },
            new() { Content = "Task B", Status = TodoStatus.InProgress, Order = 2 },
            new() { Content = "Task C", Status = TodoStatus.Pending, Order = 3 }
        };

        var result = tools.WriteTodos(items);

        Assert.Contains("1/3 completed", result);
    }

    [Fact]
    public void WriteTodos_NullList_ClearsTodos()
    {
        var accessor = new AgentExecutionContextAccessor();
        var tools = new TodoTools(accessor);

        var result = tools.WriteTodos(null!);

        Assert.Contains("cleared", result, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// 后台落库「最后一次写入胜出」：前一次落库期间又写了几次，库里最终留下的必须是最新那份；失败要留痕。
/// </summary>
public class TodoToolsPersistenceTests
{
    [Fact]
    public async Task WritesDuringAPersist_AreCoalesced_AndTheLatestIsPersistedLast()
    {
        var service = new RecordingTaskService { BlockFirstCall = true };
        var (tools, runId) = CreateTools(service);

        tools.WriteTodos([Item("v1")]);
        await service.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        tools.WriteTodos([Item("v2")]);
        tools.WriteTodos([Item("v3")]);
        service.ReleaseFirstCall();
        await tools.WhenPersistedAsync().WaitAsync(TimeSpan.FromSeconds(10));

        service.Calls.Select(c => c.Todos.Single().Content).ShouldBe(["v1", "v3"]);
        service.Calls.ShouldAllBe(c => c.RunId == runId);
    }

    [Fact]
    public async Task ClearingTheList_IsPersistedToo()
    {
        var service = new RecordingTaskService();
        var (tools, _) = CreateTools(service);

        tools.WriteTodos([]);
        await tools.WhenPersistedAsync().WaitAsync(TimeSpan.FromSeconds(10));

        service.Calls.ShouldHaveSingleItem().Todos.ShouldBeEmpty();
    }

    [Fact]
    public async Task PersistFailure_IsLogged_NotSwallowed()
    {
        var service = new RecordingTaskService { Throw = true };
        var logger = new Mock<ILogger<TodoTools>>();
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        var (tools, _) = CreateTools(service, logger.Object);

        tools.WriteTodos([Item("v1")]);
        await tools.WhenPersistedAsync().WaitAsync(TimeSpan.FromSeconds(10));

        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.Is<Exception?>(e => e is InvalidOperationException),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task PersistRunsUnderTheTenantCapturedAtWriteTime()
    {
        var tenantId = Guid.NewGuid();
        var caller = new Mock<ICurrentTenant>();
        caller.SetupGet(t => t.Id).Returns(tenantId);
        var background = new Mock<ICurrentTenant>();
        background.Setup(t => t.Change(It.IsAny<Guid?>(), It.IsAny<string?>())).Returns(Mock.Of<IDisposable>());
        var service = new RecordingTaskService();
        var (tools, _) = CreateTools(service, currentTenant: caller.Object, scopeTenant: background.Object);

        tools.WriteTodos([Item("v1")]);
        await tools.WhenPersistedAsync().WaitAsync(TimeSpan.FromSeconds(10));

        background.Verify(t => t.Change(tenantId, It.IsAny<string?>()), Times.Once);
    }

    private static TodoItemDto Item(string content) => new() { Content = content, Status = TodoStatus.Pending, Order = 0 };

    private static (TodoTools Tools, Guid RunId) CreateTools(
        RecordingTaskService service, ILogger<TodoTools>? logger = null, ICurrentTenant? currentTenant = null, ICurrentTenant? scopeTenant = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAgentTaskService>(service);
        if (scopeTenant is not null) services.AddSingleton(scopeTenant);
        var provider = services.BuildServiceProvider();

        var accessor = new AgentExecutionContextAccessor();
        var runId = Guid.NewGuid();
        accessor.Properties[ContextPropertyKeys.CurrentRunId] = runId;
        return (new TodoTools(accessor, provider.GetRequiredService<IServiceScopeFactory>(), currentTenant, logger), runId);
    }

    private sealed class RecordingTaskService : IAgentTaskService
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public bool BlockFirstCall { get; init; }
        public bool Throw { get; init; }
        public TaskCompletionSource FirstCallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<(Guid RunId, List<TodoItemDto> Todos)> Calls { get; } = [];

        public void ReleaseFirstCall() => _release.TrySetResult();

        public async Task SyncFromTodosAsync(Guid runId, List<TodoItemDto> todos, CancellationToken cancellationToken = default)
        {
            lock (Calls) Calls.Add((runId, todos));
            if (Throw) throw new InvalidOperationException("db down");
            if (Interlocked.Increment(ref _callCount) == 1 && BlockFirstCall)
            {
                FirstCallStarted.TrySetResult();
                await _release.Task;
            }
        }

        public Task<Result<List<AgentTaskDto>>> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<List<AgentTaskDto>>> GetByStatusAsync(AgentTaskStatus status, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
