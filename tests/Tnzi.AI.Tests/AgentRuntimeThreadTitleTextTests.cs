namespace Tnzi.AI.Tests;

/// <summary>
/// 新线程的标题取自本轮模型实际收到的用户文本（守卫改写之后、带内容块时取内容块），而不是请求原文。
/// </summary>
/// <remarks>
/// 标题会落库（兜底标题是用户文本的前缀），还会交给 LLM 生成正式标题。输入守卫把卡号脱敏之后，
/// 历史与模型都只见到脱敏文本，标题若仍取 <c>Request.UserMessage</c>，原文就以线程标题的形式留在线程列表里；
/// 只带内容块、不带 Message 的请求则根本得不到标题。
/// </remarks>
public class AgentRuntimeThreadTitleTextTests
{
    private const string RawMessage = "my card is 4111 1111 1111 1111";
    private const string RedactedMessage = "my card is [REDACTED]";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewThread_GuardrailRedactedMessage_TitleUsesTheRedactedText(bool streaming)
    {
        var harness = new Harness(new AgentRunRequest { AgentId = Guid.NewGuid(), UserMessage = RawMessage });
        harness.Middleware.OnBefore = context =>
        {
            context.IsNewThread = true;
            context.Request.ThreadId = Guid.NewGuid();
            context.EffectiveUserMessage = RedactedMessage;
        };

        await harness.RunAsync(streaming);

        harness.EventPublisher.Verify(x => x.HandleNewThreadTitleAsync(
            harness.Request, It.IsAny<AgentRunResult>(), RedactedMessage), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewThread_ContentPartsOnly_TitleUsesThePartText(bool streaming)
    {
        var harness = new Harness(new AgentRunRequest
        {
            AgentId = Guid.NewGuid(),
            ContentParts = [new TextContentPartDto { Text = "summarise the quarterly report" }]
        });
        harness.Middleware.OnBefore = context =>
        {
            context.IsNewThread = true;
            context.Request.ThreadId = Guid.NewGuid();
        };

        await harness.RunAsync(streaming);

        harness.EventPublisher.Verify(x => x.HandleNewThreadTitleAsync(
            harness.Request, It.IsAny<AgentRunResult>(), "summarise the quarterly report"), Times.Once);
    }

    [Fact]
    public async Task EventPublisher_GivenTheUserTurnText_PublishesItInsteadOfTheRequestMessage()
    {
        var captured = new List<ThreadFirstReplyCompletedEvent>();
        var eventBus = new Mock<IEventBus>();
        eventBus.Setup(x => x.PublishAsync(It.IsAny<ThreadFirstReplyCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ThreadFirstReplyCompletedEvent, CancellationToken>((evt, _) => captured.Add(evt))
            .Returns(Task.CompletedTask);
        var threadService = new Mock<IAgentThreadService>();
        var services = new ServiceCollection()
            .AddSingleton(eventBus.Object)
            .AddSingleton(threadService.Object)
            .BuildServiceProvider();
        var publisher = new EventPublisher(eventBus.Object, services.GetRequiredService<IServiceScopeFactory>(), Mock.Of<ILogger<EventPublisher>>());
        var threadId = Guid.NewGuid();

        await publisher.HandleNewThreadTitleAsync(
            new AgentRunRequest { ThreadId = threadId, UserMessage = RawMessage },
            new AgentRunResult { Response = "noted" },
            RedactedMessage);

        captured.ShouldHaveSingleItem().UserMessage.ShouldBe(RedactedMessage);
        threadService.Verify(x => x.UpdateTitleAsync(threadId, It.Is<string>(t => !t.Contains("4111"))), Times.Once);
    }

    private sealed class Harness
    {
        public AgentRunRequest Request { get; }
        public AgentRuntime Runtime { get; }
        public Mock<IEventPublisher> EventPublisher { get; } = new();
        public ProbeMiddleware Middleware { get; } = new();

        public Harness(AgentRunRequest request)
        {
            Request = request;
            var resolver = new Mock<IAgentResolver>();
            resolver.Setup(x => x.ResolveAgentAsync(request.AgentId, null, null, null, It.IsAny<CancellationToken>(), null))
                .ReturnsAsync(AgentResolution.Success(Mock.Of<IAgentExecutor>(), "test", "gpt-5", request.AgentId));

            var sp = new ServiceCollection()
                .AddSingleton<IAiMiddleware>(Middleware)
                .BuildServiceProvider();

            Runtime = new AgentRuntime(
                resolver.Object,
                Mock.Of<IAgentFactory>(),
                Mock.Of<IRepository<Agent, Guid>>(),
                new RunTracker(Mock.Of<IRunStore>(), Mock.Of<ITraceStore>(), Mock.Of<ILogger<RunTracker>>()),
                Mock.Of<IWorkflowDelegator>(),
                new AgentExecutionContextAccessor(),
                sp,
                Mock.Of<IOptionsMonitor<AIOptions>>(),
                EventPublisher.Object,
                Mock.Of<ILogger<AgentRuntime>>());
        }

        public async Task RunAsync(bool streaming)
        {
            if (!streaming)
            {
                await Runtime.RunAsync(Request);
                return;
            }

            await foreach (var _ in Runtime.RunStreamingAsync(Request))
            {
            }
        }
    }

    private sealed class ProbeMiddleware : IAiMiddleware
    {
        public Action<AiMiddlewareContext> OnBefore { get; set; } = _ => { };

        public int Order => 0;

        public Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
        {
            OnBefore(context);
            return Task.FromResult(new AgentRunResult { Response = "noted", FinishReason = FinishReasons.Stop });
        }

        public IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, CancellationToken cancellationToken = default)
        {
            OnBefore(context);
            return Chunks();
        }

#pragma warning disable CS1998 // 迭代器只按需 yield
        private static async IAsyncEnumerable<AgentStreamChunk> Chunks()
        {
            yield return new AgentStreamChunk { Text = "noted" };
            yield return new AgentStreamChunk { FinishReason = FinishReasons.Stop };
        }
#pragma warning restore CS1998
    }
}
