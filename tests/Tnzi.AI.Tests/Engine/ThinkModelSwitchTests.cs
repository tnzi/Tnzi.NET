namespace Tnzi.AI.Tests.Engine;

/// <summary>
/// <c>ReasoningEffort</c> 触发的 think 别名切换必须按「这次真正用的提供商与模型」判定。
/// </summary>
/// <remarks>
/// 已定义 Agent 的提供商与模型属于 Agent，解析前不知道。此前切换在解析前按请求的提供商（Agent 请求通常为空，
/// 落到默认提供商）读 <c>Models["think"]</c>，再作为模型覆盖交给解析器：Agent 在另一家提供商上时，
/// 默认提供商的模型名被发给 Agent 自己的提供商；Agent 已配了推理模型时也被别名盖掉。
/// </remarks>
public class ThinkModelSwitchTests
{
    private const string DefaultProvider = "OpenAI";
    private const string DefaultThinkModel = "o3-mini";

    [Fact]
    public async Task AgentOnAnotherProvider_KeepsItsOwnModel()
    {
        var harness = new Harness(agentProvider: "Anthropic", agentModel: "claude-sonnet-4");

        await harness.RunAsync(agentId: Guid.NewGuid(), ReasoningEffort.High);

        harness.ModelPassedToResolver.ShouldBeNull("the default provider's think alias must not reach another provider's agent");
        harness.FactoryCalls.ShouldBe(0);
        harness.ResolvedExecutorCalls.ShouldBe(1);
    }

    [Fact]
    public async Task AgentWithAReasoningModel_IsNotSwitched()
    {
        var harness = new Harness(agentProvider: DefaultProvider, agentModel: "o3");

        await harness.RunAsync(agentId: Guid.NewGuid(), ReasoningEffort.High);

        harness.ModelPassedToResolver.ShouldBeNull("an agent's own reasoning model must not be overridden by the alias");
        harness.FactoryCalls.ShouldBe(0);
        harness.ResolvedExecutorCalls.ShouldBe(1);
    }

    [Fact]
    public async Task AgentOnTheDefaultProviderWithAPlainModel_SwitchesToThatProvidersThinkAlias()
    {
        var harness = new Harness(agentProvider: DefaultProvider, agentModel: "gpt-4o-mini");

        await harness.RunAsync(agentId: Guid.NewGuid(), ReasoningEffort.High);

        harness.FactoryCalls.ShouldBe(1);
        harness.FactoryProvider.ShouldBe(DefaultProvider);
        harness.FactoryModel.ShouldBe(DefaultThinkModel);
        harness.ThinkExecutorCalls.ShouldBe(1);
        harness.ResolvedExecutorCalls.ShouldBe(0);
    }

    [Fact]
    public async Task AgentRequestWithoutReasoningEffort_IsNotSwitched()
    {
        var harness = new Harness(agentProvider: DefaultProvider, agentModel: "gpt-4o-mini");

        await harness.RunAsync(agentId: Guid.NewGuid(), effort: null);

        harness.FactoryCalls.ShouldBe(0);
        harness.ResolvedExecutorCalls.ShouldBe(1);
    }

    [Fact]
    public async Task AdHocRequest_StillSwitchesBeforeResolution()
    {
        var harness = new Harness(agentProvider: DefaultProvider, agentModel: null);

        await harness.RunAsync(agentId: null, ReasoningEffort.High);

        harness.ModelPassedToResolver.ShouldBe(DefaultThinkModel);
        harness.FactoryCalls.ShouldBe(0);
    }

    private sealed class Harness
    {
        private readonly AgentRuntime _runtime;

        public Harness(string agentProvider, string? agentModel)
        {
            var accessor = new AgentExecutionContextAccessor();
            _ = accessor.Properties;

            var resolvedExecutor = CountingExecutor(() => ResolvedExecutorCalls++);
            var thinkExecutor = CountingExecutor(() => ThinkExecutorCalls++);

            var resolver = new Mock<IAgentResolver>();
            resolver
                .Setup(x => x.ResolveAgentAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<List<string>?>(), It.IsAny<CancellationToken>(), It.IsAny<List<string>?>()))
                .ReturnsAsync((Guid? agentId, string? _, string? model, List<string>? _, CancellationToken _, List<string>? _) =>
                {
                    ModelPassedToResolver = model;
                    var effectiveModel = agentId.HasValue ? model ?? agentModel : model;
                    return AgentResolution.Success(resolvedExecutor, agentProvider, effectiveModel, agentId,
                        creationParameters: new AgentCreationParameters("Be helpful.", "assistant", null, null, null, null));
                });
            resolver
                .Setup(x => x.BuildChatMessageAsync(It.IsAny<string?>(), It.IsAny<List<ContentPartDto>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string? message, List<ContentPartDto>? _, CancellationToken _) => new ChatMessage(ChatRole.User, message ?? string.Empty));

            var factory = new Mock<IAgentFactory>();
            factory.Setup(f => f.CreateAgentAsync(
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                    It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(),
                    It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .Callback<string?, string?, string?, string?, IEnumerable<string>?, double?, int?, AgentExecutorOptions?, IEnumerable<string>?, IEnumerable<string>?, Guid?, CancellationToken>(
                    (provider, model, _, _, _, _, _, _, _, _, _, _) =>
                    {
                        FactoryCalls++;
                        FactoryProvider = provider;
                        FactoryModel = model;
                    })
                .ReturnsAsync(thinkExecutor);

            var runStore = new Mock<IRunStore>();
            var traceStore = new Mock<ITraceStore>();
            var runTracker = new RunTracker(runStore.Object, traceStore.Object, Mock.Of<ILogger<RunTracker>>());

            var options = new AIOptions
            {
                DefaultProvider = DefaultProvider,
                Providers = new Dictionary<string, ProviderOptions>
                {
                    [DefaultProvider] = new() { Enabled = true, DefaultModel = "gpt-4o-mini", Models = new() { ["think"] = DefaultThinkModel } },
                    ["Anthropic"] = new() { Enabled = true, DefaultModel = "claude-sonnet-4" }
                }
            };

            _runtime = new AgentRuntime(
                resolver.Object,
                factory.Object,
                Mock.Of<IRepository<Agent, Guid>>(),
                runTracker,
                new WorkflowDelegator(Mock.Of<IWorkflowService>(), runStore.Object, runTracker),
                accessor,
                new ServiceCollection().BuildServiceProvider(),
                new StaticOptionsMonitor<AIOptions>(options),
                new EventPublisher(null, Mock.Of<IServiceScopeFactory>(), Mock.Of<ILogger<EventPublisher>>()),
                Mock.Of<ILogger<AgentRuntime>>());
        }

        public string? ModelPassedToResolver { get; private set; }
        public int FactoryCalls { get; private set; }
        public string? FactoryProvider { get; private set; }
        public string? FactoryModel { get; private set; }
        public int ResolvedExecutorCalls { get; private set; }
        public int ThinkExecutorCalls { get; private set; }

        public Task<AgentRunResult> RunAsync(Guid? agentId, ReasoningEffort? effort)
            => _runtime.RunAsync(new AgentRunRequest { AgentId = agentId, UserMessage = "think hard", ReasoningEffort = effort });

        private static AgentExecutor CountingExecutor(Action onCall)
        {
            var client = new Mock<IChatClient>();
            client.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
                .Callback(onCall)
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
            return new AgentExecutor(client.Object, new AgentExecutorOptions { Name = "assistant" });
        }
    }
}
