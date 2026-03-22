namespace Tnzi.AI.Tests;

/// <summary>
/// AgentTraceService 单元测试 — 覆盖 GetByRunAsync / GetByNodeAsync
/// </summary>
public class AgentTraceServiceTests
{
    #region Helpers

    private readonly Mock<IRepository<AgentRun, Guid>> _runRepo = new();
    private readonly Mock<IRepository<AgentRunTrace, Guid>> _traceRepo = new();
    private readonly Mock<IRepository<AgentRunNode, Guid>> _nodeRepo = new();
    private readonly IServiceProvider _serviceProvider;

    public AgentTraceServiceTests()
    {
        // Initialize Mapster for MapTo<T> / ProjectTo<T> extension methods
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    private AgentTraceService CreateService() => new(
        _runRepo.Object,
        _traceRepo.Object,
        _nodeRepo.Object,
        _serviceProvider);

    private static AgentRunTrace MakeTrace(
        Guid runId,
        Guid? nodeId = null,
        string eventType = "llm_call",
        DateTime? creationTime = null) => new()
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            NodeId = nodeId,
            EventType = eventType,
            DurationMs = 100,
            CreationTime = creationTime ?? DateTime.UtcNow
        };

    /// <summary>
    /// 设置 trace 仓储为 IQueryable（支持 LINQ Where/OrderBy/ProjectTo 链式调用）
    /// </summary>
    private void SetupTraceQueryable(List<AgentRunTrace> data)
    {
        var mock = data.BuildMock();
        _traceRepo.As<IQueryable<AgentRunTrace>>().Setup(q => q.Provider).Returns(mock.Provider);
        _traceRepo.As<IQueryable<AgentRunTrace>>().Setup(q => q.Expression).Returns(mock.Expression);
        _traceRepo.As<IQueryable<AgentRunTrace>>().Setup(q => q.ElementType).Returns(mock.ElementType);
        _traceRepo.As<IQueryable<AgentRunTrace>>().Setup(q => q.GetEnumerator()).Returns(mock.GetEnumerator());
    }

    #endregion

    // -------------------------------------------------------------------------
    // GetByRunAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetByRunAsync_ExistingTraces_ReturnsOrderedByCreationTime()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var t1 = MakeTrace(runId, creationTime: new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc));
        var t2 = MakeTrace(runId, creationTime: new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        var t3 = MakeTrace(runId, creationTime: new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc));

        _runRepo.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<AgentRun, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        SetupTraceQueryable([t1, t2, t3]);

        var svc = CreateService();

        // Act
        var result = await svc.GetByRunAsync(runId);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Count.ShouldBe(3);
        // 应按 CreationTime 升序排列
        result.Data[0].CreationTime.ShouldBe(t1.CreationTime);
        result.Data[1].CreationTime.ShouldBe(t3.CreationTime);
        result.Data[2].CreationTime.ShouldBe(t2.CreationTime);
    }

    [Fact]
    public async Task GetByRunAsync_NoTraces_ReturnsEmptyList()
    {
        // Arrange
        var runId = Guid.NewGuid();

        _runRepo.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<AgentRun, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        SetupTraceQueryable([]);

        var svc = CreateService();

        // Act
        var result = await svc.GetByRunAsync(runId);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Count.ShouldBe(0);
    }

    [Fact]
    public async Task GetByRunAsync_InvalidRunId_ReturnsNotFoundError()
    {
        // Arrange
        var runId = Guid.NewGuid();

        _runRepo.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<AgentRun, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var svc = CreateService();

        // Act
        var result = await svc.GetByRunAsync(runId);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    // -------------------------------------------------------------------------
    // GetByNodeAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetByNodeAsync_MatchingNode_ReturnsFilteredTraces()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var otherNodeId = Guid.NewGuid();

        var t1 = MakeTrace(runId, nodeId, "llm_call");
        var t2 = MakeTrace(runId, otherNodeId, "tool_call");
        var t3 = MakeTrace(runId, nodeId, "tool_call");

        _nodeRepo.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<AgentRunNode, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        SetupTraceQueryable([t1, t2, t3]);

        var svc = CreateService();

        // Act
        var result = await svc.GetByNodeAsync(runId, nodeId);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Count.ShouldBe(2);
        result.Data.ShouldAllBe(d => d.NodeId == nodeId);
    }

    [Fact]
    public async Task GetByNodeAsync_NonExistentNode_ReturnsNotFoundError()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();

        _nodeRepo.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<AgentRunNode, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var svc = CreateService();

        // Act
        var result = await svc.GetByNodeAsync(runId, nodeId);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }
}
