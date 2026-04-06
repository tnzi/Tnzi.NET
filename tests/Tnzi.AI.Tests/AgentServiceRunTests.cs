namespace Tnzi.AI.Tests;

public class AgentServiceRunTests
{
    [Fact]
    public async Task RunAsync_RuntimeReturnsGuardrailRejectedFinishReason_ReturnsFailedResult()
    {
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(x => x.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult
            {
                Response = "Input rejected by guardrail",
                FinishReason = FinishReasons.GuardrailRejected
            });

        var service = CreateService(runtime.Object);

        var result = await service.RunAsync(Guid.NewGuid(), "hello");

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe("Input rejected by guardrail");
        result.Code.ShouldBe(400);
        result.ErrorCode.ShouldBe(ErrorCodes.GuardrailRejected);
    }

    [Fact]
    public async Task RunAsync_RuntimeReturnsSuccessfulResult_PropagatesFinishReasonModelAndReasoning()
    {
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(x => x.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult
            {
                Response = "done",
                FinishReason = FinishReasons.Stop,
                Model = "claude-sonnet",
                FinalAgentName = "reviewer",
                Reasoning = "trace"
            });

        var service = CreateService(runtime.Object);

        var result = await service.RunAsync(Guid.NewGuid(), "hello");

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.FinishReason.ShouldBe(FinishReasons.Stop);
        result.Data.Model.ShouldBe("claude-sonnet");
        result.Data.FinalAgentName.ShouldBe("reviewer");
        result.Data.Reasoning.ShouldBe("trace");
    }

    [Fact]
    public async Task RunAsync_RuntimeReturnsMaxHandoffsFinishReason_ReturnsFailedResult()
    {
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(x => x.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult
            {
                Response = "Max handoff limit reached",
                FinishReason = FinishReasons.MaxHandoffs
            });

        var service = CreateService(runtime.Object);

        var result = await service.RunAsync(Guid.NewGuid(), "hello");

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe("Max handoff limit reached");
        result.Code.ShouldBe(500);
        result.ErrorCode.ShouldBe(ErrorCodes.AgentRunFailed);
    }

    [Fact]
    public async Task RunStreamingAsync_PropagatesStructuredChunkFields()
    {
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(x => x.RunStreamingAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(
            [
                new AgentStreamChunk
                {
                    EventType = MiddlewareEventTypes.TodosUpdated,
                    Model = "claude-think",
                    Todos =
                    [
                        new TodoItemDto
                        {
                            Content = "Ship fix",
                            Status = TodoStatus.Completed,
                            Order = 1
                        }
                    ],
                    Artifacts =
                    [
                        new AgentArtifactDto
                        {
                            FileName = "result.md",
                            VirtualPath = "/artifacts/result.md"
                        }
                    ],
                    FinishReason = FinishReasons.RequiresClarification
                }
            ]));

        var service = CreateService(runtime.Object);
        var events = new List<StreamEvent>();

        await foreach (var item in service.RunStreamingAsync(Guid.NewGuid(), "hello"))
        {
            events.Add(item);
        }

        events.Count.ShouldBe(2);
        events[0].EventType.ShouldBe(MiddlewareEventTypes.TodosUpdated);
        events[0].Model.ShouldBe("claude-think");
        var todos = events[0].Todos;
        todos.ShouldNotBeNull();
        todos.Count.ShouldBe(1);
        var artifacts = events[0].Artifacts;
        artifacts.ShouldNotBeNull();
        artifacts.Count.ShouldBe(1);
        events[1].IsDone.ShouldBeTrue();
        events[1].Model.ShouldBe("claude-think");
        events[1].FinishReason.ShouldBe(FinishReasons.RequiresClarification);
    }

    private static AgentService CreateService(IAgentRuntime runtime)
    {
        return new AgentService(
            Mock.Of<IRepository<Agent, Guid>>(),
            Mock.Of<IRepository<AgentVersion, Guid>>(),
            runtime,
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
