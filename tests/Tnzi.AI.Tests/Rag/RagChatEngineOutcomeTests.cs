using Tnzi.AI.Rag.Dtos;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// <c>/api/rag/chat(/stream)</c> 把运行的结局如实交给客户端：失败不是一个回答，流里带着线程 Id。
/// </summary>
/// <remarks>
/// ★ 此前流式路径只拷贝文本、结束原因与用量：运行失败（agent 不存在 / 已停用、护栏拒绝、模型报错）被转成
/// <c>IsError=false</c> 的空事件，客户端当成一次空回答；首轮不带 <c>threadId</c> 的请求建了线程，却没有任何事件把它的 Id
/// 带回去，下一轮只能再开新线程，流式端点上的「多轮对话」不成立，每轮还留下一条孤儿线程。
/// </remarks>
public class RagChatEngineOutcomeTests
{
    private readonly Mock<IRagRetriever> _retriever = new();
    private readonly Mock<IAgentDispatchFacade> _dispatcher = new();
    private readonly Mock<IRagAccessAuthorizer> _authorizer = new();

    public RagChatEngineOutcomeTests()
    {
        _retriever
            .Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<RagRetrievalOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RetrievalResult { Content = "chunk", Score = 0.9 }]);
        _authorizer
            .Setup(a => a.AuthorizeQueryAsync(It.IsAny<IReadOnlyList<Guid>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Guid>>.Success([Guid.NewGuid()]));
    }

    [Fact]
    public async Task Chat_GuardrailRejection_IsA400_NotAnAnswer()
    {
        _dispatcher
            .Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult { Response = "I can't help with that.", FinishReason = FinishReasons.GuardrailRejected });

        var result = await CreateEngine().ChatAsync(new RagChatRequest { Query = "hi" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.ErrorCode.ShouldBe(ErrorCodes.GuardrailRejected);
    }

    [Fact]
    public async Task Chat_RunError_IsA500_NotAnAnswer()
    {
        _dispatcher
            .Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult { Response = "Agent not found", FinishReason = FinishReasons.Error });

        var result = await CreateEngine().ChatAsync(new RagChatRequest { Query = "hi" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(500);
    }

    [Fact]
    public async Task ChatStreaming_RunError_EndsWithAnErrorEvent()
    {
        // 与 AgentRuntime 解析不到 agent 时产出的块同形：没有文本，只有错误与结束原因
        SetupStream(_ => [new AgentStreamChunk { Error = "Agent not found", FinishReason = FinishReasons.Error }]);

        var events = await Collect(new RagChatRequest { Query = "hi" });

        var done = events.Single(e => e.IsDone);
        done.IsError.ShouldBeTrue();
        done.ErrorMessage.ShouldBe("Agent not found");
        events.ShouldContain(e => e.IsError && e.ErrorCode == ErrorCodes.ChatFailed);
    }

    [Fact]
    public async Task ChatStreaming_GuardrailRejection_CarriesTheGuardrailCode()
    {
        SetupStream(_ => [new AgentStreamChunk { Error = "Blocked by policy", FinishReason = FinishReasons.GuardrailRejected }]);

        var events = await Collect(new RagChatRequest { Query = "hi" });

        events.ShouldContain(e => e.IsError && e.ErrorCode == ErrorCodes.GuardrailRejected);
        events.Single(e => e.IsDone).IsError.ShouldBeTrue();
    }

    [Fact]
    public async Task ChatStreaming_FirstTurn_ReturnsTheThreadTheRunCreated()
    {
        // 线程解析中间件在首轮不带 threadId 时建线程并回写到运行请求上
        var created = Guid.NewGuid();
        SetupStream(request =>
        {
            request.ThreadId = created;
            return [new AgentStreamChunk { Text = "hello" }, new AgentStreamChunk { Text = " there", FinishReason = "stop" }];
        });

        var events = await Collect(new RagChatRequest { Query = "hi" });

        events.Single(e => e.IsDone).ThreadId.ShouldBe(created);
        events.Where(e => e.Delta != null).ShouldAllBe(e => e.ThreadId == created);
        string.Concat(events.Select(e => e.Delta)).ShouldBe("hello there");
    }

    [Fact]
    public async Task ChatStreaming_Success_EndsOnceWithCitationsAndUsage()
    {
        SetupStream(_ =>
        [
            new AgentStreamChunk { Text = "answer" },
            new AgentStreamChunk
            {
                FinishReason = "stop",
                Usage = new TokenUsageDto { InputTokens = 10, OutputTokens = 2, TotalTokens = 12 }
            }
        ]);

        var events = await Collect(new RagChatRequest { Query = "hi", IncludeCitations = true });

        var done = events.Single(e => e.IsDone);
        done.IsError.ShouldBeFalse();
        done.FinishReason.ShouldBe("stop");
        done.Usage.ShouldNotBeNull().TotalTokens.ShouldBe(12);
        done.Citations.ShouldNotBeNull().ShouldHaveSingleItem().Text.ShouldBe("chunk");
        events.Last().ShouldBeSameAs(done);
    }

    [Fact]
    public async Task ChatStreaming_StreamWithoutFinishReason_StillEndsWithADoneEvent()
    {
        SetupStream(_ => [new AgentStreamChunk { Text = "partial" }]);

        var events = await Collect(new RagChatRequest { Query = "hi" });

        var done = events.Last();
        done.IsDone.ShouldBeTrue();
        done.IsError.ShouldBeFalse();
    }

    private void SetupStream(Func<AgentRunRequest, AgentStreamChunk[]> produce)
        => _dispatcher
            .Setup(r => r.RunStreamingAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns((AgentRunRequest request, CancellationToken _) => Chunks(() => produce(request)));

    private static async IAsyncEnumerable<AgentStreamChunk> Chunks(Func<AgentStreamChunk[]> produce)
    {
        await Task.CompletedTask;
        foreach (var chunk in produce())
        {
            yield return chunk;
        }
    }

    private async Task<List<StreamEvent>> Collect(RagChatRequest request)
    {
        var events = new List<StreamEvent>();
        await foreach (var evt in CreateEngine().ChatStreamingAsync(request))
        {
            events.Add(evt);
        }

        return events;
    }

    private RagChatEngine CreateEngine()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return new RagChatEngine(services.BuildServiceProvider(), _retriever.Object, _dispatcher.Object, _authorizer.Object,
            new StaticOptionsMonitor<AIRagOptions>(new AIRagOptions()));
    }
}
