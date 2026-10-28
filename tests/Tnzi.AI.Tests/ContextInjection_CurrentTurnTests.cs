namespace Tnzi.AI.Tests;

/// <summary>
/// 上下文提供器（RAG / 记忆召回）必须按<b>本轮</b>用户消息检索。
/// </summary>
/// <remarks>
/// 本轮用户消息要到 AgentRuntime 构建模型输入时才追加，ContextInjectionMiddleware 运行时
/// <c>context.Messages</c> 里只有 HistoryMiddleware 加载的历史。此前直接把它交给提供器：新线程首轮
/// 检索输入为空、什么都注入不了；之后每一轮都拿上一轮的问题（加上整段历史）去检索。
/// 这里用真实的 HistoryMiddleware + ContextInjectionMiddleware + TextSearchProvider 驱动，
/// 不预先往 context.Messages 里放本轮消息（旧夹具正是这样掩盖了缺陷）。
/// </remarks>
[Collection("ContextInjectionCache")]
public class ContextInjection_CurrentTurnTests
{
    public ContextInjection_CurrentTurnTests()
    {
        ContextInjectionMiddleware.ClearAllCachesForTesting();
    }

    [Fact]
    public async Task NewThread_FirstTurn_SearchesWithTheCurrentQuestion()
    {
        var harness = new Harness(history: []);

        await harness.RunAsync("What is our refund policy?");

        harness.Queries.ShouldHaveSingleItem().ShouldBe("What is our refund policy?");
    }

    [Fact]
    public async Task LaterTurn_SearchesWithTheNewQuestionNotThePreviousOne()
    {
        var harness = new Harness(history:
        [
            new ChatMessage(ChatRole.User, "What is our refund policy?"),
            new ChatMessage(ChatRole.Assistant, "Refunds are available within 30 days.")
        ]);

        await harness.RunAsync("and for EU customers?");

        var query = harness.Queries.ShouldHaveSingleItem();
        query.ShouldEndWith("and for EU customers?");
        // 追问要靠上一轮才知道在问什么：最近的消息作上下文保留
        query.ShouldContain("What is our refund policy?");
    }

    [Fact]
    public async Task LongThread_SearchInputIsBoundedToRecentMessages()
    {
        var history = Enumerable.Range(0, 40)
            .Select(i => new ChatMessage(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, $"old message {i:D2}"))
            .ToList();
        var harness = new Harness(history);

        await harness.RunAsync("current question");

        var query = harness.Queries.ShouldHaveSingleItem();
        query.ShouldEndWith("current question");
        query.ShouldNotContain("old message 00");
        query.Split('\n').Length.ShouldBe(new TextSearchOptions().RecentMessageMemoryLimit + 1);
    }

    [Fact]
    public async Task ProviderInput_DoesNotLeakTheCurrentTurnIntoTheModelMessages()
    {
        // 提供器拿到的是副本：AgentRuntime 会再追加一次本轮消息，写进 context.Messages 会让模型看到两遍
        var harness = new Harness(history: []);

        var context = await harness.RunAsync("What is our refund policy?");

        context.Messages.ShouldNotContain(m => m.Role == ChatRole.User && m.Text == "What is our refund policy?");
    }

    [Fact]
    public async Task GuardrailRedactedTurn_IsWhatTheProvidersSearchWith()
    {
        var harness = new Harness(history: []);

        await harness.RunAsync("card 4111-1111-1111-1111 was charged twice", effectiveUserMessage: "card [CARD] was charged twice");

        harness.Queries.ShouldHaveSingleItem().ShouldBe("card [CARD] was charged twice");
    }

    private sealed class Harness
    {
        private readonly Guid _threadId = Guid.NewGuid();
        private readonly HistoryMiddleware _history;
        private readonly ContextInjectionMiddleware _contextInjection;

        public Harness(List<ChatMessage> history)
        {
            var threadService = new Mock<IAgentThreadInternalService>();
            threadService
                .Setup(s => s.GetOrCreateThreadAsync(_threadId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((_threadId, false));
            threadService
                .Setup(s => s.GetMessageHistoryAsync(_threadId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => history.ToList());

            var aiOptions = new AIOptions();
            aiOptions.ContextProviders.Enabled = true;
            var options = new StaticOptionsMonitor<AIOptions>(aiOptions);
            _history = new HistoryMiddleware(threadService.Object, options, NullLogger<HistoryMiddleware>.Instance);

            var search = new Mock<ITextSearchService>();
            search
                .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<TextSearchFilter?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Callback<string, TextSearchFilter?, int, CancellationToken>((query, _, _, _) => Queries.Add(query))
                .ReturnsAsync(Array.Empty<TextSearchResult>());

            var factory = new CompositeContextProviderFactory(
                [new TextSearchTestContributor(search.Object)],
                options,
                new HeuristicTokenEstimator(),
                NullLoggerFactory.Instance,
                NullLogger<CompositeContextProviderFactory>.Instance);
            _contextInjection = new ContextInjectionMiddleware(factory, NullLogger<ContextInjectionMiddleware>.Instance);
        }

        public List<string> Queries { get; } = [];

        public async Task<AiMiddlewareContext> RunAsync(string userMessage, string? effectiveUserMessage = null)
        {
            var context = new AiMiddlewareContext
            {
                Request = new AgentRunRequest { ThreadId = _threadId, UserMessage = userMessage, UserId = Guid.NewGuid() },
                Agent = AgentResolution.Success(agent: null!, provider: "test", model: "test-model", agentId: null),
                ServiceProvider = new ServiceCollection().BuildServiceProvider(),
                EffectiveUserMessage = effectiveUserMessage
            };

            await _history.InvokeAsync(context, (ctx, ct) =>
                _contextInjection.InvokeAsync(ctx, (_, _) => Task.FromResult(new AgentRunResult { Response = "ok" }), ct));
            return context;
        }
    }

    private sealed class TextSearchTestContributor(ITextSearchService search) : IContextProviderContributor
    {
        public int Order => 0;

        public IContextProvider? TryCreate(ContextProviderCreationContext context)
            => new TextSearchProvider(search, new TextSearchOptions(), NullLogger<TextSearchProvider>.Instance);
    }
}
