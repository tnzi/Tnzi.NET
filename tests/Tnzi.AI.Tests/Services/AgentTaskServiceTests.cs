namespace Tnzi.AI.Tests.Services;

/// <summary>
/// AgentTaskService 单元测试
/// </summary>
public class AgentTaskServiceTests
{
    private readonly Mock<IRepository<AgentTask, Guid>> _repository;
    private readonly IServiceProvider _serviceProvider;

    public AgentTaskServiceTests()
    {
        _repository = new Mock<IRepository<AgentTask, Guid>>();

        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    private AgentTaskService CreateService() => new(_serviceProvider, _repository.Object);

    // -------------------------------------------------------------------------
    // SyncFromTodosAsync — 新增
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SyncFromTodosAsync_CreatesNewTasks()
    {
        // Arrange
        var runId = Guid.NewGuid();
        _repository.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<AgentTask, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AgentTask>());
        _repository.Setup(r => r.InsertManyAsync(It.IsAny<IEnumerable<AgentTask>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var todos = new List<TodoItemDto>
        {
            new() { Content = "Task A", Status = TodoStatus.Pending, Order = 0 },
            new() { Content = "Task B", Status = TodoStatus.InProgress, Order = 1 },
            new() { Content = "Task C", Status = TodoStatus.Completed, Order = 2 }
        };

        // Act
        await CreateService().SyncFromTodosAsync(runId, todos);

        // Assert
        _repository.Verify(r => r.InsertManyAsync(
            It.Is<IEnumerable<AgentTask>>(tasks =>
                tasks.Count() == 3 &&
                tasks.First().Title == "Task A" &&
                tasks.Last().Status == AgentTaskStatus.Completed),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // -------------------------------------------------------------------------
    // SyncFromTodosAsync — 更新已有
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SyncFromTodosAsync_UpdatesExistingTasks()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var existing = new AgentTask
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            Title = "Old title",
            Status = AgentTaskStatus.Pending,
            OrderIndex = 0
        };

        _repository.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<AgentTask, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AgentTask> { existing });

        var todos = new List<TodoItemDto>
        {
            new() { Content = "Updated title", Status = TodoStatus.InProgress, Order = 0 }
        };

        // Act
        await CreateService().SyncFromTodosAsync(runId, todos);

        // Assert — batch update
        _repository.Verify(r => r.UpdateManyAsync(
            It.Is<IEnumerable<AgentTask>>(tasks =>
                tasks.Count() == 1 &&
                tasks.First().Title == "Updated title" &&
                tasks.First().Status == AgentTaskStatus.InProgress),
            It.IsAny<CancellationToken>()), Times.Once);
        // 没有新增
        _repository.Verify(r => r.InsertManyAsync(It.IsAny<IEnumerable<AgentTask>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // SyncFromTodosAsync — CompletedAt 设置
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SyncFromTodosAsync_SetsCompletedAtOnCompletion()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var existing = new AgentTask
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            Title = "Some task",
            Status = AgentTaskStatus.InProgress,
            OrderIndex = 0,
            CompletedAt = null
        };

        _repository.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<AgentTask, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AgentTask> { existing });

        var todos = new List<TodoItemDto>
        {
            new() { Content = "Some task", Status = TodoStatus.Completed, Order = 0 }
        };

        // Act
        await CreateService().SyncFromTodosAsync(runId, todos);

        // Assert — batch update with CompletedAt set
        _repository.Verify(r => r.UpdateManyAsync(
            It.Is<IEnumerable<AgentTask>>(tasks =>
                tasks.Count() == 1 &&
                tasks.First().Status == AgentTaskStatus.Completed &&
                tasks.First().CompletedAt != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncFromTodosAsync_NewCompletedTask_SetsCompletedAt()
    {
        // Arrange
        var runId = Guid.NewGuid();
        _repository.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<AgentTask, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AgentTask>());
        _repository.Setup(r => r.InsertManyAsync(It.IsAny<IEnumerable<AgentTask>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var todos = new List<TodoItemDto>
        {
            new() { Content = "Already done", Status = TodoStatus.Completed, Order = 0 }
        };

        // Act
        await CreateService().SyncFromTodosAsync(runId, todos);

        // Assert
        _repository.Verify(r => r.InsertManyAsync(
            It.Is<IEnumerable<AgentTask>>(tasks => tasks.All(t => t.CompletedAt != null)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // -------------------------------------------------------------------------
    // GetByRunIdAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetByRunIdAsync_ReturnsOrderedTasks()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var tasks = new List<AgentTask>
        {
            new() { Id = Guid.NewGuid(), RunId = runId, Title = "B", OrderIndex = 1, CreationTime = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), RunId = runId, Title = "A", OrderIndex = 0, CreationTime = DateTime.UtcNow }
        };
        _repository.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<AgentTask, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tasks);

        // Act
        var result = await CreateService().GetByRunIdAsync(runId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Count);
        Assert.Equal("A", result.Data[0].Title);
        Assert.Equal("B", result.Data[1].Title);
    }

    // -------------------------------------------------------------------------
    // GetByStatusAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetByStatusAsync_FiltersCorrectly()
    {
        // Arrange
        var tasks = new List<AgentTask>
        {
            new() { Id = Guid.NewGuid(), Title = "T1", Status = AgentTaskStatus.Completed, CreationTime = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc) },
            new() { Id = Guid.NewGuid(), Title = "T2", Status = AgentTaskStatus.Completed, CreationTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        };
        _repository.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<AgentTask, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tasks);

        // Act
        var result = await CreateService().GetByStatusAsync(AgentTaskStatus.Completed);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Count);
        // 按 CreationTime 排序
        Assert.Equal("T2", result.Data[0].Title);
        Assert.Equal("T1", result.Data[1].Title);
    }
}
