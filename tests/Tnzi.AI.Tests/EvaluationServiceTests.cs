namespace Tnzi.AI.Tests;

/// <summary>
/// EvaluationService 单元测试 - 覆盖 GetById / GetList / Delete
/// </summary>
public class EvaluationServiceTests
{
    #region Helpers

    private readonly Mock<IRepository<EvaluationRun, Guid>> _repository = new();
    private readonly Mock<IAgentEvaluator> _evaluator = new();
    private readonly IServiceProvider _serviceProvider;
    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();

    public EvaluationServiceTests()
    {
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    private EvaluationService CreateService() => new(_serviceProvider, _repository.Object, _evaluator.Object, _scopeFactory.Object);

    private static EvaluationRun MakeRun(
        Guid? id = null,
        Guid? agentId = null,
        EvaluationRunStatus status = EvaluationRunStatus.Completed,
        int caseCount = 5,
        int passedCount = 4,
        double averageScore = 0.8,
        DateTime? creationTime = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        AgentId = agentId ?? Guid.NewGuid(),
        Status = status,
        CaseCount = caseCount,
        PassedCount = passedCount,
        AverageScore = averageScore,
        Duration = TimeSpan.FromSeconds(10),
        ResultsJson = """{"cases":[]}""",
        CreationTime = creationTime ?? DateTime.UtcNow
    };

    private void SetupQueryable(List<EvaluationRun> data)
    {
        var mock = data.BuildMock();
        _repository.As<IQueryable<EvaluationRun>>().Setup(q => q.Provider).Returns(mock.Provider);
        _repository.As<IQueryable<EvaluationRun>>().Setup(q => q.Expression).Returns(mock.Expression);
        _repository.As<IQueryable<EvaluationRun>>().Setup(q => q.ElementType).Returns(mock.ElementType);
        _repository.As<IQueryable<EvaluationRun>>().Setup(q => q.GetEnumerator()).Returns(mock.GetEnumerator());
    }

    #endregion

    // -------------------------------------------------------------------------
    // GetByIdAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_ExistingRun_ReturnsDetailDto()
    {
        // Arrange
        var run = MakeRun();
        _repository.Setup(r => r.GetAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        var svc = CreateService();

        // Act
        var result = await svc.GetByIdAsync(run.Id);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Id.ShouldBe(run.Id);
        result.Data.AgentId.ShouldBe(run.AgentId);
        result.Data.ResultsJson.ShouldBe(run.ResultsJson);
        result.Data.Status.ShouldBe(EvaluationRunStatus.Completed);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_Returns404()
    {
        // Arrange
        _repository.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EvaluationRun?)null);
        var svc = CreateService();

        // Act
        var result = await svc.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    // -------------------------------------------------------------------------
    // GetListAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetListAsync_FilterByAgentId_ReturnsMatchingRuns()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var runs = new List<EvaluationRun>
        {
            MakeRun(agentId: agentId, creationTime: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)),
            MakeRun(agentId: agentId, creationTime: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            MakeRun(agentId: Guid.NewGuid()) // 其他 Agent
        };
        SetupQueryable(runs);
        var svc = CreateService();

        // Act
        var result = await svc.GetListAsync(new EvaluationRunQueryDto
        {
            AgentId = agentId,
            PageIndex = 1,
            PageSize = 10
        });

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetListAsync_FilterByStatus_ReturnsMatchingRuns()
    {
        // Arrange
        var runs = new List<EvaluationRun>
        {
            MakeRun(status: EvaluationRunStatus.Running),
            MakeRun(status: EvaluationRunStatus.Completed),
            MakeRun(status: EvaluationRunStatus.Failed)
        };
        SetupQueryable(runs);
        var svc = CreateService();

        // Act
        var result = await svc.GetListAsync(new EvaluationRunQueryDto
        {
            Status = EvaluationRunStatus.Completed,
            PageIndex = 1,
            PageSize = 10
        });

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Items.Count.ShouldBe(1);
        result.Data.Items[0].Status.ShouldBe(EvaluationRunStatus.Completed);
    }

    // -------------------------------------------------------------------------
    // DeleteAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_ExistingRun_DeletesAndReturnsOk()
    {
        // Arrange
        var run = MakeRun();
        _repository.Setup(r => r.GetAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        _repository.Setup(r => r.DeleteAsync(run, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var svc = CreateService();

        // Act
        var result = await svc.DeleteAsync(run.Id);

        // Assert
        result.Succeeded.ShouldBeTrue();
        _repository.Verify(r => r.DeleteAsync(run, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_NotFound_Returns404()
    {
        // Arrange
        _repository.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EvaluationRun?)null);
        var svc = CreateService();

        // Act
        var result = await svc.DeleteAsync(Guid.NewGuid());

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    // -------------------------------------------------------------------------
    // RunBatchAsync - per-target DI scope isolation (B10)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunBatchAsync_ParallelTargets_NoDbContextConflict()
    {
        // Arrange: 5 targets so maxConcurrency=3 actually overlaps
        const int targetCount = 5;
        var targets = Enumerable.Range(0, targetCount)
            .Select(_ => new BatchEvaluationTargetDto { AgentId = Guid.NewGuid() })
            .ToList();

        var cases = new List<EvaluationCaseDto>
        {
            new() { Input = "q1", ExpectedOutput = "a1" }
        };

        var dto = new BatchEvaluationDto { Targets = targets, Cases = cases };

        // Track scopes created - each target must get its own
        var scopesCreated = 0;

        // Each scope resolves a fresh IEvaluationService backed by its own repo/evaluator
        _scopeFactory
            .Setup(f => f.CreateScope())
            .Returns(() =>
            {
                Interlocked.Increment(ref scopesCreated);

                var scopeRepo = new Mock<IRepository<EvaluationRun, Guid>>();
                scopeRepo
                    .Setup(r => r.InsertAsync(It.IsAny<EvaluationRun>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
                scopeRepo
                    .Setup(r => r.UpdateAsync(It.IsAny<EvaluationRun>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

                var scopeEvaluator = new Mock<IAgentEvaluator>();
                scopeEvaluator
                    .Setup(e => e.EvaluateAsync(It.IsAny<EvaluationCase>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new EvaluationResult { Passed = true, Score = 1.0 });

                var scopeServiceFactory = new Mock<IServiceScopeFactory>();
                var innerServices = new ServiceCollection();
                innerServices.AddLogging();
                var innerSp = innerServices.BuildServiceProvider();

                var scopedSvc = new EvaluationService(innerSp, scopeRepo.Object, scopeEvaluator.Object, scopeServiceFactory.Object);

                var scopeServiceProvider = new Mock<IServiceProvider>();
                scopeServiceProvider
                    .Setup(sp => sp.GetService(typeof(IEvaluationService)))
                    .Returns(scopedSvc);

                var scope = new Mock<IServiceScope>();
                scope.Setup(s => s.ServiceProvider).Returns(scopeServiceProvider.Object);
                return scope.Object;
            });

        var svc = CreateService();

        // Act - should not throw InvalidOperationException from concurrent EF context use
        var exception = await Record.ExceptionAsync(() => svc.RunBatchAsync(dto));

        // Assert
        exception.ShouldBeNull();
        scopesCreated.ShouldBe(targetCount);
    }
}
