using System.Runtime.CompilerServices;

namespace Tnzi.AI.Tests;

public class WorkflowDelegatorTests
{
    // ============== Mapping methods (instance, per refactor) ==============

    [Theory]
    [InlineData("Completed", true)]
    [InlineData("Failed", true)]
    [InlineData("Cancelled", true)]
    [InlineData("AwaitingInput", true)]
    [InlineData("AwaitingApproval", true)]
    [InlineData("PartialFailure(step-a)", true)]
    [InlineData("Running", false)]
    [InlineData("Step 'a'", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsTerminalStatus_ReturnsExpected(string? status, bool expected)
    {
        var delegator = CreateDelegator();
        delegator.IsTerminalStatus(status).ShouldBe(expected);
    }

    [Theory]
    [InlineData("AwaitingInput", AgentRunStatus.RequiresClarification)]
    [InlineData("AwaitingApproval", AgentRunStatus.AwaitingApproval)]
    [InlineData("Cancelled", AgentRunStatus.Cancelled)]
    [InlineData("Failed", AgentRunStatus.Failed)]
    [InlineData("PartialFailure(step-b)", AgentRunStatus.Failed)]
    [InlineData("Completed", AgentRunStatus.Completed)]
    [InlineData(null, AgentRunStatus.Completed)]
    [InlineData("Unknown", AgentRunStatus.Completed)]
    public void MapStatus_ReturnsExpected(string? status, AgentRunStatus expected)
    {
        var delegator = CreateDelegator();
        delegator.MapStatus(status).ShouldBe(expected);
    }

    [Theory]
    [InlineData("AwaitingInput", false, FinishReasons.RequiresClarification)]
    [InlineData("AwaitingApproval", false, FinishReasons.AwaitingApproval)]
    [InlineData("Cancelled", false, FinishReasons.Cancelled)]
    [InlineData("Failed", false, FinishReasons.Failed)]
    [InlineData("Completed", false, FinishReasons.Completed)]
    [InlineData("Completed", true, FinishReasons.Stop)]
    [InlineData(null, true, FinishReasons.Stop)]
    public void MapFinishReason_HonorsStreamingFlag(string? status, bool streaming, string expected)
    {
        var delegator = CreateDelegator();
        delegator.MapFinishReason(status, streaming).ShouldBe(expected);
    }

    // ============== ExecuteWorkflowAsync ==============

    [Fact]
    public async Task ExecuteWorkflowAsync_Completed_ReturnsCompletedResult()
    {
        var workflowId = Guid.NewGuid();
        var runId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var workflowService = new Mock<IWorkflowService>();
        workflowService.Setup(x => x.RunAsync(workflowId, "hello", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkflowExecutionResultDto>.Success(new WorkflowExecutionResultDto
            {
                ExecutionId = "wf-1",
                RunId = runId,
                Output = "result",
                Status = "Completed"
            }));

        var delegator = CreateDelegator(workflowService.Object);

        var result = await delegator.ExecuteWorkflowAsync(
            new AgentRunRequest { WorkflowId = workflowId, UserMessage = "hello" },
            CancellationToken.None);

        result.Response.ShouldBe("result");
        result.RunId.ShouldBe(runId);
        result.Status.ShouldBe(AgentRunStatus.Completed);
        result.FinishReason.ShouldBe(FinishReasons.Completed);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_FailedResult_ThrowsBusinessException()
    {
        var workflowId = Guid.NewGuid();
        var workflowService = new Mock<IWorkflowService>();
        workflowService.Setup(x => x.RunAsync(workflowId, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkflowExecutionResultDto>.Failure("wf boom", 500, ErrorCodes.WorkflowFailed));

        var delegator = CreateDelegator(workflowService.Object);

        var ex = await Should.ThrowAsync<Tnzi.Exceptions.BusinessException>(() =>
            delegator.ExecuteWorkflowAsync(
                new AgentRunRequest { WorkflowId = workflowId, UserMessage = "hi" },
                CancellationToken.None));

        ex.Code.ShouldBe(ErrorCodes.WorkflowFailed);
    }

    // ============== ExecuteWorkflowStreamingAsync ==============

    [Fact]
    public async Task ExecuteWorkflowStreamingAsync_PassesThroughTextChunksAndTagsTerminalFinishReason()
    {
        var workflowId = Guid.NewGuid();
        var workflowService = new Mock<IWorkflowService>();
        workflowService.Setup(x => x.RunStreamingAsync(workflowId, "hi", null, It.IsAny<CancellationToken>()))
            .Returns(Stream());

        var delegator = CreateDelegator(workflowService.Object);
        var chunks = new List<AgentStreamChunk>();

        await foreach (var c in delegator.ExecuteWorkflowStreamingAsync(
            new AgentRunRequest { WorkflowId = workflowId, UserMessage = "hi" }, CancellationToken.None))
        {
            chunks.Add(c);
        }

        chunks.Count.ShouldBe(2);
        chunks[0].Text.ShouldBe("step");
        chunks[0].FinishReason.ShouldBeNull();
        chunks[1].Text.ShouldBe("done");
        chunks[1].FinishReason.ShouldBe(FinishReasons.Stop);

        static async IAsyncEnumerable<WorkflowExecutionResultDto> Stream()
        {
            yield return new WorkflowExecutionResultDto { ExecutionId = "w", Output = "step", Status = "Running" };
            await Task.Yield();
            yield return new WorkflowExecutionResultDto { ExecutionId = "w", Output = "done", Status = "Completed" };
        }
    }

    // ============== ResumeWorkflowRunAsync - reject path ==============

    [Fact]
    public async Task ResumeWorkflowRunAsync_Reject_MarksAwaitingNodesAndRunFailed()
    {
        var workflowService = new Mock<IWorkflowService>();
        workflowService.Setup(x => x.RejectStepAsync(It.IsAny<string>(), "gate", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkflowExecutionResultDto>.Success(new WorkflowExecutionResultDto { Status = "Cancelled" }));

        var updated = new List<AgentRun>();
        var updatedNodes = new List<AgentRunNode>();
        var runStore = CreateRunStore(updated, updatedNodes);
        var runTracker = new RunTracker(runStore.Object, CreateTraceStore().Object, Mock.Of<ILogger<RunTracker>>());

        var delegator = new WorkflowDelegator(workflowService.Object, runStore.Object, runTracker,
            Mock.Of<ILogger<WorkflowDelegator>>());

        var run = new AgentRun
        {
            Id = Guid.NewGuid(),
            WorkflowExecutionId = "exec-1",
            WorkflowDefinitionId = Guid.NewGuid(),
            Status = AgentRunStatus.AwaitingApproval,
            Nodes = new List<AgentRunNode>
            {
                new() { Id = Guid.NewGuid(), NodeName = "gate", Status = AgentRunNodeStatus.AwaitingApproval }
            }
        };

        var result = await delegator.ResumeWorkflowRunAsync(run,
            new ResumeRunInput { ApprovalDecision = "reject", ApprovalComment = "nope" },
            CancellationToken.None);

        result.FinishReason.ShouldBe(FinishReasons.Rejected);
        result.Status.ShouldBe(AgentRunStatus.Failed);
        updatedNodes.Any(n => n.Status == AgentRunNodeStatus.Rejected).ShouldBeTrue();
        updated.Last().Status.ShouldBe(AgentRunStatus.Failed);
    }

    // ============== ResumeWorkflowRunAsync - approve path ==============

    [Fact]
    public async Task ResumeWorkflowRunAsync_Approve_ApprovesNodesAndResumesWorkflow()
    {
        var workflowService = new Mock<IWorkflowService>();
        workflowService.Setup(x => x.ApproveStepAsync(It.IsAny<string>(), "gate", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkflowExecutionResultDto>.Success(new WorkflowExecutionResultDto { Status = "Running" }));
        workflowService.Setup(x => x.ResumeAsync("exec-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkflowExecutionResultDto>.Success(new WorkflowExecutionResultDto
            {
                ExecutionId = "exec-2",
                Output = "final",
                Status = "Completed"
            }));

        var updated = new List<AgentRun>();
        var runStore = CreateRunStore(updated, new List<AgentRunNode>());
        var runTracker = new RunTracker(runStore.Object, CreateTraceStore().Object, Mock.Of<ILogger<RunTracker>>());

        var delegator = new WorkflowDelegator(workflowService.Object, runStore.Object, runTracker,
            Mock.Of<ILogger<WorkflowDelegator>>());

        var run = new AgentRun
        {
            Id = Guid.NewGuid(),
            WorkflowExecutionId = "exec-2",
            WorkflowDefinitionId = Guid.NewGuid(),
            Status = AgentRunStatus.AwaitingApproval,
            Nodes = new List<AgentRunNode>
            {
                new() { Id = Guid.NewGuid(), NodeName = "gate", Status = AgentRunNodeStatus.AwaitingApproval }
            }
        };

        var result = await delegator.ResumeWorkflowRunAsync(run,
            new ResumeRunInput { ApprovalDecision = "approve" }, CancellationToken.None);

        result.Response.ShouldBe("final");
        result.Status.ShouldBe(AgentRunStatus.Completed);
        result.FinishReason.ShouldBe(FinishReasons.Completed);
        workflowService.Verify(x => x.ResumeAsync("exec-2", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ============== ResumeWorkflowRunAsync - RequiresClarification path ==============

    [Fact]
    public async Task ResumeWorkflowRunAsync_RequiresClarification_WithoutInput_ThrowsBusinessException()
    {
        var workflowService = new Mock<IWorkflowService>();
        var runStore = CreateRunStore(new List<AgentRun>(), new List<AgentRunNode>());
        var runTracker = new RunTracker(runStore.Object, CreateTraceStore().Object, Mock.Of<ILogger<RunTracker>>());
        var delegator = new WorkflowDelegator(workflowService.Object, runStore.Object, runTracker,
            Mock.Of<ILogger<WorkflowDelegator>>());

        var run = new AgentRun
        {
            Id = Guid.NewGuid(),
            WorkflowExecutionId = "exec-3",
            WorkflowDefinitionId = Guid.NewGuid(),
            Status = AgentRunStatus.RequiresClarification,
            Nodes = new List<AgentRunNode>()
        };

        var ex = await Should.ThrowAsync<Tnzi.Exceptions.BusinessException>(() =>
            delegator.ResumeWorkflowRunAsync(run, new ResumeRunInput(), CancellationToken.None));

        ex.Code.ShouldBe(ErrorCodes.RunInvalidState);
    }

    // ============== Helpers ==============

    private static WorkflowDelegator CreateDelegator(IWorkflowService? service = null)
    {
        var runStore = CreateRunStore(new List<AgentRun>(), new List<AgentRunNode>());
        var runTracker = new RunTracker(runStore.Object, CreateTraceStore().Object, Mock.Of<ILogger<RunTracker>>());
        return new WorkflowDelegator(service ?? Mock.Of<IWorkflowService>(),
            runStore.Object, runTracker, Mock.Of<ILogger<WorkflowDelegator>>());
    }

    private static Mock<IRunStore> CreateRunStore(List<AgentRun> updatedRuns, List<AgentRunNode> updatedNodes)
    {
        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.UpdateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
            .Callback<AgentRun, CancellationToken>((r, _) => updatedRuns.Add(CloneRun(r)))
            .Returns(Task.CompletedTask);
        runStore.Setup(x => x.UpdateNodeAsync(It.IsAny<AgentRunNode>(), It.IsAny<CancellationToken>()))
            .Callback<AgentRunNode, CancellationToken>((n, _) => updatedNodes.Add(CloneNode(n)))
            .Returns(Task.CompletedTask);
        return runStore;
    }

    private static Mock<ITraceStore> CreateTraceStore()
    {
        var traceStore = new Mock<ITraceStore>();
        traceStore.Setup(x => x.AddAsync(It.IsAny<AgentRunTrace>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRunTrace trace, CancellationToken _) => trace);
        return traceStore;
    }

    private static AgentRun CloneRun(AgentRun r) => new()
    {
        Id = r.Id,
        Status = r.Status,
        OutputSummary = r.OutputSummary,
        Error = r.Error,
        WorkflowExecutionId = r.WorkflowExecutionId,
        WorkflowDefinitionId = r.WorkflowDefinitionId
    };

    private static AgentRunNode CloneNode(AgentRunNode n) => new()
    {
        Id = n.Id,
        NodeName = n.NodeName,
        Status = n.Status,
        Output = n.Output,
        Error = n.Error,
        RetryCount = n.RetryCount
    };
}
