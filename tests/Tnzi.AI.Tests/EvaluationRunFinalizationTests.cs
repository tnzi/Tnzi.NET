namespace Tnzi.AI.Tests;

/// <summary>
/// 评估运行的收尾：取消不能把行留在 Running，批量评估不能把失败的目标从结果里抹掉。
/// </summary>
public class EvaluationRunFinalizationTests
{
    public EvaluationRunFinalizationTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    [Fact]
    public async Task CreateAndRunAsync_CancelledMidway_FinalizesTheRunInsteadOfLeavingItRunning()
    {
        // 管理员关掉页面：请求的取消令牌在第二个用例之前触发
        using var cts = new CancellationTokenSource();
        var evaluator = new Mock<IAgentEvaluator>();
        evaluator.Setup(e => e.EvaluateAsync(It.IsAny<EvaluationCase>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                cts.Cancel();
                return new EvaluationResult { Passed = true, Score = 1 };
            });
        var repository = new RecordingRunRepository();
        var service = CreateService(repository.Mock.Object, evaluator.Object);

        await Should.ThrowAsync<OperationCanceledException>(() => service.CreateAndRunAsync(TwoCases(Guid.NewGuid()), cts.Token));

        repository.LastWritten.ShouldNotBeNull();
        repository.LastWritten.Status.ShouldBe(EvaluationRunStatus.Failed, "a cancelled run must not stay Running forever");
        repository.LastWritten.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
        repository.UpdateTokens.ShouldAllBe(t => !t.IsCancellationRequested, "finalizing must not use the cancelled request token");
    }

    [Fact]
    public async Task CreateAndRunAsync_EvaluatorThrowsInternalCancellation_FinalizesAsFailed()
    {
        // 内部 HTTP 超时等以 OperationCanceledException 冒出来，但请求本身没被取消：那是失败
        var evaluator = new Mock<IAgentEvaluator>();
        evaluator.Setup(e => e.EvaluateAsync(It.IsAny<EvaluationCase>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("provider timed out"));
        var repository = new RecordingRunRepository();
        var service = CreateService(repository.Mock.Object, evaluator.Object);

        var result = await service.CreateAndRunAsync(TwoCases(Guid.NewGuid()));

        result.Succeeded.ShouldBeFalse();
        repository.LastWritten!.Status.ShouldBe(EvaluationRunStatus.Failed);
    }

    [Fact]
    public async Task RunBatchAsync_OneTargetFails_ReportsItAsAFailure()
    {
        var healthy = Guid.NewGuid();
        var broken = Guid.NewGuid();
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(() =>
        {
            var evaluator = new Mock<IAgentEvaluator>();
            evaluator.Setup(e => e.EvaluateAsync(It.IsAny<EvaluationCase>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((EvaluationCase c, CancellationToken _) => c.AgentId == broken
                    ? throw new InvalidOperationException("agent is disabled")
                    : new EvaluationResult { Passed = true, Score = 1 });
            var scoped = CreateService(new RecordingRunRepository().Mock.Object, evaluator.Object);

            var provider = new Mock<IServiceProvider>();
            provider.Setup(p => p.GetService(typeof(IEvaluationService))).Returns(scoped);
            var scope = new Mock<IServiceScope>();
            scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
            return scope.Object;
        });
        var service = CreateService(new RecordingRunRepository().Mock.Object, Mock.Of<IAgentEvaluator>(), scopeFactory.Object);

        var result = await service.RunBatchAsync(new BatchEvaluationDto
        {
            Targets =
            [
                new BatchEvaluationTargetDto { AgentId = healthy },
                new BatchEvaluationTargetDto { AgentId = broken, VersionNumber = 3 }
            ],
            Cases = [new EvaluationCaseDto { Input = "q", ExpectedOutput = "a" }]
        });

        result.Succeeded.ShouldBeTrue();
        result.Data!.Results.Select(r => r.AgentId).ShouldBe([healthy]);
        var failure = result.Data.Failures.ShouldHaveSingleItem();
        failure.AgentId.ShouldBe(broken);
        failure.VersionNumber.ShouldBe(3);
        failure.Message.ShouldContain("agent is disabled");
    }

    private static CreateEvaluationRunDto TwoCases(Guid agentId) => new()
    {
        AgentId = agentId,
        Cases =
        [
            new EvaluationCaseDto { Input = "q1", ExpectedOutput = "a1" },
            new EvaluationCaseDto { Input = "q2", ExpectedOutput = "a2" }
        ]
    };

    private static EvaluationService CreateService(
        IRepository<EvaluationRun, Guid> repository,
        IAgentEvaluator evaluator,
        IServiceScopeFactory? scopeFactory = null)
        => new(
            new ServiceCollection().AddLogging().BuildServiceProvider(),
            repository,
            evaluator,
            scopeFactory ?? Mock.Of<IServiceScopeFactory>());

    /// <summary>记下最后一次写入的行与每次更新用的令牌。</summary>
    private sealed class RecordingRunRepository
    {
        public RecordingRunRepository()
        {
            Mock.Setup(r => r.InsertAsync(It.IsAny<EvaluationRun>(), It.IsAny<CancellationToken>()))
                .Callback<EvaluationRun, CancellationToken>((run, _) =>
                {
                    run.Id = Guid.NewGuid();
                    LastWritten = Snapshot(run);
                })
                .Returns(Task.CompletedTask);
            Mock.Setup(r => r.UpdateAsync(It.IsAny<EvaluationRun>(), It.IsAny<CancellationToken>()))
                .Callback<EvaluationRun, CancellationToken>((run, token) =>
                {
                    UpdateTokens.Add(token);
                    LastWritten = Snapshot(run);
                })
                .Returns(Task.CompletedTask);
        }

        public Mock<IRepository<EvaluationRun, Guid>> Mock { get; } = new();
        public EvaluationRun? LastWritten { get; private set; }
        public List<CancellationToken> UpdateTokens { get; } = [];

        private static EvaluationRun Snapshot(EvaluationRun run) => new()
        {
            Id = run.Id,
            AgentId = run.AgentId,
            Status = run.Status,
            Duration = run.Duration,
            CaseCount = run.CaseCount
        };
    }
}
