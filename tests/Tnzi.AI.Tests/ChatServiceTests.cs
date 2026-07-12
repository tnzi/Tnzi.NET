namespace Tnzi.AI.Tests;

public class ChatServiceTests
{
    [Fact]
    public async Task ChatAsync_RuntimeReturnsErrorFinishReason_ReturnsFailedResult()
    {
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(x => x.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult
            {
                Response = "Agent resolution failed: AI_AGENT_NOT_FOUND",
                FinishReason = FinishReasons.Error
            });

        var service = CreateService(runtime.Object);

        var result = await service.ChatAsync(new ChatRequestDto { Message = "hello" });

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe("Agent resolution failed: AI_AGENT_NOT_FOUND");
        result.Code.ShouldBe(500);
        result.ErrorCode.ShouldBe(ErrorCodes.ChatFailed);
    }

    [Fact]
    public async Task ChatAsync_RuntimeReturnsQuotaExceededFinishReason_ReturnsFailedResult()
    {
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(x => x.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult
            {
                Response = "Quota exceeded",
                FinishReason = FinishReasons.QuotaExceeded
            });

        var service = CreateService(runtime.Object);

        var result = await service.ChatAsync(new ChatRequestDto { Message = "hello" });

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe("Quota exceeded");
        result.Code.ShouldBe(429);
        result.ErrorCode.ShouldBe(ErrorCodes.QuotaExceeded);
    }

    [Fact]
    public async Task ChatAsync_RuntimeReturnsMaxHandoffsFinishReason_ReturnsFailedResult()
    {
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(x => x.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult
            {
                Response = "Max handoff limit reached",
                FinishReason = FinishReasons.MaxHandoffs
            });

        var service = CreateService(runtime.Object);

        var result = await service.ChatAsync(new ChatRequestDto { Message = "hello" });

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe("Max handoff limit reached");
        result.Code.ShouldBe(500);
        result.ErrorCode.ShouldBe(ErrorCodes.ChatFailed);
    }

    [Fact]
    public async Task ChatAsync_RuntimeReturnsSuccessfulResult_PropagatesFinishReasonAndModel()
    {
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(x => x.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult
            {
                Response = "done",
                FinishReason = FinishReasons.Stop,
                Model = "gpt-5-think",
                Reasoning = "reasoning",
                ThreadId = Guid.NewGuid()
            });

        var service = CreateService(runtime.Object);

        var result = await service.ChatAsync(new ChatRequestDto { Message = "hello", Model = "gpt-5" });

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Content.ShouldBe("done");
        result.Data.FinishReason.ShouldBe(FinishReasons.Stop);
        result.Data.Model.ShouldBe("gpt-5-think");
        result.Data.Reasoning.ShouldBe("reasoning");
    }

    [Fact]
    public async Task ChatStreamingAsync_PropagatesStructuredChunkFields()
    {
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(x => x.RunStreamingAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(
            [
                new AgentStreamChunk
                {
                    Text = "Need clarification",
                    FinishReason = FinishReasons.RequiresClarification,
                    Model = "gpt-5-think",
                    EventType = MiddlewareEventTypes.Clarification,
                    EventData = new Dictionary<string, object> { ["type"] = "ApproachChoice" },
                    Suggestions = ["Choose A"],
                    Todos =
                    [
                        new TodoItemDto
                        {
                            Content = "Compare options",
                            Status = TodoStatus.InProgress,
                            Order = 1
                        }
                    ],
                    Artifacts =
                    [
                        new AgentArtifactDto
                        {
                            FileName = "options.md",
                            VirtualPath = "/artifacts/options.md"
                        }
                    ]
                }
            ]));

        var service = CreateService(runtime.Object);
        var events = new List<StreamEvent>();

        await foreach (var item in service.ChatStreamingAsync(new ChatRequestDto { Message = "hello", Model = "gpt-5" }))
        {
            events.Add(item);
        }

        events.Count.ShouldBe(2);
        events[0].Delta.ShouldBe("Need clarification");
        events[0].Model.ShouldBe("gpt-5-think");
        events[0].EventType.ShouldBe(MiddlewareEventTypes.Clarification);
        events[0].EventData.ShouldNotBeNull();
        var suggestions = events[0].Suggestions;
        suggestions.ShouldNotBeNull();
        suggestions.ShouldContain("Choose A");
        events[0].Todos.ShouldNotBeNull();
        events[0].Artifacts.ShouldNotBeNull();
        events[1].IsDone.ShouldBeTrue();
        events[1].Model.ShouldBe("gpt-5-think");
        events[1].FinishReason.ShouldBe(FinishReasons.RequiresClarification);
    }

    [Fact]
    public async Task ChatStreamingAsync_TerminalEvent_CarriesPersistedMessageIds()
    {
        // HistoryMiddleware stamps persisted message ids directly onto the terminal chunk —
        // ChatService must lift those IDs onto the StreamEvent so the client can call
        // message-scoped APIs (e.g. feedback) without an extra round-trip. AsyncLocal cannot
        // back-propagate writes across async iterator yield boundaries, hence the chunk-borne
        // wire format.
        var userId = SequentialGuid.NewGuid();
        var assistantId = SequentialGuid.NewGuid();
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(x => x.RunStreamingAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(
            [
                new AgentStreamChunk { Text = "Hi" },
                new AgentStreamChunk
                {
                    Text = "!",
                    FinishReason = FinishReasons.Stop,
                    UserMessageId = userId,
                    AssistantMessageId = assistantId
                }
            ]));

        var service = CreateService(runtime.Object);
        var events = new List<StreamEvent>();
        await foreach (var item in service.ChatStreamingAsync(new ChatRequestDto { Message = "hello" }))
        {
            events.Add(item);
        }

        var terminal = events.Single(e => e.IsDone);
        terminal.UserMessageId.ShouldBe(userId);
        terminal.AssistantMessageId.ShouldBe(assistantId);
    }

    [Fact]
    public async Task ChatAsync_PersistedMessageIdsSurfaceOnResponse()
    {
        var userId = SequentialGuid.NewGuid();
        var assistantId = SequentialGuid.NewGuid();
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(x => x.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult
            {
                Response = "Hi there",
                FinishReason = FinishReasons.Stop,
                ThreadId = Guid.NewGuid(),
                UserMessageId = userId,
                AssistantMessageId = assistantId
            });

        var service = CreateService(runtime.Object);
        var result = await service.ChatAsync(new ChatRequestDto { Message = "hello" });

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.UserMessageId.ShouldBe(userId);
        result.Data.AssistantMessageId.ShouldBe(assistantId);
    }

    private static ChatService CreateService(IAgentRuntime runtime)
    {
        return new ChatService(
            runtime,
            new StaticOptionsMonitor<AIOptions>(new AIOptions { DefaultProvider = "test" }),
            new ServiceCollection()
                .AddLogging()
                .BuildServiceProvider());
    }

    private static async IAsyncEnumerable<AgentStreamChunk> ToAsyncEnumerable(IEnumerable<AgentStreamChunk> items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.CompletedTask;
        }
    }
}
