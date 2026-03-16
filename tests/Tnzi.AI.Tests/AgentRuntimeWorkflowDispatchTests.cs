
namespace Tnzi.AI.Tests;

public class AgentRuntimeWorkflowDispatchTests
{
    [Fact]
    public async Task RunAsync_WithWorkflowId_DelegatesToWorkflowService()
    {
        var workflowId = Guid.NewGuid();
        var workflowService = new Mock<IWorkflowService>();
        workflowService.Setup(x => x.RunAsync(workflowId, "workflow input", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkflowExecutionResultDto>.Success(new WorkflowExecutionResultDto
            {
                ExecutionId = "wf-001",
                RunId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Output = "workflow result",
                Status = "Completed"
            }));

        var runtime = CreateRuntime(workflowService.Object);

        var result = await runtime.RunAsync(new AgentRunRequest
        {
            WorkflowId = workflowId,
            UserMessage = "workflow input"
        });

        result.Response.ShouldBe("workflow result");
        result.RunId.ShouldBe(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        result.Status.ShouldBe(AgentRunStatus.Completed);
        result.FinishReason.ShouldBe("completed");

        workflowService.Verify(x => x.RunAsync(workflowId, "workflow input", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunStreamingAsync_WithWorkflowId_DelegatesToWorkflowService()
    {
        var workflowId = Guid.NewGuid();
        var workflowService = new Mock<IWorkflowService>();
        workflowService.Setup(x => x.RunStreamingAsync(workflowId, "workflow input", null, It.IsAny<CancellationToken>()))
            .Returns(StreamWorkflowResults());

        var runtime = CreateRuntime(workflowService.Object);
        var chunks = new List<AgentStreamChunk>();

        await foreach (var chunk in runtime.RunStreamingAsync(new AgentRunRequest
        {
            WorkflowId = workflowId,
            UserMessage = "workflow input"
        }))
        {
            chunks.Add(chunk);
        }

        chunks.Count.ShouldBe(2);
        chunks[0].Text.ShouldBe("step output");
        chunks[1].Text.ShouldBe("done");
        chunks[1].FinishReason.ShouldBe("stop");

        workflowService.Verify(x => x.RunStreamingAsync(workflowId, "workflow input", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WithWorkflowId_WhenWorkflowFails_MapsFailedStatus()
    {
        var workflowId = Guid.NewGuid();
        var workflowService = new Mock<IWorkflowService>();
        workflowService.Setup(x => x.RunAsync(workflowId, "workflow input", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkflowExecutionResultDto>.Success(new WorkflowExecutionResultDto
            {
                ExecutionId = "wf-failed-001",
                Output = "workflow failed",
                Status = "Failed"
            }));

        var runtime = CreateRuntime(workflowService.Object);

        var result = await runtime.RunAsync(new AgentRunRequest
        {
            WorkflowId = workflowId,
            UserMessage = "workflow input"
        });

        result.Response.ShouldBe("workflow failed");
        result.Status.ShouldBe(AgentRunStatus.Failed);
        result.FinishReason.ShouldBe("failed");
    }

    private static AgentRuntime CreateRuntime(IWorkflowService workflowService)
    {
        return new AgentRuntime(
            Mock.Of<IAgentResolver>(),
            Mock.Of<IAgentFactory>(),
            Mock.Of<IRepository<Agent, Guid>>(),
            Mock.Of<IRunStore>(),
            Mock.Of<ITraceStore>(),
            workflowService,
            new AgentExecutionContextAccessor(),
            new ServiceCollection().BuildServiceProvider(),
            Mock.Of<ILogger<AgentRuntime>>());
    }

    private static async IAsyncEnumerable<WorkflowExecutionResultDto> StreamWorkflowResults()
    {
        yield return new WorkflowExecutionResultDto
        {
            ExecutionId = "wf-001",
            Output = "step output",
            Status = "Step 'step-a'"
        };

        await Task.Yield();

        yield return new WorkflowExecutionResultDto
        {
            ExecutionId = "wf-001",
            Output = "done",
            Status = "Completed"
        };
    }
}
