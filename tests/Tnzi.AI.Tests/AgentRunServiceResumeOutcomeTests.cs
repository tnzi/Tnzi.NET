namespace Tnzi.AI.Tests;

/// <summary>
/// 审批、驳回与重试节点都经运行时续跑，续跑的失败以结果（FinishReason）而不是异常返回。
/// 端点必须按结果回答，不能续跑失败了还报 200。
/// </summary>
public class AgentRunServiceResumeOutcomeTests
{
    private readonly Guid _runId = Guid.NewGuid();
    private readonly Guid _nodeId = Guid.NewGuid();

    [Fact]
    public async Task ApproveAsync_ResumedRunFails_ReturnsTheFailure()
    {
        var service = CreateService(AgentRunStatus.AwaitingApproval, new AgentRunResult
        {
            Response = "the provider is unreachable",
            FinishReason = FinishReasons.Error,
            Status = AgentRunStatus.Failed
        });

        var result = await service.ApproveAsync(_runId, "ok");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(500);
        result.ErrorCode.ShouldBe(ErrorCodes.AgentRunFailed);
        result.Message.ShouldBe("the provider is unreachable");
    }

    [Fact]
    public async Task ApproveAsync_ResumedRunHitsTheQuota_Returns429()
    {
        var service = CreateService(AgentRunStatus.AwaitingApproval, new AgentRunResult
        {
            Response = "quota exceeded",
            FinishReason = FinishReasons.QuotaExceeded,
            Status = AgentRunStatus.Failed
        });

        var result = await service.ApproveAsync(_runId, null);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(429);
        result.ErrorCode.ShouldBe(ErrorCodes.QuotaExceeded);
    }

    [Fact]
    public async Task ApproveAsync_ResumedRunCompletes_Succeeds()
    {
        var service = CreateService(AgentRunStatus.AwaitingApproval, new AgentRunResult
        {
            Response = "done",
            FinishReason = FinishReasons.Stop,
            Status = AgentRunStatus.Completed
        });

        var result = await service.ApproveAsync(_runId, null);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task RejectAsync_RunEndsRejected_Succeeds()
    {
        // 驳回的正常结局就是 Rejected：那是这次操作的成功，不是失败
        var service = CreateService(AgentRunStatus.AwaitingApproval, new AgentRunResult
        {
            Response = "Rejected by reviewer",
            FinishReason = FinishReasons.Rejected,
            Status = AgentRunStatus.Failed
        });

        var result = await service.RejectAsync(_runId, "no");

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task RejectAsync_ResumedRunErrors_ReturnsTheFailure()
    {
        var service = CreateService(AgentRunStatus.AwaitingApproval, new AgentRunResult
        {
            Response = "workflow store unavailable",
            FinishReason = FinishReasons.Failed,
            Status = AgentRunStatus.Failed
        });

        var result = await service.RejectAsync(_runId, "no");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(500);
    }

    [Fact]
    public async Task RetryNodeAsync_NodeFailsAgain_ReturnsTheFailure()
    {
        var service = CreateService(AgentRunStatus.Failed, new AgentRunResult
        {
            Response = "node failed again",
            FinishReason = FinishReasons.Error,
            Status = AgentRunStatus.Failed
        });

        var result = await service.RetryNodeAsync(_runId, _nodeId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(500);
        result.Message.ShouldBe("node failed again");
    }

    private AgentRunService CreateService(AgentRunStatus runStatus, AgentRunResult resumeResult)
    {
        var runRepository = new Mock<IRepository<AgentRun, Guid>>();
        runRepository.Setup(x => x.GetAsync(_runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRun { Id = _runId, Status = runStatus });

        var nodeRepository = new Mock<IRepository<AgentRunNode, Guid>>();
        nodeRepository.Setup(x => x.GetAsync(_nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunNode { Id = _nodeId, RunId = _runId, Status = AgentRunNodeStatus.Failed });

        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(x => x.ResumeAsync(_runId, It.IsAny<ResumeRunInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resumeResult);

        return new AgentRunService(
            runRepository.Object,
            nodeRepository.Object,
            runtime.Object,
            new ServiceCollection().AddLogging().BuildServiceProvider());
    }
}
