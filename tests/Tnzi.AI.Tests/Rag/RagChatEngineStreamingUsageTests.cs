using Tnzi.AI.Rag.Dtos;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// RAG 流式对话的终止事件必须给出整次运行的用量之和。
/// </summary>
/// <remarks>
/// 运行时流里每个 chunk 的 <see cref="AgentStreamChunk.Usage"/> 是一次 LLM 调用的增量；
/// 原样转发会让终止事件只带最后一次调用的用量，工具循环前面几轮的消耗在客户端看来不存在。
/// </remarks>
public class RagChatEngineStreamingUsageTests
{
    [Fact]
    public async Task ChatStreamingAsync_DoneEventCarriesTheTotalAndUsageOnlyChunksAreNotEvents()
    {
        var retriever = new Mock<IRagRetriever>();
        retriever
            .Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<RagRetrievalOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RetrievalResult { Content = "chunk", Score = 0.9 }]);
        var authorizer = new Mock<IRagAccessAuthorizer>();
        authorizer
            .Setup(a => a.AuthorizeQueryAsync(It.IsAny<IReadOnlyList<Guid>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Guid>>.Success([Guid.NewGuid()]));
        var runtime = new Mock<IAgentDispatchFacade>();
        runtime
            .Setup(r => r.RunStreamingAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns(new[]
            {
                new AgentStreamChunk { Usage = new TokenUsageDto { InputTokens = 100, OutputTokens = 10 } },
                new AgentStreamChunk { Text = "answer" },
                new AgentStreamChunk { FinishReason = FinishReasons.Stop, Usage = new TokenUsageDto { InputTokens = 200, OutputTokens = 20 } }
            }.ToAsyncEnumerable());

        var engine = new RagChatEngine(
            new ServiceCollection().AddLogging().BuildServiceProvider(),
            retriever.Object,
            runtime.Object,
            authorizer.Object,
            new StaticOptionsMonitor<AIRagOptions>(new AIRagOptions()));

        var events = new List<StreamEvent>();
        await foreach (var evt in engine.ChatStreamingAsync(new RagChatRequest { Query = "q" }))
        {
            events.Add(evt);
        }

        events.Count.ShouldBe(2);
        events[0].Usage.ShouldBeNull();
        var done = events.Single(e => e.IsDone);
        done.Usage.ShouldNotBeNull();
        done.Usage!.InputTokens.ShouldBe(300);
        done.Usage.OutputTokens.ShouldBe(30);
    }
}
