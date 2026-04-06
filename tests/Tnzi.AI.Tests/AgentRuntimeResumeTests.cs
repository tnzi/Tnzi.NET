
using System.Runtime.CompilerServices;

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
            Mock.Of<IServiceScopeFactory>(),
            Mock.Of<IOptionsMonitor<AIOptions>>(),
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

    [Fact]
    public async Task ResumeAsync_WorkflowRun_WhenResumeReturnsFailed_MapsFailedStatus()
    {
        var runId = Guid.NewGuid();
        var run = new AgentRun
        {
            Id = runId,
            ThreadId = Guid.NewGuid(),
            WorkflowDefinitionId = Guid.NewGuid(),
            WorkflowExecutionId = "wf-exec-failed",
            Status = AgentRunStatus.AwaitingApproval,
            Nodes = []
        };

        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.GetWithNodesAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);
        runStore.Setup(x => x.UpdateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var traceStore = new Mock<ITraceStore>();
        traceStore.Setup(x => x.AddAsync(It.IsAny<AgentRunTrace>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRunTrace trace, CancellationToken _) => trace);

        var workflowService = new Mock<IWorkflowService>();
        workflowService.Setup(x => x.ResumeAsync("wf-exec-failed", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkflowExecutionResultDto>.Success(new WorkflowExecutionResultDto
            {
                ExecutionId = "wf-exec-failed",
                Output = "workflow failed",
                Status = "Failed"
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
            Mock.Of<IServiceScopeFactory>(),
            Mock.Of<IOptionsMonitor<AIOptions>>(),
            Mock.Of<ILogger<AgentRuntime>>());

        var result = await runtime.ResumeAsync(runId);

        result.Status.ShouldBe(AgentRunStatus.Failed);
        result.FinishReason.ShouldBe(FinishReasons.Failed);
        run.Status.ShouldBe(AgentRunStatus.Failed);
        run.Error.ShouldBe("workflow failed");
    }

    [Fact]
    public async Task ResumeAsync_RootRun_WhenRuntimeRequiresClarification_PreservesReturnedStatus()
    {
        var runId = Guid.NewGuid();
        var run = new AgentRun
        {
            Id = runId,
            Status = AgentRunStatus.RequiresClarification,
            ThreadId = Guid.NewGuid(),
            InputSummary = "previous input"
        };

        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.GetWithNodesAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);
        runStore.Setup(x => x.UpdateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var traceStore = new Mock<ITraceStore>();
        traceStore.Setup(x => x.AddAsync(It.IsAny<AgentRunTrace>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRunTrace trace, CancellationToken _) => trace);

        var resolver = new Mock<IAgentResolver>();
        resolver.Setup(x => x.ResolveAgentAsync(null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentResolution.SuccessWithoutExecutor("test-provider", "gpt-5", null, null, AgentExecutionMode.ExternalCli));

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IAiMiddleware>(new RequiresClarificationMiddleware())
            .BuildServiceProvider();

        var runtime = new AgentRuntime(
            resolver.Object,
            Mock.Of<IAgentFactory>(),
            Mock.Of<IRepository<Agent, Guid>>(),
            runStore.Object,
            traceStore.Object,
            Mock.Of<IWorkflowService>(),
            new AgentExecutionContextAccessor(),
            serviceProvider,
            Mock.Of<IServiceScopeFactory>(),
            Mock.Of<IOptionsMonitor<AIOptions>>(),
            Mock.Of<ILogger<AgentRuntime>>());

        var result = await runtime.ResumeAsync(runId, new ResumeRunInput { UserMessage = "retry" });

        result.Status.ShouldBe(AgentRunStatus.RequiresClarification);
        run.Status.ShouldBe(AgentRunStatus.RequiresClarification);
    }

    [Fact]
    public async Task ResumeAsync_WorkflowRun_WhenRequiresClarification_UsesResumeWithInput()
    {
        var runId = Guid.NewGuid();
        var run = new AgentRun
        {
            Id = runId,
            ThreadId = Guid.NewGuid(),
            WorkflowDefinitionId = Guid.NewGuid(),
            WorkflowExecutionId = "wf-exec-input",
            Status = AgentRunStatus.RequiresClarification,
            Nodes = []
        };

        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.GetWithNodesAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);
        runStore.Setup(x => x.UpdateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var traceStore = new Mock<ITraceStore>();
        traceStore.Setup(x => x.AddAsync(It.IsAny<AgentRunTrace>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRunTrace trace, CancellationToken _) => trace);

        var workflowService = new Mock<IWorkflowService>();
        workflowService.Setup(x => x.GetPendingInterruptAsync("wf-exec-input", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkflowInterruptDto>.Success(new WorkflowInterruptDto
            {
                ExecutionId = "wf-exec-input",
                StepId = "collect-input",
                Type = "HumanInput",
                Reason = "Need more detail"
            }));
        workflowService.Setup(x => x.ResumeWithInputAsync(
                "wf-exec-input",
                "collect-input",
                It.Is<Dictionary<string, object>>(d => (string)d["detail"] == "more context"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkflowExecutionResultDto>.Success(new WorkflowExecutionResultDto
            {
                ExecutionId = "wf-exec-input",
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
            Mock.Of<IServiceScopeFactory>(),
            Mock.Of<IOptionsMonitor<AIOptions>>(),
            Mock.Of<ILogger<AgentRuntime>>());

        var result = await runtime.ResumeAsync(runId, new ResumeRunInput
        {
            WorkflowInput = new Dictionary<string, object>
            {
                ["detail"] = "more context"
            }
        });

        result.Status.ShouldBe(AgentRunStatus.Completed);
        result.FinishReason.ShouldBe(FinishReasons.Completed);
        run.Status.ShouldBe(AgentRunStatus.Completed);
        workflowService.Verify(x => x.ResumeWithInputAsync(
            "wf-exec-input",
            "collect-input",
            It.IsAny<Dictionary<string, object>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        workflowService.Verify(x => x.ResumeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class RequiresClarificationMiddleware : IAiMiddleware
    {
        public int Order => 0;

        public Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentRunResult
            {
                Response = "Need more detail",
                FinishReason = FinishReasons.RequiresClarification
            });

        public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield break;
        }
    }
}
