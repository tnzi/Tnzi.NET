using Microsoft.Extensions.DependencyInjection;

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

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(workflowControl.Object);
        var dispatcher = new AgentRunSignalDispatcher(
            runStore.Object,
            Mock.Of<IAgentRunService>(),
            services.BuildServiceProvider());

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

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(workflowControl.Object);
        var dispatcher = new AgentRunSignalDispatcher(
            runStore.Object,
            runService.Object,
            services.BuildServiceProvider());

        var result = await dispatcher.CancelAsync(runId);

        result.Succeeded.ShouldBeTrue();
        runService.Verify(x => x.CancelAsync(runId), Times.Once);
        workflowControl.Verify(x => x.CancelAsync(executionId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
