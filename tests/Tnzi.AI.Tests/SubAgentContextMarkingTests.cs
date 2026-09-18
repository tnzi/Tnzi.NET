namespace Tnzi.AI.Tests;

/// <summary>
/// 子 Agent 上下文标记（<c>ContextPropertyKeys.IsSubAgent</c>）在各条起子 Agent 路径上的覆盖测试。
/// </summary>
/// <remarks>
/// <c>ToolPermissionRule.IsSubAgentOnly</c> 只在这个标记为 true 时匹配。标记原先只有
/// <c>AgentAsToolsExecutionStrategy</c> 一处写入，于是 <c>spawn_agent</c>（最常用的起子 Agent 手段）、
/// Handoff 与 Router 转接过去的 Agent 全都不带标记 —— 针对子 Agent 收紧工具的规则在这三条路径上
/// 一条也不生效，而管理端把它显示为一条已启用的规则。
/// </remarks>
public class SubAgentContextMarkingTests
{
    // ---------------------------------------------------------------------
    // AgentRuntime：后台子 Agent（spawn_agent）
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Runtime_RunAsync_WithParentRunId_MarksTheRunAsSubAgent()
    {
        // spawn_agent 起的后台运行在新作用域、新执行流里跑，父级属性包不会流过去；
        // 而 RunAsync 一开始就 ClearProperties，所以调用前设标记也会被抹掉。
        // 判据只能来自请求本身：带 ParentRunId 的运行按定义就是子运行。
        var harness = new RuntimeHarness();

        await harness.Runtime.RunAsync(new AgentRunRequest
        {
            WorkflowId = Guid.NewGuid(),
            UserMessage = "do the thing",
            ParentRunId = Guid.NewGuid(),
            SubAgentName = "researcher"
        });

        harness.Observed.ShouldNotBeNull();
        harness.Observed!.GetValueOrDefault(ContextPropertyKeys.IsSubAgent).ShouldBe(true);
        harness.Observed.GetValueOrDefault(ContextPropertyKeys.SubAgentName).ShouldBe("researcher");
    }

    [Fact]
    public async Task Runtime_RunAsync_WithoutParentRunId_LeavesTheRunUnmarked()
    {
        var harness = new RuntimeHarness();

        await harness.Runtime.RunAsync(new AgentRunRequest
        {
            WorkflowId = Guid.NewGuid(),
            UserMessage = "do the thing"
        });

        harness.Observed.ShouldNotBeNull();
        harness.Observed!.ContainsKey(ContextPropertyKeys.IsSubAgent).ShouldBeFalse();
    }

    [Fact]
    public async Task Runtime_RunAsync_BackgroundRootRun_MarksTheRunAsSubAgent()
    {
        // SpawnAsync 起的每一个运行都是子 Agent 运行，包括没有父运行的根 spawn：
        // 只认 ParentRunId 会让未开追踪的聊天里 spawn 出来的运行逃过 SubAgentDisallowedTools 与 IsSubAgentOnly 规则
        var harness = new RuntimeHarness();

        await harness.Runtime.RunAsync(new AgentRunRequest
        {
            WorkflowId = Guid.NewGuid(),
            UserMessage = "do the thing",
            IsBackground = true,
            SubAgentName = "researcher"
        });

        harness.Observed.ShouldNotBeNull();
        harness.Observed!.GetValueOrDefault(ContextPropertyKeys.IsSubAgent).ShouldBe(true);
        harness.Observed.GetValueOrDefault(ContextPropertyKeys.SubAgentName).ShouldBe("researcher");
    }

    [Fact]
    public async Task Runtime_RunStreamingAsync_WithParentRunId_MarksTheRunAsSubAgent()
    {
        var harness = new RuntimeHarness(streaming: true);

        await foreach (var _ in harness.Runtime.RunStreamingAsync(new AgentRunRequest
        {
            WorkflowId = Guid.NewGuid(),
            UserMessage = "do the thing",
            ParentRunId = Guid.NewGuid(),
            SubAgentName = "researcher"
        }))
        {
        }

        harness.Observed.ShouldNotBeNull();
        harness.Observed!.GetValueOrDefault(ContextPropertyKeys.IsSubAgent).ShouldBe(true);
    }

    [Fact]
    public async Task SpawnAsync_PutsTheSubAgentTypeOnTheRuntimeRequest()
    {
        // 后台运行拿不到调用方的上下文，名字只能随请求一起传过去
        var agentId = Guid.NewGuid();
        var captured = new TaskCompletionSource<AgentRunRequest>(TaskCreationOptions.RunContinuationsAsynchronously);

        var resolver = new Mock<IAgentResolver>();
        resolver.Setup(x => x.ResolveAgentAsync(
                agentId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentResolution.Success(Mock.Of<IAgentExecutor>(), "openai", "gpt-4o", agentId, null, AgentExecutionMode.Single));

        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.CreateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRun run, CancellationToken _) => { run.Id = Guid.NewGuid(); return run; });

        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(x => x.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRunRequest request, CancellationToken _) =>
            {
                captured.TrySetResult(request);
                return new AgentRunResult { Response = "ok" };
            });

        var registry = new Mock<ISubAgentRegistry>();
        registry.Setup(x => x.GetForTenant("researcher", It.IsAny<string>())).Returns(
            new SubAgentTypeDefinition("researcher", "Researches things", [], [], 10));

        var services = new ServiceCollection();
        services.AddScoped(_ => runtime.Object);
        var provider = services.BuildServiceProvider();

        var service = new SubAgentExecutionService(
            resolver.Object,
            runStore.Object,
            registry.Object,
            new AgentExecutionContextAccessor(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SubAgentExecutionService>.Instance,
            provider,
            new StaticOptionsMonitor<SubAgentOptions>(new SubAgentOptions()));

        var result = await service.SpawnAsync(new SpawnAgentRunInput
        {
            AgentId = agentId,
            Message = "go research",
            SubAgentType = "researcher"
        });

        result.Succeeded.ShouldBeTrue();
        var request = await captured.Task.WaitAsync(TimeSpan.FromSeconds(10));
        request.SubAgentName.ShouldBe("researcher");
        request.ParentRunId.ShouldNotBe(request.ExistingRunId);
    }

    // ---------------------------------------------------------------------
    // Router / Handoff：转接过去的 Agent
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Router_WhenDelegatingToTarget_MarksSubAgentContext()
    {
        var targetAgentId = Guid.NewGuid();
        var accessor = new AgentExecutionContextAccessor();
        // 先在本测试的执行流里把属性包实体化：Properties 的 getter 是 AsyncLocal 的惰性赋值，
        // 若第一次读发生在被测方法的异步流里，那次赋值传不回调用方，断言会读到空
        _ = accessor.Properties;
        var strategy = new RouterExecutionStrategy(new RouterConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["specialist"] = targetAgentId },
            AllowDirectResponse = false
        });

        await strategy.ExecuteAsync(
            CreateRoutingAgent("Router", "route_to_agent", "specialist"),
            [new ChatMessage(ChatRole.User, "need help")],
            CreateContext(targetAgentId, "Specialist", accessor),
            CancellationToken.None);

        accessor.Properties.GetValueOrDefault(ContextPropertyKeys.IsSubAgent).ShouldBe(true);
        accessor.Properties.GetValueOrDefault(ContextPropertyKeys.SubAgentName).ShouldBe("Specialist");
    }

    [Fact]
    public async Task Handoff_WhenTransferringToTarget_MarksSubAgentContext()
    {
        var targetAgentId = Guid.NewGuid();
        var accessor = new AgentExecutionContextAccessor();
        // 先在本测试的执行流里把属性包实体化：Properties 的 getter 是 AsyncLocal 的惰性赋值，
        // 若第一次读发生在被测方法的异步流里，那次赋值传不回调用方，断言会读到空
        _ = accessor.Properties;
        var strategy = new HandoffExecutionStrategy(new HandoffConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["specialist"] = targetAgentId },
            MaxHandoffs = 3
        });

        await strategy.ExecuteAsync(
            CreateRoutingAgent("Triage", "handoff_to_agent", "specialist"),
            [new ChatMessage(ChatRole.User, "need help")],
            CreateContext(targetAgentId, "Specialist", accessor),
            CancellationToken.None);

        accessor.Properties.GetValueOrDefault(ContextPropertyKeys.IsSubAgent).ShouldBe(true);
        accessor.Properties.GetValueOrDefault(ContextPropertyKeys.SubAgentName).ShouldBe("Specialist");
    }

    [Fact]
    public async Task Router_WhenAnsweringDirectly_LeavesContextUnmarked()
    {
        // 没有转接就没有子 Agent —— 直接作答的 router 本人还是用户面对的那个 Agent
        var accessor = new AgentExecutionContextAccessor();
        // 先在本测试的执行流里把属性包实体化：Properties 的 getter 是 AsyncLocal 的惰性赋值，
        // 若第一次读发生在被测方法的异步流里，那次赋值传不回调用方，断言会读到空
        _ = accessor.Properties;
        var strategy = new RouterExecutionStrategy(new RouterConfiguration());

        await strategy.ExecuteAsync(
            CreateTextAgent("Router", "direct answer"),
            [new ChatMessage(ChatRole.User, "simple")],
            new ExecutionStrategyContext
            {
                AgentFactory = Mock.Of<IAgentFactory>(),
                AgentRepository = Mock.Of<IRepository<Agent, Guid>>(),
                ServiceProvider = TestHelpers.ServiceProviderWithGrants(),
                ExecutionContextAccessor = accessor,
                Logger = Mock.Of<ILogger>()
            },
            CancellationToken.None);

        accessor.Properties.ContainsKey(ContextPropertyKeys.IsSubAgent).ShouldBeFalse();
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static ExecutionStrategyContext CreateContext(
        Guid targetAgentId,
        string targetName,
        IAgentExecutionContextAccessor accessor)
    {
        var repository = new Mock<IRepository<Agent, Guid>>();
        repository.Setup(x => x.GetAsync(targetAgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent { Id = targetAgentId, Name = targetName, Provider = "test", IsEnabled = true });

        var agentFactory = new Mock<IAgentFactory>();
        agentFactory.Setup(x => x.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), targetName,
                It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(),
                targetAgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTextAgent(targetName, "target answer"));

        return new ExecutionStrategyContext
        {
            AgentFactory = agentFactory.Object,
            AgentRepository = repository.Object,
            ServiceProvider = TestHelpers.ServiceProviderWithGrants(),
            ExecutionContextAccessor = accessor,
            Logger = Mock.Of<ILogger>()
        };
    }

    private static AgentExecutor CreateTextAgent(string name, string responseText)
    {
        var client = new Mock<IChatClient>();
        client.Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));

        return new AgentExecutor(client.Object, new AgentExecutorOptions { Name = name });
    }

    /// <summary>第一轮回一个转接工具调用，之后回普通文本。</summary>
    private static AgentExecutor CreateRoutingAgent(string name, string toolName, string target)
    {
        var toolCall = new FunctionCallContent("call_1", toolName, new Dictionary<string, object?>
        {
            ["targetAgentName"] = target,
            ["reason"] = "Better suited"
        });

        var callCount = 0;
        var client = new Mock<IChatClient>();
        client.Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? new ChatResponse([new ChatMessage(ChatRole.Assistant, [toolCall])])
                    : new ChatResponse(new ChatMessage(ChatRole.Assistant, "delegating"));
            });

        return new AgentExecutor(client.Object, new AgentExecutorOptions { Name = name });
    }

    /// <summary>
    /// 一个真实 AgentRuntime，并在<b>运行过程中</b>（经 workflow 委派回调）快照属性包。
    /// 运行结束后属性会被还原，事后再读到的不是运行时看到的那份。
    /// </summary>
    private sealed class RuntimeHarness
    {
        public RuntimeHarness(bool streaming = false)
        {
            var accessor = new AgentExecutionContextAccessor();
        // 先在本测试的执行流里把属性包实体化：Properties 的 getter 是 AsyncLocal 的惰性赋值，
        // 若第一次读发生在被测方法的异步流里，那次赋值传不回调用方，断言会读到空
        _ = accessor.Properties;
            var workflowService = new Mock<IWorkflowService>();

            if (streaming)
            {
                workflowService
                    .Setup(x => x.RunStreamingAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                    .Returns(() =>
                    {
                        Observed = new Dictionary<string, object>(accessor.Properties);
                        return EmptyStream();
                    });
            }
            else
            {
                workflowService
                    .Setup(x => x.RunAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(() =>
                    {
                        Observed = new Dictionary<string, object>(accessor.Properties);
                        return Result<WorkflowExecutionResultDto>.Success(new WorkflowExecutionResultDto
                        {
                            ExecutionId = "wf-1",
                            RunId = Guid.NewGuid(),
                            Output = "done",
                            Status = "Completed"
                        });
                    });
            }

            var runStore = new Mock<IRunStore>();
            runStore.Setup(x => x.UpdateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            runStore.Setup(x => x.CreateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AgentRun run, CancellationToken _) => { run.Id = Guid.NewGuid(); return run; });

            var traceStore = new Mock<ITraceStore>();
            traceStore.Setup(x => x.AddAsync(It.IsAny<AgentRunTrace>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AgentRunTrace trace, CancellationToken _) => trace);

            var runTracker = new RunTracker(runStore.Object, traceStore.Object, Mock.Of<ILogger<RunTracker>>());

            Runtime = new AgentRuntime(
                Mock.Of<IAgentResolver>(),
                Mock.Of<IAgentFactory>(),
                Mock.Of<IRepository<Agent, Guid>>(),
                runTracker,
                new WorkflowDelegator(workflowService.Object, runStore.Object, runTracker),
                accessor,
                new ServiceCollection().AddSingleton(workflowService.Object).BuildServiceProvider(),
                Mock.Of<IOptionsMonitor<AIOptions>>(),
                new EventPublisher(null, Mock.Of<IServiceScopeFactory>(), Mock.Of<ILogger<EventPublisher>>()),
                Mock.Of<ILogger<AgentRuntime>>());
        }

        public AgentRuntime Runtime { get; }

        public Dictionary<string, object>? Observed { get; private set; }

        private static async IAsyncEnumerable<WorkflowExecutionResultDto> EmptyStream()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
