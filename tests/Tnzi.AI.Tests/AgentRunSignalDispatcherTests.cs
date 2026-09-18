
namespace Tnzi.AI.Tests;

[ExperimentalApi(Reason = "Workflow mailbox and signals are in preview")]
public class AgentRunSignalDispatcherTests
{
    [Fact]
    public async Task DispatchInputAsync_WorkflowClarification_UsesWorkflowResumeWithInput()
    {
        var runId = Guid.NewGuid();
        var executionId = "wf-run-clarify";

        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.GetWithNodesAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRun
            {
                Id = runId,
                Status = AgentRunStatus.RequiresClarification,
                WorkflowDefinitionId = Guid.NewGuid(),
                WorkflowExecutionId = executionId
            });

        var workflowControl = new Mock<IWorkflowExecutionControlService>();
        workflowControl.Setup(x => x.ResumeWithInputAsync(
                executionId,
                "ask_user",
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new WorkflowExecutionResultDto
            {
                ExecutionId = executionId,
                Status = "Completed",
                Output = "done"
            }));

        var dispatcher = new AgentRunSignalDispatcher(
            runStore.Object,
            Mock.Of<IAgentRunService>(),
            workflowControl.Object,
            Mock.Of<IWorkflowExecutionQueryService>());

        var result = await dispatcher.DispatchInputAsync(runId, new SendAgentRunInput
        {
            WorkflowStepId = "ask_user",
            WorkflowInput = new Dictionary<string, object>
            {
                ["target"] = "prod"
            }
        });

        result.Succeeded.ShouldBeTrue();
        workflowControl.Verify(x => x.ResumeWithInputAsync(
            executionId,
            "ask_user",
            It.Is<Dictionary<string, object>>(payload => (string)payload["target"] == "prod"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelAsync_WorkflowRun_CancelsRunAndWorkflowExecution()
    {
        var runId = Guid.NewGuid();
        var executionId = "wf-run-cancel";

        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.GetWithNodesAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRun
            {
                Id = runId,
                Status = AgentRunStatus.Running,
                WorkflowDefinitionId = Guid.NewGuid(),
                WorkflowExecutionId = executionId
            });

        var runService = new Mock<IAgentRunService>();
        runService.Setup(x => x.CancelAsync(runId))
            .ReturnsAsync(Result.Success());

        var workflowControl = new Mock<IWorkflowExecutionControlService>();
        workflowControl.Setup(x => x.CancelAsync(executionId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var dispatcher = new AgentRunSignalDispatcher(
            runStore.Object,
            runService.Object,
            workflowControl.Object,
            Mock.Of<IWorkflowExecutionQueryService>());

        var result = await dispatcher.CancelAsync(runId);

        result.Succeeded.ShouldBeTrue();
        runService.Verify(x => x.CancelAsync(runId), Times.Once);
        workflowControl.Verify(x => x.CancelAsync(executionId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// The run row is only a projection of the execution: it is rewritten when the engine
    /// finishes or resumes, and rows persisted before 2026-09-12 carry <c>AwaitingApproval</c>
    /// for a HumanInput interrupt. The state endpoint already reads the execution and reports
    /// <c>canSendInput = true</c>; the dispatcher must agree with it.
    /// </summary>
    [Fact]
    public async Task DispatchInputAsync_RunRowIsStale_ExecutionAwaitingInput_UsesWorkflowResumeWithInput()
    {
        var runId = Guid.NewGuid();
        var executionId = "wf-run-stale-row";
        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.GetWithNodesAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRun
            {
                Id = runId,
                Status = AgentRunStatus.AwaitingApproval,
                WorkflowDefinitionId = Guid.NewGuid(),
                WorkflowExecutionId = executionId
            });
        var workflowQuery = new Mock<IWorkflowExecutionQueryService>();
        workflowQuery.Setup(x => x.GetExecutionStatusAsync(executionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new WorkflowExecutionStatusDto { ExecutionId = executionId, Status = "AwaitingInput" }));
        workflowQuery.Setup(x => x.GetPendingInterruptAsync(executionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new WorkflowInterruptDto { StepId = "ask_user" }));
        var workflowControl = new Mock<IWorkflowExecutionControlService>();
        workflowControl.Setup(x => x.ResumeWithInputAsync(executionId, "ask_user", It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new WorkflowExecutionResultDto { ExecutionId = executionId, Status = "Completed" }));

        var dispatcher = new AgentRunSignalDispatcher(runStore.Object, Mock.Of<IAgentRunService>(), workflowControl.Object, workflowQuery.Object);

        var result = await dispatcher.DispatchInputAsync(runId, new SendAgentRunInput
        {
            WorkflowInput = new Dictionary<string, object> { ["answer"] = "42" }
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        workflowControl.Verify(x => x.ResumeWithInputAsync(executionId, "ask_user", It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// ★ A workflow-backed run that is not waiting for input used to get a <c>resume_input</c>
    /// signal enqueued and <c>Result.Success()</c> back; the engine acknowledged and discarded
    /// the signal at the next layer boundary without reading it. <c>send_agent_input</c> told the
    /// caller the input was accepted while no node ever saw it. The contract is now honest:
    /// the only way input reaches a workflow node is <c>ResumeWithInputAsync</c> on a run that
    /// is awaiting it, and everything else is refused at the boundary.
    /// </summary>
    /// <remarks>
    /// "Awaiting it" is decided on the execution's real state, not on the run row alone
    /// (see <see cref="DispatchInputAsync_RunRowIsStale_ExecutionAwaitingInput_UsesWorkflowResumeWithInput"/>),
    /// so each case here pairs the row with a matching execution status. <c>AwaitingApproval</c>
    /// is refused only because the execution is waiting for an approval, not for input.
    /// </remarks>
    [Theory]
    [InlineData(AgentRunStatus.Running, "Running")]
    [InlineData(AgentRunStatus.AwaitingApproval, "AwaitingApproval")]
    [InlineData(AgentRunStatus.Completed, "Completed")]
    public async Task DispatchInputAsync_WorkflowRunNotAwaitingInput_IsRefused_AndNothingIsEnqueued(AgentRunStatus status, string executionStatus)
    {
        var runId = Guid.NewGuid();
        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.GetWithNodesAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRun
            {
                Id = runId,
                Status = status,
                WorkflowDefinitionId = Guid.NewGuid(),
                WorkflowExecutionId = "wf-run-busy"
            });
        var workflowControl = new Mock<IWorkflowExecutionControlService>(MockBehavior.Strict);
        var workflowQuery = new Mock<IWorkflowExecutionQueryService>();
        workflowQuery.Setup(x => x.GetExecutionStatusAsync("wf-run-busy", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new WorkflowExecutionStatusDto { ExecutionId = "wf-run-busy", Status = executionStatus }));

        var dispatcher = new AgentRunSignalDispatcher(
            runStore.Object,
            Mock.Of<IAgentRunService>(),
            workflowControl.Object,
            workflowQuery.Object);

        var result = await dispatcher.DispatchInputAsync(runId, new SendAgentRunInput
        {
            Message = "please use the other branch",
            WorkflowInput = new Dictionary<string, object> { ["answer"] = "42" }
        });

        result.Succeeded.ShouldBeFalse("the caller must not be told the input was accepted");
        result.Code.ShouldBe(409);
        result.ErrorCode.ShouldBe(ErrorCodes.RunInvalidState);
        result.Message.ShouldNotBeNull();
        result.Message!.ShouldContain(status.ToString());
        // The dispatcher no longer even holds a mailbox: there is nothing to enqueue.
        workflowControl.VerifyNoOtherCalls();
    }
}
