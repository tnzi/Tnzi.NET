namespace Tnzi.AI.Tests;

public class EventPublisherTests
{
    [Fact]
    public async Task PublishRunStartedEventAsync_WithEventBus_EmitsEvent()
    {
        var captured = new List<AgentRunStartedEvent>();
        var eventBus = CreateEventBus<AgentRunStartedEvent>(captured);
        var publisher = new EventPublisher(eventBus.Object, Mock.Of<IServiceScopeFactory>(), Mock.Of<ILogger<EventPublisher>>());

        await publisher.PublishRunStartedEventAsync(
            new AgentRunRequest { AgentId = Guid.NewGuid(), UserId = Guid.NewGuid(), ThreadId = Guid.NewGuid() },
            new AgentRun { Id = Guid.NewGuid() },
            isStreaming: true, provider: "openai", model: "gpt-4o",
            AgentExecutionMode.Handoff);

        captured.Count.ShouldBe(1);
        captured[0].IsStreaming.ShouldBeTrue();
        captured[0].Provider.ShouldBe("openai");
        captured[0].Model.ShouldBe("gpt-4o");
        captured[0].ExecutionMode.ShouldBe("Handoff");
    }

    [Fact]
    public async Task PublishRunStartedEventAsync_EventBusNull_DoesNotThrow()
    {
        var publisher = new EventPublisher(eventBus: null, Mock.Of<IServiceScopeFactory>(), Mock.Of<ILogger<EventPublisher>>());

        await publisher.PublishRunStartedEventAsync(
            new AgentRunRequest(), null, false, null, null, AgentExecutionMode.Single);
    }

    [Fact]
    public async Task PublishRunStartedEventAsync_EventBusThrows_SwallowsException()
    {
        var eventBus = new Mock<IEventBus>();
        eventBus.Setup(x => x.PublishAsync(It.IsAny<AgentRunStartedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bus down"));
        var publisher = new EventPublisher(eventBus.Object, Mock.Of<IServiceScopeFactory>(), Mock.Of<ILogger<EventPublisher>>());

        await publisher.PublishRunStartedEventAsync(
            new AgentRunRequest(), null, false, null, null, AgentExecutionMode.Single);
    }

    [Fact]
    public async Task PublishRunCompletedEventAsync_UsesRunStatus_WhenAvailable()
    {
        var captured = new List<AgentRunCompletedEvent>();
        var eventBus = CreateEventBus<AgentRunCompletedEvent>(captured);
        var publisher = new EventPublisher(eventBus.Object, Mock.Of<IServiceScopeFactory>(), Mock.Of<ILogger<EventPublisher>>());

        var run = new AgentRun { Id = Guid.NewGuid(), Status = AgentRunStatus.Completed };
        var result = new AgentRunResult
        {
            Response = "done",
            Provider = "result-provider",
            Model = "result-model",
            FinishReason = FinishReasons.Completed,
            Usage = new TokenUsageDto { InputTokens = 10, OutputTokens = 20 }
        };

        await publisher.PublishRunCompletedEventAsync(
            new AgentRunRequest(), result, run, durationMs: 123, isStreaming: false, actualProvider: "actual-provider");

        captured.Count.ShouldBe(1);
        captured[0].Status.ShouldBe(AgentRunStatus.Completed.ToString());
        captured[0].Provider.ShouldBe("actual-provider");
        captured[0].Model.ShouldBe("result-model");
        captured[0].TotalTokens.ShouldBe(30);
        captured[0].DurationMs.ShouldBe(123);
        captured[0].FinishReason.ShouldBe(FinishReasons.Completed);
    }

    [Fact]
    public async Task PublishRunCompletedEventAsync_RunNull_DerivesStatusFromFinishReason()
    {
        var captured = new List<AgentRunCompletedEvent>();
        var eventBus = CreateEventBus<AgentRunCompletedEvent>(captured);
        var publisher = new EventPublisher(eventBus.Object, Mock.Of<IServiceScopeFactory>(), Mock.Of<ILogger<EventPublisher>>());

        var result = new AgentRunResult
        {
            Response = "blocked",
            FinishReason = FinishReasons.GuardrailRejected
        };

        await publisher.PublishRunCompletedEventAsync(
            new AgentRunRequest(), result, run: null, durationMs: 0, isStreaming: false, actualProvider: null);

        captured[0].Status.ShouldBe(AgentRunStatus.Failed.ToString());
        captured[0].FinishReason.ShouldBe(FinishReasons.GuardrailRejected);
    }

    [Fact]
    public async Task PublishRunFailedEventAsync_CapturesExceptionTypeAndMessage()
    {
        var captured = new List<AgentRunFailedEvent>();
        var eventBus = CreateEventBus<AgentRunFailedEvent>(captured);
        var publisher = new EventPublisher(eventBus.Object, Mock.Of<IServiceScopeFactory>(), Mock.Of<ILogger<EventPublisher>>());

        var ex = new InvalidOperationException("pipeline crash");

        await publisher.PublishRunFailedEventAsync(
            new AgentRunRequest { AgentId = Guid.NewGuid() }, run: null, ex, durationMs: 42, isStreaming: true);

        captured.Count.ShouldBe(1);
        captured[0].ErrorMessage.ShouldBe("pipeline crash");
        captured[0].ExceptionType.ShouldBe(nameof(InvalidOperationException));
        captured[0].IsStreaming.ShouldBeTrue();
        captured[0].DurationMs.ShouldBe(42);
    }

    [Fact]
    public async Task PublishRunFailedEventAsync_EventBusNull_DoesNotThrow()
    {
        var publisher = new EventPublisher(eventBus: null, Mock.Of<IServiceScopeFactory>(), Mock.Of<ILogger<EventPublisher>>());

        await publisher.PublishRunFailedEventAsync(
            new AgentRunRequest(), null, new InvalidOperationException("x"), 0, false);
    }

    [Fact]
    public async Task HandleNewThreadTitleAsync_ThreadIdMissing_ReturnsSilently()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var publisher = new EventPublisher(null, scopeFactory.Object, Mock.Of<ILogger<EventPublisher>>());

        await publisher.HandleNewThreadTitleAsync(
            new AgentRunRequest { ThreadId = null, UserMessage = "anything" },
            new AgentRunResult { Response = "hi" });

        scopeFactory.Verify(x => x.CreateScope(), Times.Never);
    }

    [Fact]
    public async Task HandleNewThreadTitleAsync_ExceptionInScopedWork_SwallowsAndLogs()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(x => x.CreateScope()).Throws(new InvalidOperationException("boom"));

        var publisher = new EventPublisher(null, scopeFactory.Object, Mock.Of<ILogger<EventPublisher>>());

        // Should not throw - the scoped work error is caught inside HandleNewThreadTitleAsync
        await publisher.HandleNewThreadTitleAsync(
            new AgentRunRequest { ThreadId = Guid.NewGuid(), UserMessage = "hi" },
            new AgentRunResult { Response = "hi back" });
    }

    private static Mock<IEventBus> CreateEventBus<TEvent>(List<TEvent> target)
        where TEvent : class, Tnzi.EventBus.IEvent
    {
        var bus = new Mock<IEventBus>();
        bus.Setup(x => x.PublishAsync(It.IsAny<TEvent>(), It.IsAny<CancellationToken>()))
            .Callback<TEvent, CancellationToken>((evt, _) => target.Add(evt))
            .Returns(Task.CompletedTask);
        return bus;
    }
}
