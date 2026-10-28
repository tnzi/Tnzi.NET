namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// 工具经属性包把结果交给中间件（ask_clarification → ClarificationMiddleware）：真实 AgentRuntime、真实执行器、
/// 真实工具与真实中间件，不加载沙箱、不开 PlanMode、不是子 Agent 运行。
/// </summary>
/// <remarks>
/// 属性包是 AsyncLocal。若运行不在自己的上下文里先把它建好，第一次读取会在某个子异步帧里惰性创建，
/// 那次赋值传不回调用方：工具写进一个包，中间件在自己的帧里读到另一个空包，ask_clarification 静默失效，
/// 运行以 stop 收尾、从不产出 RequiresClarification。ClarificationMiddleware 的单元测试用同步的假 next
/// 在中间件自己的帧里写入，测不出这一点，所以这里走完整链路。
/// </remarks>
public class ClarificationEndToEndTests
{
    [Fact]
    public async Task RunAsync_ToolAsksForClarification_RunEndsRequiringClarification()
    {
        var harness = new Harness();

        var result = await harness.Runtime.RunAsync(harness.Request);

        result.FinishReason.ShouldBe(FinishReasons.RequiresClarification);
        result.ClarificationQuestion.ShouldNotBeNull().ShouldContain("Which environment?");
    }

    [Fact]
    public async Task RunStreamingAsync_ToolAsksForClarification_StreamEndsWithTheClarification()
    {
        var harness = new Harness();

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in harness.Runtime.RunStreamingAsync(harness.Request))
        {
            chunks.Add(chunk);
        }

        chunks.ShouldContain(c => c.FinishReason == FinishReasons.RequiresClarification && c.Text != null && c.Text.Contains("Which environment?"));
    }

    private sealed class Harness
    {
        private readonly Guid _agentId = Guid.NewGuid();
        private int _llmCalls;

        public Harness()
        {
            var accessor = new AgentExecutionContextAccessor();
            var clarificationTools = new ClarificationTools(accessor);
            var askTool = AIFunctionFactory.Create(
                (string question, ClarificationType type) => clarificationTools.AskClarification(question, type),
                "ask_clarification");

            var executor = new AgentExecutor(CreateChatClient(), new AgentExecutorOptions { Name = "assistant", Tools = [askTool] });

            var resolver = new Mock<IAgentResolver>();
            resolver.Setup(x => x.ResolveAgentAsync(_agentId, null, null, null, It.IsAny<CancellationToken>(), null))
                .ReturnsAsync(AgentResolution.Success(executor, "test", "gpt-5", _agentId));
            resolver.Setup(x => x.BuildChatMessageAsync(It.IsAny<string?>(), It.IsAny<List<ContentPartDto>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string? message, List<ContentPartDto>? _, CancellationToken _) => new ChatMessage(ChatRole.User, message ?? string.Empty));

            var sp = new ServiceCollection()
                .AddSingleton<IAiMiddleware>(new ClarificationMiddleware(accessor, NullLogger<ClarificationMiddleware>.Instance))
                .BuildServiceProvider();

            Runtime = new AgentRuntime(
                resolver.Object,
                Mock.Of<IAgentFactory>(),
                Mock.Of<IRepository<Agent, Guid>>(),
                new RunTracker(Mock.Of<IRunStore>(), Mock.Of<ITraceStore>(), Mock.Of<ILogger<RunTracker>>()),
                Mock.Of<IWorkflowDelegator>(),
                accessor,
                sp,
                Mock.Of<IOptionsMonitor<AIOptions>>(),
                Mock.Of<IEventPublisher>(),
                Mock.Of<ILogger<AgentRuntime>>());

            Request = new AgentRunRequest { AgentId = _agentId, UserId = Guid.NewGuid(), UserMessage = "deploy it" };
        }

        public AgentRuntime Runtime { get; }

        public AgentRunRequest Request { get; }

        private IChatClient CreateChatClient()
        {
            var toolCall = new FunctionCallContent("call_1", "ask_clarification", new Dictionary<string, object?>
            {
                ["question"] = "Which environment?",
                ["type"] = nameof(ClarificationType.MissingInfo)
            });

            var client = new Mock<IChatClient>();
            client.Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => ++_llmCalls == 1
                    ? new ChatResponse([new ChatMessage(ChatRole.Assistant, [toolCall])])
                    : new ChatResponse(new ChatMessage(ChatRole.Assistant, "I will wait for your answer.")));
            client.Setup(x => x.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
                .Returns(() => ++_llmCalls == 1
                    ? Stream(new ChatResponseUpdate(ChatRole.Assistant, [toolCall]))
                    : Stream(new ChatResponseUpdate(ChatRole.Assistant, "I will wait for your answer.") { FinishReason = ChatFinishReason.Stop }));
            return client.Object;
        }

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
