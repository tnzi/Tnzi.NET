
namespace Tnzi.AI.Tests;

public class AgentRunServiceStatsTests
{
    [Fact]
    public async Task GetStatsAsync_ReturnsAggregatedRunStatistics()
    {
        var runRepository = new Mock<IRepository<AgentRun, Guid>>();
        var nodeRepository = new Mock<IRepository<AgentRunNode, Guid>>();

        runRepository.Setup(x => x.CountAsync((Expression<Func<AgentRun, bool>>?)null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);
        runRepository.Setup(x => x.CountAsync(
                It.Is<Expression<Func<AgentRun, bool>>>(predicate => predicate != null),
                It.IsAny<CancellationToken>()))
            .Returns<Expression<Func<AgentRun, bool>>?, CancellationToken>((predicate, _) =>
            {
                predicate.ShouldNotBeNull();
                var compiled = predicate!.Compile();

                return Task.FromResult(compiled(new AgentRun { Status = AgentRunStatus.Pending }) ? 1 :
                    compiled(new AgentRun { Status = AgentRunStatus.Running }) ? 2 :
                    compiled(new AgentRun { Status = AgentRunStatus.AwaitingApproval }) ? 1 :
                    compiled(new AgentRun { Status = AgentRunStatus.RequiresClarification }) ? 1 :
                    compiled(new AgentRun { Status = AgentRunStatus.Completed }) ? 4 :
                    compiled(new AgentRun { Status = AgentRunStatus.Failed }) ? 1 :
                    compiled(new AgentRun { Status = AgentRunStatus.Cancelled }) ? 1 : 0);
            });

        runRepository.Setup(x => x.SumAsync(
                It.IsAny<Expression<Func<AgentRun, decimal>>>(),
                null,
                It.IsAny<CancellationToken>()))
            .Returns<Expression<Func<AgentRun, decimal>>, Expression<Func<AgentRun, bool>>?, CancellationToken>((selector, _, _) =>
            {
                var compiled = selector.Compile();
                if (compiled(new AgentRun { TotalInputTokens = 1 }) == 1)
                {
                    return Task.FromResult(1234m);
                }

                return Task.FromResult(567m);
            });

        runRepository.Setup(x => x.AverageAsync(
                It.IsAny<Expression<Func<AgentRun, decimal>>>(),
                It.IsAny<Expression<Func<AgentRun, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(250m);

        var services = new ServiceCollection();
        services.AddLogging();

        var service = new AgentRunService(
            runRepository.Object,
            nodeRepository.Object,
            Mock.Of<IAgentRuntime>(),
            services.BuildServiceProvider());

        var result = await service.GetStatsAsync();

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.TotalRuns.ShouldBe(10);
        result.Data.PendingRuns.ShouldBe(1);
        result.Data.RunningRuns.ShouldBe(2);
        result.Data.AwaitingApprovalRuns.ShouldBe(1);
        result.Data.RequiresClarificationRuns.ShouldBe(1);
        result.Data.CompletedRuns.ShouldBe(4);
        result.Data.FailedRuns.ShouldBe(1);
        result.Data.CancelledRuns.ShouldBe(1);
        result.Data.TotalInputTokens.ShouldBe(1234);
        result.Data.TotalOutputTokens.ShouldBe(567);
        result.Data.AverageDurationMs.ShouldBe(250);
        result.Data.SuccessRate.ShouldBe(0.5714m);
    }
}
