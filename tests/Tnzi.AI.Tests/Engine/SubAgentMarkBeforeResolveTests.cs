namespace Tnzi.AI.Tests.Engine;

/// <summary>
/// Handoff / Router 的目标 Agent 必须在<b>解析之前</b>就被标记为子 Agent。
/// </summary>
/// <remarks>
/// 目标 Agent 的工具列表在解析时一次定型：<c>ExecutionStrategyAgentLoader.ResolveAgentAsync</c>
/// → <c>AgentFactory</c> → <c>ToolResolver.ApplySubAgentToolLists</c>，后者此刻读属性包里的
/// <c>IsSubAgent</c> 决定是否裁掉 <c>AI:SubAgent:SubAgentDisallowedTools</c>。先解析后标记，
/// 运维为「模型选出来的 Agent」禁掉的工具（例如 bash）就原样留在模型自选的目标手里。
/// 这里在工厂被调用的那一刻记录标记，那正是名单被应用的时刻。
/// </remarks>
public class SubAgentMarkBeforeResolveTests
{
    [Fact]
    public async Task Router_ExecuteAsync_MarksTargetAsSubAgentBeforeResolvingIt()
    {
        var harness = new Harness();
        var strategy = new RouterExecutionStrategy(new RouterConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["specialist"] = harness.TargetAgentId },
            AllowDirectResponse = false
        });

        await strategy.ExecuteAsync(
            Harness.CreateNonStreamingRoutingAgent("Router", "route_to_agent", "specialist"),
            [new ChatMessage(ChatRole.User, "need help")],
            harness.Context,
            CancellationToken.None);

        harness.ResolvedCount.ShouldBe(1);
        harness.IsSubAgentAtResolution.ShouldBe(true);
        harness.Accessor.Properties.GetValueOrDefault(ContextPropertyKeys.SubAgentName).ShouldBe(Harness.TargetName);
    }

    [Fact]
    public async Task Router_ExecuteStreamingAsync_MarksTargetAsSubAgentBeforeResolvingIt()
    {
        var harness = new Harness();
        var strategy = new RouterExecutionStrategy(new RouterConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["specialist"] = harness.TargetAgentId },
            AllowDirectResponse = false
        });

        await foreach (var _ in strategy.ExecuteStreamingAsync(
            Harness.CreateStreamingRoutingAgent("Router", "route_to_agent", "specialist"),
            [new ChatMessage(ChatRole.User, "need help")],
            harness.Context,
            CancellationToken.None))
        {
        }

        harness.ResolvedCount.ShouldBe(1);
        harness.IsSubAgentAtResolution.ShouldBe(true);
    }

    [Fact]
    public async Task Handoff_ExecuteAsync_MarksTargetAsSubAgentBeforeResolvingIt()
    {
        var harness = new Harness();
        var strategy = new HandoffExecutionStrategy(new HandoffConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["specialist"] = harness.TargetAgentId },
            MaxHandoffs = 3
        });

        await strategy.ExecuteAsync(
            Harness.CreateNonStreamingRoutingAgent("Triage", "handoff_to_agent", "specialist"),
            [new ChatMessage(ChatRole.User, "need help")],
            harness.Context,
            CancellationToken.None);

        harness.ResolvedCount.ShouldBe(1);
        harness.IsSubAgentAtResolution.ShouldBe(true);
        harness.Accessor.Properties.GetValueOrDefault(ContextPropertyKeys.SubAgentName).ShouldBe(Harness.TargetName);
    }

    [Fact]
    public async Task Handoff_ExecuteStreamingAsync_MarksTargetAsSubAgentBeforeResolvingIt()
    {
        var harness = new Harness();
        var strategy = new HandoffExecutionStrategy(new HandoffConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["specialist"] = harness.TargetAgentId },
            MaxHandoffs = 3
        });

        await foreach (var _ in strategy.ExecuteStreamingAsync(
            Harness.CreateStreamingRoutingAgent("Triage", "handoff_to_agent", "specialist"),
            [new ChatMessage(ChatRole.User, "need help")],
            harness.Context,
            CancellationToken.None))
        {
        }

        harness.ResolvedCount.ShouldBe(1);
        harness.IsSubAgentAtResolution.ShouldBe(true);
    }

    private sealed class Harness
    {
        public const string TargetName = "Specialist";

        public Harness()
        {
            // 先在本测试的执行流里把属性包实体化：Properties 的 getter 是 AsyncLocal 的惰性赋值，
            // 若第一次读发生在被测方法的异步流里，那次赋值传不回调用方，断言会读到空
            _ = Accessor.Properties;

            var repository = new Mock<IRepository<Agent, Guid>>();
            repository.Setup(x => x.GetAsync(TargetAgentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Agent { Id = TargetAgentId, Name = TargetName, Provider = "test", IsEnabled = true });

            var agentFactory = new Mock<IAgentFactory>();
            agentFactory.Setup(x => x.CreateAgentAsync(
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), TargetName,
                    It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                    It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(),
                    TargetAgentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    // 与 ToolResolver.ApplySubAgentToolLists 读的是同一个键、同一时刻
                    ResolvedCount++;
                    IsSubAgentAtResolution = Accessor.Properties.TryGetValue(ContextPropertyKeys.IsSubAgent, out var flag) && flag is true;
                    return CreateTextAgent(TargetName, "target answer");
                });

            Context = new ExecutionStrategyContext
            {
                AgentFactory = agentFactory.Object,
                AgentRepository = repository.Object,
                ServiceProvider = TestHelpers.ServiceProviderWithGrants(),
                ExecutionContextAccessor = Accessor,
                Logger = Mock.Of<ILogger>()
            };
        }

        public Guid TargetAgentId { get; } = Guid.NewGuid();
        public AgentExecutionContextAccessor Accessor { get; } = new();
        public ExecutionStrategyContext Context { get; }
        public int ResolvedCount { get; private set; }
        public bool? IsSubAgentAtResolution { get; private set; }

        private static AgentExecutor CreateTextAgent(string name, string responseText)
        {
            var client = new Mock<IChatClient>();
            client.Setup(x => x.GetResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
            client.Setup(x => x.GetStreamingResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
                .Returns(() => Stream(new ChatResponseUpdate(ChatRole.Assistant, responseText) { FinishReason = ChatFinishReason.Stop }));

            return new AgentExecutor(client.Object, new AgentExecutorOptions { Name = name });
        }

        /// <summary>第一轮回一个转接工具调用，之后回普通文本。</summary>
        public static AgentExecutor CreateNonStreamingRoutingAgent(string name, string toolName, string target)
        {
            var toolCall = CreateToolCall(toolName, target);
            var callCount = 0;
            var client = new Mock<IChatClient>();
            client.Setup(x => x.GetResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => ++callCount == 1
                    ? new ChatResponse([new ChatMessage(ChatRole.Assistant, [toolCall])])
                    : new ChatResponse(new ChatMessage(ChatRole.Assistant, "delegating")));

            return new AgentExecutor(client.Object, new AgentExecutorOptions { Name = name });
        }

        /// <summary>流式版：第一轮流出转接工具调用，之后流出普通文本。</summary>
        public static AgentExecutor CreateStreamingRoutingAgent(string name, string toolName, string target)
        {
            var toolCall = CreateToolCall(toolName, target);
            var callCount = 0;
            var client = new Mock<IChatClient>();
            client.Setup(x => x.GetStreamingResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
                .Returns(() => ++callCount == 1
                    ? Stream(new ChatResponseUpdate(ChatRole.Assistant, [toolCall]))
                    : Stream(new ChatResponseUpdate(ChatRole.Assistant, "delegating") { FinishReason = ChatFinishReason.Stop }));

            return new AgentExecutor(client.Object, new AgentExecutorOptions { Name = name });
        }

        private static FunctionCallContent CreateToolCall(string toolName, string target)
            => new("call_1", toolName, new Dictionary<string, object?>
            {
                ["targetAgentName"] = target,
                ["reason"] = "Better suited"
            });

        private static async IAsyncEnumerable<ChatResponseUpdate> Stream(params ChatResponseUpdate[] updates)
        {
            foreach (var update in updates)
            {
                await Task.Yield();
                yield return update;
            }
        }
    }
}
