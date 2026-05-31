namespace Tnzi.AI.Tests.Services;

/// <summary>
/// EvaluationMetricsService 单元测试 — 覆盖 trend / version comparison / batch / empty data
/// </summary>
public class EvaluationMetricsServiceTests
{
    #region Helpers

    private readonly Mock<IRepository<EvaluationRun, Guid>> _repository = new();
    private readonly Mock<IAgentEvaluator> _evaluator = new();
    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
    private readonly IServiceProvider _serviceProvider;

    public EvaluationMetricsServiceTests()
    {
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();

        // Default scope factory: each scope resolves a fresh EvaluationService with
        // the same mocked repository/evaluator so RunBatchAsync tests keep working.
        _scopeFactory
            .Setup(f => f.CreateScope())
            .Returns(() =>
            {
                var noopScopeFactory = new Mock<IServiceScopeFactory>();
                var scopedSvc = new EvaluationService(_serviceProvider, _repository.Object, _evaluator.Object, noopScopeFactory.Object);
                var sp = new Mock<IServiceProvider>();
                sp.Setup(p => p.GetService(typeof(IEvaluationService))).Returns(scopedSvc);
                var scope = new Mock<IServiceScope>();
                scope.Setup(s => s.ServiceProvider).Returns(sp.Object);
                return scope.Object;
            });
    }

    private EvaluationMetricsService CreateMetricsService() => new(_serviceProvider, _repository.Object);
    private EvaluationService CreateEvaluationService() => new(_serviceProvider, _repository.Object, _evaluator.Object, _scopeFactory.Object);

    private static EvaluationRun MakeRun(
        Guid? id = null,
        Guid? agentId = null,
        int? versionNumber = null,
        EvaluationRunStatus status = EvaluationRunStatus.Completed,
        int caseCount = 5,
        int passedCount = 4,
        double averageScore = 0.8,
        DateTime? creationTime = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        AgentId = agentId ?? Guid.NewGuid(),
        AgentVersionNumber = versionNumber,
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
    // GetTrendAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetTrendAsync_WithMultipleRuns_ReturnsSortedTrend()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var runs = new List<EvaluationRun>
        {
            MakeRun(agentId: agentId, averageScore: 0.6, caseCount: 10, passedCount: 6,
                creationTime: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            MakeRun(agentId: agentId, averageScore: 0.7, caseCount: 10, passedCount: 7,
                creationTime: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)),
            MakeRun(agentId: agentId, averageScore: 0.9, caseCount: 10, passedCount: 9,
                creationTime: new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc)),
            MakeRun(agentId: Guid.NewGuid(), averageScore: 0.5) // 其他 Agent
        };
        SetupQueryable(runs);
        var svc = CreateMetricsService();

        // Act
        var result = await svc.GetTrendAsync(agentId, 10);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.AgentId.ShouldBe(agentId);
        result.Data.Points.Count.ShouldBe(3);
        // 按时间正序（从旧到新）
        result.Data.Points[0].Score.ShouldBe(0.6);
        result.Data.Points[1].Score.ShouldBe(0.7);
        result.Data.Points[2].Score.ShouldBe(0.9);
        result.Data.Points[0].PassRate.ShouldBe(0.6);
    }

    [Fact]
    public async Task GetTrendAsync_EmptyData_ReturnsEmptyPoints()
    {
        // Arrange
        SetupQueryable([]);
        var svc = CreateMetricsService();

        // Act
        var result = await svc.GetTrendAsync(Guid.NewGuid(), 5);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Points.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetTrendAsync_InvalidLastNRuns_Returns400()
    {
        // Arrange
        var svc = CreateMetricsService();

        // Act
        var result = await svc.GetTrendAsync(Guid.NewGuid(), 0);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task GetTrendAsync_OnlyCompletedRuns_ExcludesRunningAndFailed()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var runs = new List<EvaluationRun>
        {
            MakeRun(agentId: agentId, status: EvaluationRunStatus.Running, averageScore: 0.1),
            MakeRun(agentId: agentId, status: EvaluationRunStatus.Completed, averageScore: 0.8),
            MakeRun(agentId: agentId, status: EvaluationRunStatus.Failed, averageScore: 0.2)
        };
        SetupQueryable(runs);
        var svc = CreateMetricsService();

        // Act
        var result = await svc.GetTrendAsync(agentId, 10);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.Points.Count.ShouldBe(1);
        result.Data.Points[0].Score.ShouldBe(0.8);
    }

    // -------------------------------------------------------------------------
    // CompareVersionsAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CompareVersionsAsync_TwoVersions_ReturnsComparison()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var runs = new List<EvaluationRun>
        {
            MakeRun(agentId: agentId, versionNumber: 1, averageScore: 0.6, caseCount: 10, passedCount: 6),
            MakeRun(agentId: agentId, versionNumber: 1, averageScore: 0.7, caseCount: 10, passedCount: 7),
            MakeRun(agentId: agentId, versionNumber: 2, averageScore: 0.9, caseCount: 10, passedCount: 9),
            MakeRun(agentId: agentId, versionNumber: 2, averageScore: 0.85, caseCount: 10, passedCount: 8)
        };
        SetupQueryable(runs);
        var svc = CreateMetricsService();

        // Act
        var result = await svc.CompareVersionsAsync(agentId, 1, 2);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.AgentId.ShouldBe(agentId);
        result.Data.VersionA.VersionNumber.ShouldBe(1);
        result.Data.VersionA.RunCount.ShouldBe(2);
        result.Data.VersionA.AverageScore.ShouldBe(0.65, 0.001);
        result.Data.VersionB.VersionNumber.ShouldBe(2);
        result.Data.VersionB.RunCount.ShouldBe(2);
        result.Data.VersionB.AverageScore.ShouldBe(0.875, 0.001);
        result.Data.ScoreDelta.ShouldBeGreaterThan(0);
        result.Data.Winner.ShouldBe(2);
    }

    [Fact]
    public async Task CompareVersionsAsync_SameVersion_Returns400()
    {
        // Arrange
        var svc = CreateMetricsService();

        // Act
        var result = await svc.CompareVersionsAsync(Guid.NewGuid(), 1, 1);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task CompareVersionsAsync_NoRunsForEitherVersion_Returns404()
    {
        // Arrange
        SetupQueryable([]);
        var svc = CreateMetricsService();

        // Act
        var result = await svc.CompareVersionsAsync(Guid.NewGuid(), 1, 2);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task CompareVersionsAsync_OneVersionEmpty_ReturnsNoWinner()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var runs = new List<EvaluationRun>
        {
            MakeRun(agentId: agentId, versionNumber: 1, averageScore: 0.8, caseCount: 10, passedCount: 8)
        };
        SetupQueryable(runs);
        var svc = CreateMetricsService();

        // Act
        var result = await svc.CompareVersionsAsync(agentId, 1, 2);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.VersionA.RunCount.ShouldBe(1);
        result.Data.VersionB.RunCount.ShouldBe(0);
        result.Data.Winner.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // CreateAndRunAsync (on EvaluationService)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAndRunAsync_SuccessfulCases_ReturnsCompletedRun()
    {
        // Arrange
        _evaluator.Setup(e => e.EvaluateAsync(It.IsAny<EvaluationCase>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EvaluationResult { Passed = true, Score = 0.9, CaseId = "1" });
        _repository.Setup(r => r.InsertAsync(It.IsAny<EvaluationRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repository.Setup(r => r.UpdateAsync(It.IsAny<EvaluationRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var svc = CreateEvaluationService();

        var dto = new CreateEvaluationRunDto
        {
            AgentId = Guid.NewGuid(),
            VersionNumber = 1,
            Cases = [new EvaluationCaseDto { Input = "Hello", ExpectedOutput = "Hi" }]
        };

        // Act
        var result = await svc.CreateAndRunAsync(dto);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.AgentId.ShouldBe(dto.AgentId);
        result.Data.Status.ShouldBe(EvaluationRunStatus.Completed);
        result.Data.CaseCount.ShouldBe(1);
        result.Data.PassedCount.ShouldBe(1);
        _repository.Verify(r => r.InsertAsync(It.IsAny<EvaluationRun>(), It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.UpdateAsync(It.IsAny<EvaluationRun>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAndRunAsync_EvaluatorThrows_ReturnsFailedRun()
    {
        // Arrange
        _evaluator.Setup(e => e.EvaluateAsync(It.IsAny<EvaluationCase>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Evaluator error"));
        _repository.Setup(r => r.InsertAsync(It.IsAny<EvaluationRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repository.Setup(r => r.UpdateAsync(It.IsAny<EvaluationRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var svc = CreateEvaluationService();

        var dto = new CreateEvaluationRunDto
        {
            AgentId = Guid.NewGuid(),
            Cases = [new EvaluationCaseDto { Input = "Hello" }]
        };

        // Act
        var result = await svc.CreateAndRunAsync(dto);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(500);
        // 验证 run 被更新为 Failed 状态
        _repository.Verify(r => r.UpdateAsync(
            It.Is<EvaluationRun>(run => run.Status == EvaluationRunStatus.Failed),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // -------------------------------------------------------------------------
    // RunBatchAsync (on EvaluationService)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunBatchAsync_MultipleTargets_RunsInParallel()
    {
        // Arrange
        _evaluator.Setup(e => e.EvaluateAsync(It.IsAny<EvaluationCase>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EvaluationResult { Passed = true, Score = 1.0, CaseId = "1" });
        _repository.Setup(r => r.InsertAsync(It.IsAny<EvaluationRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repository.Setup(r => r.UpdateAsync(It.IsAny<EvaluationRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var svc = CreateEvaluationService();

        var dto = new BatchEvaluationDto
        {
            Targets =
            [
                new BatchEvaluationTargetDto { AgentId = Guid.NewGuid(), VersionNumber = 1 },
                new BatchEvaluationTargetDto { AgentId = Guid.NewGuid(), VersionNumber = 2 }
            ],
            Cases = [new EvaluationCaseDto { Input = "Test input" }]
        };

        // Act
        var result = await svc.RunBatchAsync(dto);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Results.Count.ShouldBe(2);
        result.Data.TotalDuration.ShouldBeGreaterThan(TimeSpan.Zero);
    }
}
