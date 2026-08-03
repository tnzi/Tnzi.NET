namespace Tnzi.AI.Tests;

public class AgentRunServiceRetryTests
{
    [Fact]
    public async Task CancelAsync_RequiresClarificationRun_AllowsCancellation()
    {
        var runId = Guid.NewGuid();

        var runRepository = new Mock<IRepository<AgentRun, Guid>>();
        runRepository.Setup(x => x.GetAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRun
            {
                Id = runId,
                Status = AgentRunStatus.RequiresClarification
            });
        runRepository.Setup(x => x.UpdateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new AgentRunService(
            runRepository.Object,
            new Mock<IRepository<AgentRunNode, Guid>>().Object,
            Mock.Of<IAgentRuntime>(),
            new ServiceCollection().AddLogging().BuildServiceProvider());

        var result = await service.CancelAsync(runId);

        result.Succeeded.ShouldBeTrue();
        runRepository.Verify(x => x.UpdateAsync(
            It.Is<AgentRun>(run => run.Status == AgentRunStatus.Cancelled),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RetryNodeAsync_FailedNode_DelegatesToRuntimeResume()
    {
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();

        var runRepository = new Mock<IRepository<AgentRun, Guid>>();
        runRepository.Setup(x => x.GetAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRun
            {
                Id = runId,
                Status = AgentRunStatus.Failed
            });

        var nodeRepository = new Mock<IRepository<AgentRunNode, Guid>>();
        nodeRepository.Setup(x => x.GetAsync(nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunNode
            {
                Id = nodeId,
                RunId = runId,
                Status = AgentRunNodeStatus.Failed
            });

        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(x => x.ResumeAsync(runId, It.IsAny<ResumeRunInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult
            {
                Response = "retried",
                RunId = runId,
                Status = AgentRunStatus.Completed
            });

        var services = new ServiceCollection();
        services.AddLogging();

        var service = new AgentRunService(
            runRepository.Object,
            nodeRepository.Object,
            runtime.Object,
            services.BuildServiceProvider());

        var result = await service.RetryNodeAsync(runId, nodeId);

        result.Succeeded.ShouldBeTrue();
        runtime.Verify(x => x.ResumeAsync(
            runId,
            It.Is<ResumeRunInput>(i => i.RetryNodeId == nodeId),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResumeAsync_RequiresClarificationRun_DelegatesToRuntimeAndMapsResponse()
    {
        var runId = Guid.NewGuid();
        var runRepository = new Mock<IRepository<AgentRun, Guid>>();
        var nodeRepository = new Mock<IRepository<AgentRunNode, Guid>>();
        var runtime = new Mock<IAgentRuntime>();

        runtime.Setup(x => x.ResumeAsync(runId, It.IsAny<ResumeRunInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult
            {
                Response = "Need one more detail",
                RunId = runId,
                FinishReason = FinishReasons.RequiresClarification,
                Status = AgentRunStatus.RequiresClarification,
                Model = "gpt-5",
                Provider = "openai",
                Suggestions = ["Option A"],
                Artifacts = [new AgentArtifactDto { FileName = "draft.md", VirtualPath = "/artifacts/draft.md" }],
                ClarificationQuestion = "Which environment should I target?"
            });

        var service = new AgentRunService(
            runRepository.Object,
            nodeRepository.Object,
            runtime.Object,
            new ServiceCollection().AddLogging().BuildServiceProvider());

        var result = await service.ResumeAsync(runId, new ResumeRunInput
        {
            WorkflowInput = new Dictionary<string, object> { ["target"] = "prod" }
        });

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Status.ShouldBe(AgentRunStatus.RequiresClarification);
        result.Data.Provider.ShouldBe("openai");
        result.Data.ClarificationQuestion.ShouldBe("Which environment should I target?");
        result.Data.Suggestions!.ShouldContain("Option A");
        result.Data.Artifacts.ShouldNotBeNull();
    }
}
