namespace Tnzi.AI.Tests.Engine;

/// <summary>
/// Handoff / Router 交给目标 Agent 的消息：对话带过去，转出方的人格块不带过去。
/// </summary>
/// <remarks>
/// Handoff 此前取 <c>messages[0]</c> 当「原始用户消息」，而上下文注入把人格块（Soul）与用户画像插在第 0 位、
/// 历史也插在前面。目标收到的要么是转出方的人格（以转出方的身份作答），要么是很久以前那条对话的第一句，
/// 最近的上下文一条不剩。Router 一直带完整对话，但同样把路由者的人格带给了目标。
/// </remarks>
public class HandoffMessageForwardingTests
{
    private const string SourcePersona = "<soul>You are Ava, the front-desk bot.</soul>";
    private const string UserProfile = "<user_profile>Prefers short answers.</user_profile>";
    private const string LatestQuestion = "Where is my invoice for March?";

    private static List<ChatMessage> Conversation() =>
    [
        new(ChatRole.System, SourcePersona),
        new(ChatRole.System, UserProfile),
        new(ChatRole.User, "Hi, I have an older question about my plan."),
        new(ChatRole.Assistant, "Sure, you are on the Pro plan."),
        new(ChatRole.User, LatestQuestion)
    ];

    [Fact]
    public async Task Handoff_ExecuteAsync_ForwardsTheConversationWithoutTheSourcePersona()
    {
        var harness = new Harness();
        var strategy = new HandoffExecutionStrategy(new HandoffConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["billing"] = harness.TargetAgentId },
            MaxHandoffs = 3
        });

        await strategy.ExecuteAsync(
            Harness.CreateNonStreamingRoutingAgent("Receptionist", "handoff_to_agent", "billing"),
            Conversation(), harness.Context, CancellationToken.None);

        AssertTargetReceivedTheConversation(harness.TargetReceived.ShouldHaveSingleItem());
    }

    [Fact]
    public async Task Handoff_ExecuteStreamingAsync_ForwardsTheSameMessagesAsNonStreaming()
    {
        var harness = new Harness();
        var strategy = new HandoffExecutionStrategy(new HandoffConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["billing"] = harness.TargetAgentId },
            MaxHandoffs = 3
        });

        await foreach (var _ in strategy.ExecuteStreamingAsync(
            Harness.CreateStreamingRoutingAgent("Receptionist", "handoff_to_agent", "billing"),
            Conversation(), harness.Context, CancellationToken.None))
        {
        }

        AssertTargetReceivedTheConversation(harness.TargetReceived.ShouldHaveSingleItem());
    }

    [Fact]
    public async Task Router_ExecuteAsync_DoesNotHandTheRoutersPersonaToTheTarget()
    {
        var harness = new Harness();
        var strategy = new RouterExecutionStrategy(new RouterConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["billing"] = harness.TargetAgentId },
            AllowDirectResponse = false
        });

        await strategy.ExecuteAsync(
            Harness.CreateNonStreamingRoutingAgent("Router", "route_to_agent", "billing"),
            Conversation(), harness.Context, CancellationToken.None);

        AssertTargetReceivedTheConversation(harness.TargetReceived.ShouldHaveSingleItem());
    }

    private static void AssertTargetReceivedTheConversation(List<ChatMessage> received)
    {
        received.ShouldNotContain(m => m.Text.Contains("<soul>"), "the source agent's persona must not be handed to the target");
        received.ShouldContain(m => m.Role == ChatRole.User && m.Text == LatestQuestion, "the target must receive the latest user turn as a user message");
        received.ShouldContain(m => m.Role == ChatRole.Assistant && m.Text == "Sure, you are on the Pro plan.", "recent history travels with the conversation");
        received.ShouldContain(m => m.Role == ChatRole.System && m.Text == UserProfile, "the user profile describes the user, not the agent");
    }

    private sealed class Harness
    {
        private const string TargetName = "Billing";

        public Harness()
        {
            var repository = new Mock<IRepository<Agent, Guid>>();
            repository.Setup(x => x.GetAsync(TargetAgentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Agent { Id = TargetAgentId, Name = TargetName, Provider = "test", IsEnabled = true });

            var agentFactory = new Mock<IAgentFactory>();
            agentFactory.Setup(x => x.CreateAgentAsync(
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), TargetName,
                    It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                    It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(),
                    TargetAgentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => CreateRecordingAgent(TargetName));

            Context = new ExecutionStrategyContext
            {
                AgentFactory = agentFactory.Object,
                AgentRepository = repository.Object,
                ServiceProvider = TestHelpers.ServiceProviderWithGrants(),
                ExecutionContextAccessor = new AgentExecutionContextAccessor(),
                Logger = Mock.Of<ILogger>()
            };
        }

        public Guid TargetAgentId { get; } = Guid.NewGuid();
        public ExecutionStrategyContext Context { get; }
        public List<List<ChatMessage>> TargetReceived { get; } = [];

        private AgentExecutor CreateRecordingAgent(string name)
        {
            var client = new Mock<IChatClient>();
            client.Setup(x => x.GetResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IEnumerable<ChatMessage> messages, ChatOptions? _, CancellationToken _) =>
                {
                    TargetReceived.Add(messages.ToList());
                    return new ChatResponse(new ChatMessage(ChatRole.Assistant, "target answer"));
                });
            client.Setup(x => x.GetStreamingResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
                .Returns((IEnumerable<ChatMessage> messages, ChatOptions? _, CancellationToken _) =>
                {
                    TargetReceived.Add(messages.ToList());
                    return Stream(new ChatResponseUpdate(ChatRole.Assistant, "target answer") { FinishReason = ChatFinishReason.Stop });
                });

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
