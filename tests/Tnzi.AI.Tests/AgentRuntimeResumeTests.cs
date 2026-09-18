using Tnzi.Exceptions;

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

        var workflowDelegator = new WorkflowDelegator(workflowService.Object, runStore.Object, runTracker);

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

        var workflowDelegator = new WorkflowDelegator(workflowService.Object, runStore.Object, runTracker);

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

        var workflowDelegator = new WorkflowDelegator(workflowService.Object, runStore.Object, runTracker);

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
    /// 续跑必须按建行时记下的请求快照重建请求：此前只带 AgentId / ThreadId / InputSummary，
    /// 模板 spawn（AgentId 为 null）的运行续跑成一个没有任何工具的默认 agent，DB agent 的续跑丢掉
    /// 子 Agent 标记（ToolResolver 的子 Agent 裁剪整体失效、比原运行更宽），用户消息还被 InputSummary 截到 500 字。
    /// </summary>
    [Fact]
    public async Task ResumeAsync_NonWorkflowRun_RebuildsTheRequestFromTheSnapshot()
    {
        var runId = Guid.NewGuid();
        var parentRunId = Guid.NewGuid();
        var rootRunId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var longMessage = new string('x', 601);
        var original = new AgentRunRequest
        {
            OperationType = AIOperationType.AgentRun,
            Provider = "openai",
            Model = "gpt-5",
            UserMessage = longMessage,
            ToolGroups = ["sandbox"],
            ToolNames = ["bash"],
            TrustedToolSelection = true,
            SubAgentName = "bash",
            IsBackground = true,
            PlanMode = true,
            ReasoningEffort = ReasoningEffort.High,
            Metadata = new Dictionary<string, object> { ["node_name"] = "step-1" },
            Attachments = [new FileAttachment("spec.md", 12, "text/markdown")]
        };
        var run = new AgentRun
        {
            Id = runId,
            AgentId = null,
            ThreadId = threadId,
            ParentRunId = parentRunId,
            RootRunId = rootRunId,
            CreatorId = creatorId,
            Status = AgentRunStatus.Failed,
            InputSummary = StringTruncator.Truncate(longMessage, 500),
            RequestSnapshot = AgentRunRequestSnapshot.From(original).Serialize(),
            Nodes = []
        };

        var (runtime, resolver, probe, accessor) = BuildRuntime(run, creatorId, threadId);

        var result = await runtime.ResumeAsync(runId);

        result.Status.ShouldBe(AgentRunStatus.Completed);
        resolver.Verify(x => x.ResolveAgentAsync(null, "openai", "gpt-5",
            It.Is<List<string>>(g => g.SequenceEqual(new[] { "sandbox" })), It.IsAny<CancellationToken>(),
            It.Is<List<string>>(n => n.SequenceEqual(new[] { "bash" }))), Times.Once);

        var rebuilt = probe.CapturedRequest.ShouldNotBeNull();
        rebuilt.UserMessage.ShouldBe(longMessage);
        rebuilt.ToolGroups.ShouldBe(["sandbox"]);
        rebuilt.ToolNames.ShouldBe(["bash"]);
        rebuilt.TrustedToolSelection.ShouldBeTrue();
        rebuilt.SubAgentName.ShouldBe("bash");
        rebuilt.IsBackground.ShouldBeTrue();
        rebuilt.PlanMode.ShouldBeTrue();
        rebuilt.ReasoningEffort.ShouldBe(ReasoningEffort.High);
        rebuilt.ParentRunId.ShouldBe(parentRunId);
        rebuilt.RootRunId.ShouldBe(rootRunId);
        rebuilt.UserId.ShouldBe(creatorId);
        rebuilt.ThreadId.ShouldBe(threadId);
        rebuilt.EnableRunTracking.ShouldBeFalse();
        rebuilt.Metadata.ShouldNotBeNull();
        rebuilt.Metadata.ShouldContainKey("node_name");
        rebuilt.Attachments.ShouldNotBeNull();
        rebuilt.Attachments.Single().FileName.ShouldBe("spec.md");
        // 子 Agent 标记在执行期成立：ToolResolver 的裁剪与 IsSubAgentOnly 规则据此匹配
        probe.WasMarkedSubAgent.ShouldBeTrue();
        accessor.Properties.ShouldNotContainKey(ContextPropertyKeys.IsSubAgent);
    }

    [Fact]
    public async Task ResumeAsync_ExplicitUserMessage_OverridesTheSnapshotMessage()
    {
        var runId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var run = new AgentRun
        {
            Id = runId,
            AgentId = agentId,
            Status = AgentRunStatus.RequiresClarification,
            RequestSnapshot = AgentRunRequestSnapshot.From(new AgentRunRequest { AgentId = agentId, UserMessage = "original task" }).Serialize(),
            Nodes = []
        };
        var (runtime, _, probe, _) = BuildRuntime(run, null, null);

        await runtime.ResumeAsync(runId, new ResumeRunInput { UserMessage = "here is the clarification" });

        probe.CapturedRequest.ShouldNotBeNull().UserMessage.ShouldBe("here is the clarification");
    }

    /// <summary>
    /// 迁移前的旧行：没有快照、AgentId 也为 null（模板 spawn）—— 拒绝续跑，不能静默降级成无工具的默认 agent。
    /// </summary>
    [Fact]
    public async Task ResumeAsync_AdHocRunWithoutSnapshot_Returns400InsteadOfDegrading()
    {
        var runId = Guid.NewGuid();
        var run = new AgentRun { Id = runId, AgentId = null, Status = AgentRunStatus.Failed, RequestSnapshot = null, Nodes = [] };
        var (runtime, resolver, _, _) = BuildRuntime(run, null, null);

        var ex = await Should.ThrowAsync<BusinessException>(() => runtime.ResumeAsync(runId));

        ex.Code.ShouldBe(ErrorCodes.RunInvalidState);
        ex.HttpStatusCode.ShouldBe(400);
        resolver.Verify(x => x.ResolveAgentAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<List<string>?>(), It.IsAny<CancellationToken>(), It.IsAny<List<string>?>()), Times.Never);
        run.Status.ShouldBe(AgentRunStatus.Failed, "the row must stay as it was, not be flipped to Running");
    }

    /// <summary>
    /// 迁移前的旧行、DB agent：没有快照也能续跑（工具组由 grants 重建），父子链与归属人从行上回填。
    /// </summary>
    [Fact]
    public async Task ResumeAsync_DbAgentRunWithoutSnapshot_BackfillsLineageFromTheRow()
    {
        var runId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var parentRunId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var run = new AgentRun
        {
            Id = runId, AgentId = agentId, ParentRunId = parentRunId, RootRunId = parentRunId, CreatorId = creatorId,
            Status = AgentRunStatus.Failed, InputSummary = "legacy", RequestSnapshot = null, Nodes = []
        };
        var (runtime, _, probe, _) = BuildRuntime(run, creatorId, null);

        await runtime.ResumeAsync(runId);

        var rebuilt = probe.CapturedRequest.ShouldNotBeNull();
        rebuilt.AgentId.ShouldBe(agentId);
        rebuilt.ParentRunId.ShouldBe(parentRunId);
        rebuilt.RootRunId.ShouldBe(parentRunId);
        rebuilt.UserId.ShouldBe(creatorId);
        rebuilt.UserMessage.ShouldBe("legacy");
        probe.WasMarkedSubAgent.ShouldBeTrue();
    }

    private static (AgentRuntime Runtime, Mock<IAgentResolver> Resolver, RequestProbeMiddleware Probe, AgentExecutionContextAccessor Accessor)
        BuildRuntime(AgentRun run, Guid? ownerId, Guid? ownedThreadId)
    {
        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.GetWithNodesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        runStore.Setup(x => x.UpdateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var traceStore = new Mock<ITraceStore>();
        traceStore.Setup(x => x.AddAsync(It.IsAny<AgentRunTrace>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRunTrace trace, CancellationToken _) => trace);

        var runTracker = new RunTracker(runStore.Object, traceStore.Object, Mock.Of<ILogger<RunTracker>>());

        var resolver = new Mock<IAgentResolver>();
        resolver.Setup(x => x.ResolveAgentAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<List<string>?>(), It.IsAny<CancellationToken>(), It.IsAny<List<string>?>()))
            .ReturnsAsync(AgentResolution.Success(Mock.Of<IAgentExecutor>(), "test", "gpt-5", run.AgentId));

        var accessor = new AgentExecutionContextAccessor();
        var probe = new RequestProbeMiddleware(accessor);
        var sp = new ServiceCollection().AddSingleton<IAiMiddleware>(probe).BuildServiceProvider();

        var threadService = new Mock<IAgentThreadService>();
        threadService.Setup(x => x.IsOwnerAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync((Guid t, Guid u) => t == ownedThreadId && u == ownerId);

        var runtime = new AgentRuntime(
            resolver.Object,
            Mock.Of<IAgentFactory>(),
            Mock.Of<IRepository<Agent, Guid>>(),
            runTracker,
            Mock.Of<IWorkflowDelegator>(),
            accessor,
            sp,
            new StaticOptionsMonitor<AIOptions>(new AIOptions()),
            Mock.Of<IEventPublisher>(),
            Mock.Of<ILogger<AgentRuntime>>(),
            threadService.Object);

        return (runtime, resolver, probe, accessor);
    }

    /// <summary>抓住管线看到的请求与执行期的子 Agent 标记。</summary>
    private sealed class RequestProbeMiddleware : IAiMiddleware
    {
        private readonly AgentExecutionContextAccessor _accessor;

        public RequestProbeMiddleware(AgentExecutionContextAccessor accessor)
        {
            _accessor = accessor;
        }

        public AgentRunRequest? CapturedRequest { get; private set; }
        public bool WasMarkedSubAgent { get; private set; }

        public int Order => 0;

        public Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
        {
            CapturedRequest = context.Request;
            WasMarkedSubAgent = _accessor.Properties.TryGetValue(ContextPropertyKeys.IsSubAgent, out var flag) && flag is true;
            return Task.FromResult(new AgentRunResult { Response = "resumed", FinishReason = FinishReasons.Stop });
        }

        public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield break;
        }
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
