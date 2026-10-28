using Tnzi.AI.Infrastructure.ContextProviders.Contributors;

namespace Tnzi.AI.Tests.Memory;

/// <summary>
/// 自动沉淀在框架管线里的真实形态：提供器每个请求新建一次，交给它的历史是纯文本重建的。
/// </summary>
/// <remarks>
/// 此前的节流计数是实例字段（每个请求都从 0 开始）、互斥靠在消息里找 <c>save_memory</c> 的工具调用
/// （纯文本历史里永远没有），两者在框架里都从不起作用；提炼也只取最后一问一答，节流跳过的那几轮永远进不了记忆。
/// </remarks>
[Collection("ContextInjectionCache")]
public class MemoryAutoPersistTurnTests
{
    private readonly Guid _threadId = Guid.NewGuid();
    private readonly Dictionary<string, string?> _threadMetadata = new(StringComparer.Ordinal);
    private readonly Mock<IAgentThreadInternalService> _threadService = new();
    private readonly List<string> _extractionInputs = [];
    private readonly Mock<IChatClientFactory> _chatClientFactory = new();
    private readonly Mock<IMemoryStore> _store = new();
    private readonly AgentExecutionContextAccessor _accessor = new();

    public MemoryAutoPersistTurnTests()
    {
        ContextInjectionMiddleware.ClearAllCachesForTesting();

        _threadService
            .Setup(s => s.GetMetadataValueAsync(_threadId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string key, CancellationToken _) => _threadMetadata.GetValueOrDefault(key));
        _threadService
            .Setup(s => s.SetMetadataValueAsync(_threadId, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, string key, string? value, CancellationToken _) =>
            {
                if (value == null) _threadMetadata.Remove(key);
                else _threadMetadata[key] = value;
            })
            .Returns(Task.CompletedTask);

        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Callback((IEnumerable<ChatMessage> messages, ChatOptions? _, CancellationToken _) =>
                _extractionInputs.Add(messages.Last(m => m.Role == ChatRole.User).Text))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "- a durable fact")));
        _chatClientFactory.Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>())).Returns(chatClient.Object);

        _accessor.RestoreProperties(new Dictionary<string, object>());
    }

    private static MemoryOptions Options(int minTurns) => new()
    {
        AutoPersist = true,
        AutoConsolidate = false,
        IncrementalConsolidate = false,
        MinTurnsBetweenExtractions = minTurns
    };

    /// <summary>框架的做法：每一轮新建一个提供器实例。</summary>
    private MemoryContextProvider NewProviderForThisTurn(MemoryOptions options) => new(
        _store.Object,
        new MemoryScope("default"),
        NullLogger<MemoryContextProvider>.Instance,
        _chatClientFactory.Object,
        options,
        memoryConsolidator: null,
        executionContextAccessor: _accessor,
        agentBoundScope: null,
        threadService: _threadService.Object,
        threadId: _threadId);

    /// <summary>第 <paramref name="turn"/> 轮完成时交给提供器的消息：之前各轮的纯文本历史 + 本轮问答。</summary>
    private static List<ChatMessage> CompletedTurn(int turn)
    {
        var messages = new List<ChatMessage>();
        for (var i = 1; i <= turn; i++)
        {
            messages.Add(new ChatMessage(ChatRole.User, $"question {i}"));
            messages.Add(new ChatMessage(ChatRole.Assistant, $"answer {i}"));
        }
        return messages;
    }

    [Fact]
    public async Task Throttle_HoldsAcrossRequests_AndEachExtractionCoversEveryPendingTurn()
    {
        var options = Options(minTurns: 3);

        for (var turn = 1; turn <= 6; turn++)
        {
            await NewProviderForThisTurn(options).OnCompletedAsync(CompletedTurn(turn));
        }

        _extractionInputs.Count.ShouldBe(2);

        _extractionInputs[0].ShouldContain("User: question 1");
        _extractionInputs[0].ShouldContain("User: question 2");
        _extractionInputs[0].ShouldContain("User: question 3");
        _extractionInputs[0].ShouldContain("Assistant: answer 3");

        _extractionInputs[1].ShouldNotContain("question 3");
        _extractionInputs[1].ShouldContain("User: question 4");
        _extractionInputs[1].ShouldContain("User: question 6");
        _extractionInputs[1].ShouldContain("Assistant: answer 6");
    }

    [Fact]
    public async Task Throttle_PendingTurnsAreRecordedOnTheThread()
    {
        var options = Options(minTurns: 3);

        await NewProviderForThisTurn(options).OnCompletedAsync(CompletedTurn(1));
        await NewProviderForThisTurn(options).OnCompletedAsync(CompletedTurn(2));

        _extractionInputs.ShouldBeEmpty();
        _threadMetadata[MemoryContextProvider.PendingTurnsMetadataKey].ShouldBe("2");

        await NewProviderForThisTurn(options).OnCompletedAsync(CompletedTurn(3));

        _extractionInputs.ShouldHaveSingleItem();
        _threadMetadata.ShouldNotContainKey(MemoryContextProvider.PendingTurnsMetadataKey);
    }

    [Fact]
    public async Task SaveMemoryCalledThisRun_SkipsAutoPersistAndRestartsTheCount()
    {
        var options = Options(minTurns: 2);

        await NewProviderForThisTurn(options).OnCompletedAsync(CompletedTurn(1));

        // 第 2 轮模型自己调了 save_memory：历史是纯文本，信号只在运行属性包里
        _accessor.Properties[ContextPropertyKeys.MemorySavedThisRun] = true;
        await NewProviderForThisTurn(options).OnCompletedAsync(CompletedTurn(2));
        _accessor.RestoreProperties(new Dictionary<string, object>());

        _extractionInputs.ShouldBeEmpty();
        _threadMetadata.ShouldNotContainKey(MemoryContextProvider.PendingTurnsMetadataKey);

        await NewProviderForThisTurn(options).OnCompletedAsync(CompletedTurn(3));
        _extractionInputs.ShouldBeEmpty();

        await NewProviderForThisTurn(options).OnCompletedAsync(CompletedTurn(4));
        var input = _extractionInputs.ShouldHaveSingleItem();
        input.ShouldContain("question 3");
        input.ShouldContain("question 4");
        input.ShouldNotContain("question 2");
    }

    [Fact]
    public async Task SaveMemoryTool_MarksTheRunSoAutoPersistSkipsIt()
    {
        var aiOptions = new AIOptions();
        aiOptions.ContextProviders.Memory.EnablePiiProtection = false;
        var tools = new MemoryTools(
            _store.Object,
            NullLogger<MemoryTools>.Instance,
            new StaticOptionsMonitor<AIOptions>(aiOptions),
            currentUser: null,
            executionContextAccessor: _accessor);
        // 运行开始时建好属性包（同 AgentRuntime.EnterRun）：工具在自己的异步方法里写入，写的必须是同一个包
        _accessor.CurrentRequest = new AgentRunRequest { UserId = Guid.NewGuid() };
        _accessor.RestoreProperties(new Dictionary<string, object>());

        await tools.SaveMemoryAsync("User prefers metric units");
        await NewProviderForThisTurn(Options(minTurns: 1)).OnCompletedAsync(CompletedTurn(1));

        _accessor.Properties.ShouldContainKey(ContextPropertyKeys.MemorySavedThisRun);
        _extractionInputs.ShouldBeEmpty();
    }

    /// <summary>
    /// 管线级：真实的 HistoryMiddleware + ContextInjectionMiddleware + MemoryContributor，新线程首轮，
    /// 不往 context.Messages 预放本轮消息。提炼输入必须是本轮的一问一答。
    /// </summary>
    [Fact]
    public async Task Pipeline_FirstTurnOfANewThread_ExtractsFromTheCurrentQuestionAndReply()
    {
        var aiOptions = new AIOptions();
        aiOptions.ContextProviders.Enabled = true;
        aiOptions.ContextProviders.Memory.AutoPersist = true;
        aiOptions.ContextProviders.Memory.AutoConsolidate = false;
        aiOptions.ContextProviders.Memory.IncrementalConsolidate = false;
        aiOptions.ContextProviders.Memory.MinTurnsBetweenExtractions = 1;
        var options = new StaticOptionsMonitor<AIOptions>(aiOptions);

        _threadService
            .Setup(s => s.GetOrCreateThreadAsync(_threadId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((_threadId, false));
        _threadService
            .Setup(s => s.GetMessageHistoryAsync(_threadId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var contributor = new MemoryContributor(
            _store.Object, _chatClientFactory.Object, options, _accessor, NullLoggerFactory.Instance,
            memoryConsolidator: null, threadService: _threadService.Object);
        var factory = new CompositeContextProviderFactory(
            [contributor], options, new HeuristicTokenEstimator(), NullLoggerFactory.Instance,
            NullLogger<CompositeContextProviderFactory>.Instance);
        var contextInjection = new ContextInjectionMiddleware(factory, NullLogger<ContextInjectionMiddleware>.Instance);
        var history = new HistoryMiddleware(_threadService.Object, options, NullLogger<HistoryMiddleware>.Instance);

        var context = new AiMiddlewareContext
        {
            Request = new AgentRunRequest { ThreadId = _threadId, UserMessage = "Remember my VAT number is CA123", UserId = Guid.NewGuid() },
            Agent = AgentResolution.Success(agent: null!, provider: "test", model: "test-model", agentId: null),
            ServiceProvider = new ServiceCollection().BuildServiceProvider()
        };

        await history.InvokeAsync(context, (ctx, ct) =>
            contextInjection.InvokeAsync(ctx, (_, _) =>
                Task.FromResult(new AgentRunResult { Response = "Noted: your VAT number is CA123." }), ct));

        var input = _extractionInputs.ShouldHaveSingleItem();
        input.ShouldBe("User: Remember my VAT number is CA123\nAssistant: Noted: your VAT number is CA123.");
    }

    [Fact]
    public void Snippet_TakesTheLastTurnsAndSkipsSystemMessages()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "## Persistent Memory\nold"),
            new(ChatRole.User, "q1"),
            new(ChatRole.Assistant, "a1"),
            new(ChatRole.User, "q2"),
            new(ChatRole.Assistant, "a2")
        };

        MemoryContextProvider.BuildConversationSnippet(messages, 1).ShouldBe("User: q2\nAssistant: a2");
        MemoryContextProvider.BuildConversationSnippet(messages, 5).ShouldBe("User: q1\nAssistant: a1\nUser: q2\nAssistant: a2");
        MemoryContextProvider.BuildConversationSnippet([new ChatMessage(ChatRole.User, "q")], 1).ShouldBeNull();
    }
}
