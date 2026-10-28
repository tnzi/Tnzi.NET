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
    // SyncFromTodosAsync - 新增
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
    // SyncFromTodosAsync - 更新已有
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

        // Assert - batch update
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
    // SyncFromTodosAsync - 完整列表语义：列表里没有的删除
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SyncFromTodosAsync_DeletesTasksNoLongerInTheList()
    {
        var runId = Guid.NewGuid();
        var kept = new AgentTask { Id = Guid.NewGuid(), RunId = runId, Title = "Keep", OrderIndex = 0 };
        var removed = new AgentTask { Id = Guid.NewGuid(), RunId = runId, Title = "Dropped", OrderIndex = 1 };
        _repository.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<AgentTask, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AgentTask> { kept, removed });

        await CreateService().SyncFromTodosAsync(runId, [new() { Content = "Keep", Status = TodoStatus.Pending, Order = 0 }]);

        _repository.Verify(r => r.DeleteManyAsync(
            It.Is<IEnumerable<AgentTask>>(tasks => tasks.Single() == removed),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncFromTodosAsync_EmptyList_DeletesEveryTaskOfTheRun()
    {
        var runId = Guid.NewGuid();
        var a = new AgentTask { Id = Guid.NewGuid(), RunId = runId, OrderIndex = 0 };
        var b = new AgentTask { Id = Guid.NewGuid(), RunId = runId, OrderIndex = 1 };
        _repository.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<AgentTask, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AgentTask> { a, b });

        await CreateService().SyncFromTodosAsync(runId, []);

        _repository.Verify(r => r.DeleteManyAsync(
            It.Is<IEnumerable<AgentTask>>(tasks => tasks.Count() == 2),
            It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.InsertManyAsync(It.IsAny<IEnumerable<AgentTask>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // SyncFromTodosAsync - CompletedAt 设置
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

        // Assert - batch update with CompletedAt set
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
    public async Task GetByStatusAsync_FiltersOrdersAndPages()
    {
        // Arrange
        SetupQueryable(
        [
            new() { Id = Guid.NewGuid(), Title = "T1", Status = AgentTaskStatus.Completed, CreationTime = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc) },
            new() { Id = Guid.NewGuid(), Title = "T2", Status = AgentTaskStatus.Completed, CreationTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new() { Id = Guid.NewGuid(), Title = "T3", Status = AgentTaskStatus.Completed, CreationTime = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc) },
            new() { Id = Guid.NewGuid(), Title = "P1", Status = AgentTaskStatus.Pending, CreationTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        ]);

        // Act
        var result = await CreateService().GetByStatusAsync(new AgentTaskStatusQueryDto
        {
            Status = AgentTaskStatus.Completed,
            PageIndex = 1,
            PageSize = 2
        });

        // Assert：只返回一页，按 CreationTime 升序，总数只算该状态
        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Data!.TotalCount);
        Assert.Equal(["T2", "T3"], result.Data.Items.Select(t => t.Title));
    }

    [Fact]
    public async Task GetByStatusAsync_LargePageSize_IsCappedByThePagingLimit()
    {
        // 一次请求的条数有上限：已完成任务随运行持续累积，此前整表读进内存
        SetupQueryable(Enumerable.Range(0, 150)
            .Select(i => new AgentTask
            {
                Id = Guid.NewGuid(),
                Title = $"T{i}",
                Status = AgentTaskStatus.Completed,
                CreationTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i)
            })
            .ToList());

        var result = await CreateService().GetByStatusAsync(new AgentTaskStatusQueryDto
        {
            Status = AgentTaskStatus.Completed,
            PageSize = 10_000
        });

        Assert.True(result.Succeeded);
        Assert.Equal(150, result.Data!.TotalCount);
        Assert.Equal(100, result.Data.Items.Count);
    }

    private void SetupQueryable(List<AgentTask> data)
    {
        var mock = data.BuildMock();
        _repository.As<IQueryable<AgentTask>>().Setup(q => q.Provider).Returns(mock.Provider);
        _repository.As<IQueryable<AgentTask>>().Setup(q => q.Expression).Returns(mock.Expression);
        _repository.As<IQueryable<AgentTask>>().Setup(q => q.ElementType).Returns(mock.ElementType);
        _repository.As<IQueryable<AgentTask>>().Setup(q => q.GetEnumerator()).Returns(() => mock.GetEnumerator());
    }
}
