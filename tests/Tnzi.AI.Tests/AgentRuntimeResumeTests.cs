

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

        var runTracker = new RunTracker(runStore.Object, traceStore.Object, Mock.Of<ILogger<RunTracker>>());

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

        var workflowDelegator = new WorkflowDelegator(workflowService.Object, runStore.Object, runTracker, Mock.Of<ILogger<WorkflowDelegator>>());

        var runtime = new AgentRuntime(
            Mock.Of<IAgentResolver>(),
            Mock.Of<IAgentFactory>(),
            Mock.Of<IRepository<Agent, Guid>>(),
            runTracker,
            workflowDelegator,
            new AgentExecutionContextAccessor(),
            new ServiceCollection().BuildServiceProvider(),
            Mock.Of<IOptionsMonitor<AIOptions>>(),
            Mock.Of<IEventPublisher>(),
            Mock.Of<ILogger<AgentRuntime>>());

        var result = await runtime.ResumeAsync(runId, new ResumeRunInput
        {
            ApprovalDecision = "approve",
            ApprovalComment = "approved"
        });

        result.RunId.ShouldBe(runId);
        result.FinishReason.ShouldBe(FinishReasons.Completed);
        result.Status.ShouldBe(AgentRunStatus.Completed);
    }

    [Fact]
    public async Task ResumeAsync_WorkflowRun_RejectsAwaitingSteps_AndReturnsRejected()
    {
        var runId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var run = new AgentRun
        {
            Id = runId,
            ThreadId = threadId,
            WorkflowDefinitionId = workflowId,
            WorkflowExecutionId = "wf-exec-002",
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

        var runTracker = new RunTracker(runStore.Object, traceStore.Object, Mock.Of<ILogger<RunTracker>>());

        var workflowService = new Mock<IWorkflowService>();
        workflowService.Setup(x => x.RejectStepAsync("wf-exec-002", "approval-step", "not good enough", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var workflowDelegator = new WorkflowDelegator(workflowService.Object, runStore.Object, runTracker, Mock.Of<ILogger<WorkflowDelegator>>());

        var runtime = new AgentRuntime(
            Mock.Of<IAgentResolver>(),
            Mock.Of<IAgentFactory>(),
            Mock.Of<IRepository<Agent, Guid>>(),
            runTracker,
            workflowDelegator,
            new AgentExecutionContextAccessor(),
            new ServiceCollection().BuildServiceProvider(),
            Mock.Of<IOptionsMonitor<AIOptions>>(),
            Mock.Of<IEventPublisher>(),
            Mock.Of<ILogger<AgentRuntime>>());

        var result = await runtime.ResumeAsync(runId, new ResumeRunInput
        {
            ApprovalDecision = "reject",
            ApprovalComment = "not good enough"
        });

        result.FinishReason.ShouldBe(FinishReasons.Rejected);
        result.Status.ShouldBe(AgentRunStatus.Failed);
    }

    [Fact]
    public async Task ResumeAsync_WorkflowRun_RequiresClarification_ResumesWithInput()
    {
        var runId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var run = new AgentRun
        {
            Id = runId,
            ThreadId = threadId,
            WorkflowDefinitionId = workflowId,
            WorkflowExecutionId = "wf-exec-003",
            Status = AgentRunStatus.RequiresClarification,
            Nodes = []
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

        var runTracker = new RunTracker(runStore.Object, traceStore.Object, Mock.Of<ILogger<RunTracker>>());

        var workflowService = new Mock<IWorkflowService>();
        workflowService.Setup(x => x.ResumeWithInputAsync("wf-exec-003", "step-1",
                It.Is<Dictionary<string, object>>(d => d.Count == 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkflowExecutionResultDto>.Success(new WorkflowExecutionResultDto
            {
                ExecutionId = "wf-exec-003",
                Output = "resumed with input",
                Status = "Completed"
            }));

        var workflowDelegator = new WorkflowDelegator(workflowService.Object, runStore.Object, runTracker, Mock.Of<ILogger<WorkflowDelegator>>());

        var runtime = new AgentRuntime(
            Mock.Of<IAgentResolver>(),
            Mock.Of<IAgentFactory>(),
            Mock.Of<IRepository<Agent, Guid>>(),
            runTracker,
            workflowDelegator,
            new AgentExecutionContextAccessor(),
            new ServiceCollection().BuildServiceProvider(),
            Mock.Of<IOptionsMonitor<AIOptions>>(),
            Mock.Of<IEventPublisher>(),
            Mock.Of<ILogger<AgentRuntime>>());

        var result = await runtime.ResumeAsync(runId, new ResumeRunInput
        {
            WorkflowInput = new() { ["field"] = "value" },
            WorkflowStepId = "step-1"
        });

        result.Response.ShouldBe("resumed with input");
        result.Status.ShouldBe(AgentRunStatus.Completed);
    }

    [Fact]
    public async Task ResumeAsync_NonWorkflowRun_CallsRunAsyncAndUpdatesOriginalRun()
    {
        var runId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var run = new AgentRun
        {
            Id = runId,
            AgentId = agentId,
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

        var runTracker = new RunTracker(runStore.Object, traceStore.Object, Mock.Of<ILogger<RunTracker>>());

        var resolver = new Mock<IAgentResolver>();
        resolver.Setup(x => x.ResolveAgentAsync(agentId, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentResolution.Success(Mock.Of<IAgentExecutor>(), "test", "gpt-5", null));

        var sp = new ServiceCollection()
            .AddSingleton<IAiMiddleware>(new StaticResultMiddleware(new AgentRunResult
            {
                Response = "resumed output",
                FinishReason = FinishReasons.Stop
            }))
            .BuildServiceProvider();

        var runtime = new AgentRuntime(
            resolver.Object,
            Mock.Of<IAgentFactory>(),
            Mock.Of<IRepository<Agent, Guid>>(),
            runTracker,
            Mock.Of<IWorkflowDelegator>(),
            new AgentExecutionContextAccessor(),
            sp,
            Mock.Of<IOptionsMonitor<AIOptions>>(),
            Mock.Of<IEventPublisher>(),
            Mock.Of<ILogger<AgentRuntime>>());

        var result = await runtime.ResumeAsync(runId);

        result.RunId.ShouldBe(runId);
        result.Status.ShouldBe(AgentRunStatus.Completed);
        resolver.Verify(x => x.ResolveAgentAsync(agentId, null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// 短路中间件 - 直接返回固定结果，使测试无需真实执行策略/ChatClient。
    /// </summary>
    private sealed class StaticResultMiddleware : IAiMiddleware
    {
        private readonly AgentRunResult _result;

        public StaticResultMiddleware(AgentRunResult result)
        {
            _result = result;
        }

        public int Order => 0;

        public Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);

        public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield break;
        }
    }
}
