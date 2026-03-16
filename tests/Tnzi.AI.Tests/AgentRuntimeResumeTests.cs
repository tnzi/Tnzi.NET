
namespace Tnzi.AI.Tests;

public class AgentRuntimeResumeTests
{
    [Fact]
    public async Task ResumeAsync_WorkflowRun_ApprovesAwaitingSteps_AndDelegatesToWorkflowService()
    {
        var runId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var run = new AgentRun
        {
            Id = runId,
            ThreadId = threadId,
            WorkflowDefinitionId = workflowId,
            WorkflowExecutionId = "wf-exec-001",
            Status = AgentRunStatus.AwaitingApproval,
            Nodes =
            [
                new AgentRunNode
                {
                    Id = Guid.NewGuid(),
                    RunId = runId,
                    NodeName = "approval-step",
                    NodeType = "approval",
                    Status = AgentRunNodeStatus.AwaitingApproval
                }
            ]
        };

        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.GetWithNodesAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);
        runStore.Setup(x => x.UpdateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        runStore.Setup(x => x.UpdateNodeAsync(It.IsAny<AgentRunNode>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var traceStore = new Mock<ITraceStore>();
        traceStore.Setup(x => x.AddAsync(It.IsAny<AgentRunTrace>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRunTrace trace, CancellationToken _) => trace);

        var workflowService = new Mock<IWorkflowService>();
        workflowService.Setup(x => x.ApproveStepAsync("wf-exec-001", "approval-step", "approved", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        workflowService.Setup(x => x.ResumeAsync("wf-exec-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkflowExecutionResultDto>.Success(new WorkflowExecutionResultDto
            {
                ExecutionId = "wf-exec-001",
                Output = "workflow completed",
                Status = "Completed"
            }));

        var runtime = new AgentRuntime(
            Mock.Of<IAgentResolver>(),
            Mock.Of<IAgentFactory>(),
            Mock.Of<IRepository<Agent, Guid>>(),
            runStore.Object,
            traceStore.Object,
            workflowService.Object,
            new AgentExecutionContextAccessor(),
            new ServiceCollection().BuildServiceProvider(),
            Mock.Of<ILogger<AgentRuntime>>());

        var result = await runtime.ResumeAsync(runId, new ResumeRunInput
        {
            ApprovalDecision = "approve",
            ApprovalComment = "approved"
        });

        result.Response.ShouldBe("workflow completed");
        result.RunId.ShouldBe(runId);
        result.Status.ShouldBe(AgentRunStatus.Completed);
        run.Status.ShouldBe(AgentRunStatus.Completed);
        run.Nodes.Single().Status.ShouldBe(AgentRunNodeStatus.Approved);

        workflowService.Verify(x => x.ApproveStepAsync("wf-exec-001", "approval-step", "approved", It.IsAny<CancellationToken>()), Times.Once);
        workflowService.Verify(x => x.ResumeAsync("wf-exec-001", It.IsAny<CancellationToken>()), Times.Once);
    }
}
