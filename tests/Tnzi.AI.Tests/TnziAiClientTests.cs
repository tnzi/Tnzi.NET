using Tnzi.Exceptions;

namespace Tnzi.AI.Tests;

public class TnziAiClientTests
{
    [Fact]
    public async Task ChatAsync_CallsAgentRuntime_ReturnsResponse()
    {
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult
            {
                Response = "Hello from AI",
                ThreadId = Guid.NewGuid(),
                FinishReason = FinishReasons.Stop
            });

        var threadService = new Mock<IAgentThreadService>();
        threadService.Setup(s => s.CreateAsync(It.IsAny<CreateAgentThreadDto>()))
            .ReturnsAsync(Result<AgentThreadDto>.Success(new AgentThreadDto { Id = Guid.NewGuid() }));

        var client = new TnziAiClient(runtime.Object, threadService.Object);

        var response = await client.ChatAsync("Hello");

        Assert.Equal("Hello from AI", response.Text);
        Assert.NotNull(response.ThreadId);
    }

    [Fact]
    public async Task ChatAsync_WithThreadId_PassesToRuntime()
    {
        var threadId = Guid.NewGuid();
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(r => r.RunAsync(It.Is<AgentRunRequest>(req => req.ThreadId == threadId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult
            {
                Response = "Continuing conversation",
                ThreadId = threadId,
                FinishReason = FinishReasons.Stop
            });

        var client = new TnziAiClient(runtime.Object, null);

        var response = await client.ChatAsync("Continue", threadId);

        Assert.Equal(threadId, response.ThreadId);
        runtime.Verify(r => r.RunAsync(
            It.Is<AgentRunRequest>(req => req.ThreadId == threadId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChatAsync_WithOptions_MapsThem()
    {
        var agentId = Guid.NewGuid();
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(r => r.RunAsync(
                It.Is<AgentRunRequest>(req => req.AgentId == agentId && req.OperationType == AIOperationType.AgentRun),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult { Response = "OK", FinishReason = FinishReasons.Stop });

        var client = new TnziAiClient(runtime.Object, null);

        await client.ChatAsync("Hi", options: new AiClientOptions { AgentId = agentId });

        runtime.Verify(r => r.RunAsync(
            It.Is<AgentRunRequest>(req => req.AgentId == agentId && req.OperationType == AIOperationType.AgentRun),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChatAsync_PropagatesStructuredNonStreamingFields()
    {
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult
            {
                Response = "Need one more detail",
                FinishReason = FinishReasons.RequiresClarification,
                Status = AgentRunStatus.RequiresClarification,
                Model = "gpt-5",
                Provider = "openai",
                Reasoning = "thinking",
                HandoffPath = ["planner", "writer"],
                FinalAgentName = "writer",
                Suggestions = ["Tell me more about X"],
                Artifacts = [new AgentArtifactDto { FileName = "draft.md", VirtualPath = "/artifacts/draft.md" }],
                ClarificationQuestion = "Which region should I use?"
            });

        var client = new TnziAiClient(runtime.Object, null);

        var response = await client.ChatAsync("Hello");

        response.Status.ShouldBe(AgentRunStatus.RequiresClarification);
        response.Model.ShouldBe("gpt-5");
        response.Provider.ShouldBe("openai");
        response.Reasoning.ShouldBe("thinking");
        response.HandoffPath.ShouldNotBeNull();
        response.FinalAgentName.ShouldBe("writer");
        response.Suggestions!.ShouldContain("Tell me more about X");
        response.Artifacts.ShouldNotBeNull();
        response.ClarificationQuestion.ShouldBe("Which region should I use?");
    }

    [Fact]
    public async Task ChatAsync_WhenQuotaExceeded_ThrowsBusinessException()
    {
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult
            {
                Response = "Quota exceeded",
                FinishReason = FinishReasons.QuotaExceeded
            });

        var client = new TnziAiClient(runtime.Object, null);

        var ex = await Should.ThrowAsync<BusinessException>(() => client.ChatAsync("Hello"));

        ex.Code.ShouldBe(ErrorCodes.QuotaExceeded);
        ex.HttpStatusCode.ShouldBe(429);
    }

    [Fact]
    public async Task ChatAsync_WhenGuardrailRejected_ThrowsBusinessException()
    {
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult
            {
                Response = "Rejected by guardrail",
                FinishReason = FinishReasons.GuardrailRejected
            });

        var client = new TnziAiClient(runtime.Object, null);

        var ex = await Should.ThrowAsync<BusinessException>(() => client.ChatAsync("Hello"));

        ex.Code.ShouldBe(ErrorCodes.GuardrailRejected);
        ex.HttpStatusCode.ShouldBe(400);
    }

    [Fact]
    public async Task ChatAsync_WhenMaxHandoffsReached_ThrowsBusinessException()
    {
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult
            {
                Response = "Max handoff limit reached",
                FinishReason = FinishReasons.MaxHandoffs
            });

        var client = new TnziAiClient(runtime.Object, null);

        var ex = await Should.ThrowAsync<BusinessException>(() => client.ChatAsync("Hello"));

        ex.Code.ShouldBe(ErrorCodes.AgentRunFailed);
        ex.HttpStatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task CreateThreadAsync_CallsThreadService()
    {
        var expectedId = Guid.NewGuid();
        var runtime = new Mock<IAgentRuntime>();
        var threadService = new Mock<IAgentThreadService>();
        threadService.Setup(s => s.CreateAsync(It.IsAny<CreateAgentThreadDto>()))
            .ReturnsAsync(Result<AgentThreadDto>.Success(new AgentThreadDto { Id = expectedId }));

        var client = new TnziAiClient(runtime.Object, threadService.Object);

        var threadId = await client.CreateThreadAsync("Test Thread");

        Assert.Equal(expectedId, threadId);
    }

    [Fact]
    public async Task DeleteThreadAsync_CallsThreadService()
    {
        var threadId = Guid.NewGuid();
        var runtime = new Mock<IAgentRuntime>();
        var threadService = new Mock<IAgentThreadService>();
        threadService.Setup(s => s.DeleteAsync(threadId))
            .ReturnsAsync(Result.Success());

        var client = new TnziAiClient(runtime.Object, threadService.Object);

        await client.DeleteThreadAsync(threadId);

        threadService.Verify(s => s.DeleteAsync(threadId), Times.Once);
    }

    [Fact]
    public async Task ChatStreamingAsync_YieldsChunks()
    {
        var runtime = new Mock<IAgentRuntime>();
        var chunks = new List<AgentStreamChunk>
        {
            new() { Text = "Hello " },
            new() { Text = "world" },
            new() { FinishReason = FinishReasons.Stop }
        };

        runtime.Setup(r => r.RunStreamingAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(chunks));

        var client = new TnziAiClient(runtime.Object, null);

        var events = new List<AiClientStreamEvent>();
        await foreach (var e in client.ChatStreamingAsync("Hi"))
        {
            events.Add(e);
        }

        Assert.Equal(3, events.Count);
        Assert.Equal("Hello ", events[0].Text);
        Assert.Equal("world", events[1].Text);
        Assert.NotNull(events[2].FinishReason);
    }

    [Fact]
    public async Task ChatStreamingAsync_PropagatesStructuredFields()
    {
        var runtime = new Mock<IAgentRuntime>();
        var chunks = new List<AgentStreamChunk>
        {
            new()
            {
                Text = "step",
                Model = "gpt-5-think",
                ReasoningText = "reasoning",
                AgentName = "planner",
                EventType = MiddlewareEventTypes.Clarification,
                EventData = new Dictionary<string, object> { ["type"] = "ApproachChoice" },
                Suggestions = ["Choose A"],
                Todos =
                [
                    new TodoItemDto { Content = "task", Order = 1 }
                ],
                Artifacts =
                [
                    new AgentArtifactDto { FileName = "plan.md", VirtualPath = "/artifacts/plan.md" }
                ],
                ToolCallNames = ["write_todos"]
            }
        };

        runtime.Setup(r => r.RunStreamingAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(chunks));

        var client = new TnziAiClient(runtime.Object, null);
        var events = new List<AiClientStreamEvent>();

        await foreach (var e in client.ChatStreamingAsync("Hi"))
        {
            events.Add(e);
        }

        events.Count.ShouldBe(1);
        events[0].Model.ShouldBe("gpt-5-think");
        events[0].ReasoningText.ShouldBe("reasoning");
        events[0].AgentName.ShouldBe("planner");
        events[0].EventType.ShouldBe(MiddlewareEventTypes.Clarification);
        events[0].EventData.ShouldNotBeNull();
        var suggestions = events[0].Suggestions;
        suggestions.ShouldNotBeNull();
        suggestions.ShouldContain("Choose A");
        events[0].Todos.ShouldNotBeNull();
        events[0].Artifacts.ShouldNotBeNull();
        var toolCallNames = events[0].ToolCallNames;
        toolCallNames.ShouldNotBeNull();
        toolCallNames.ShouldContain("write_todos");
    }

    [Fact]
    public async Task CreateThreadAsync_ThrowsWhenNoThreadService()
    {
        var runtime = new Mock<IAgentRuntime>();
        var client = new TnziAiClient(runtime.Object, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CreateThreadAsync("Test"));
    }

    [Fact]
    public async Task DeleteThreadAsync_ThrowsWhenNoThreadService()
    {
        var runtime = new Mock<IAgentRuntime>();
        var client = new TnziAiClient(runtime.Object, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DeleteThreadAsync(Guid.NewGuid()));
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.CompletedTask;
        }
    }
}
