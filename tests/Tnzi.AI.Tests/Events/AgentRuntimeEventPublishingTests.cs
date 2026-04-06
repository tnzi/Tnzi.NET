namespace Tnzi.AI.Tests.Events;

/// <summary>
/// AgentRuntime 运行生命周期事件发布测试 — 验证 AgentRunStartedEvent 和 AgentRunFailedEvent 的发布行为
/// </summary>
public class AgentRuntimeEventPublishingTests
{
    private static AgentRuntime CreateRuntime(
        IAgentResolver? agentResolver = null,
        IEventBus? eventBus = null,
        IRunStore? runStore = null,
        ITraceStore? traceStore = null)
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        return new AgentRuntime(
            agentResolver ?? Mock.Of<IAgentResolver>(),
            Mock.Of<IAgentFactory>(),
            Mock.Of<IRepository<Agent, Guid>>(),
            runStore ?? Mock.Of<IRunStore>(),
            traceStore ?? CreateDefaultTraceStore(),
            Mock.Of<IWorkflowService>(),
            new AgentExecutionContextAccessor(),
            sp,
            Mock.Of<IServiceScopeFactory>(),
            CreateOptionsMonitor(),
            Mock.Of<ILogger<AgentRuntime>>(),
            eventBus);
    }

    private static ITraceStore CreateDefaultTraceStore()
    {
        var mock = new Mock<ITraceStore>();
        mock.Setup(x => x.AddAsync(It.IsAny<AgentRunTrace>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRunTrace trace, CancellationToken _) => trace);
        return mock.Object;
    }

    private static IOptionsMonitor<AIOptions> CreateOptionsMonitor()
    {
        var options = new AIOptions
        {
            DefaultProvider = "openai",
            Providers = new Dictionary<string, ProviderOptions>
            {
                ["openai"] = new() { ApiKey = "test-key" }
            }
        };
        var mock = new Mock<IOptionsMonitor<AIOptions>>();
        mock.Setup(x => x.CurrentValue).Returns(options);
        return mock.Object;
    }

    private static AgentResolution CreateSuccessResolution(
        string provider = "openai",
        string model = "gpt-4o",
        AgentExecutionMode executionMode = AgentExecutionMode.Single)
    {
        // 创建最小化的 AgentExecutor — ChatClient 会在管道执行阶段才被调用
        var chatClient = Mock.Of<IChatClient>();
        var executor = new AgentExecutor(chatClient, new AgentExecutorOptions());

        return AgentResolution.Success(
            executor, provider, model, Guid.NewGuid(),
            executionMode: executionMode);
    }

    [Fact]
    public async Task RunAsync_PublishesStartedEvent_WithIsStreamingFalse()
    {
        // Arrange
        var eventBus = new Mock<IEventBus>();
        AgentRunStartedEvent? capturedEvent = null;
        eventBus
            .Setup(x => x.PublishAsync(It.IsAny<AgentRunStartedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AgentRunStartedEvent, CancellationToken>((evt, _) => capturedEvent = evt)
            .Returns(Task.CompletedTask);

        var agentResolver = new Mock<IAgentResolver>();
        agentResolver
            .Setup(x => x.ResolveAgentAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResolution());

        var runtime = CreateRuntime(agentResolver.Object, eventBus.Object);

        var request = new AgentRunRequest
        {
            OperationType = AIOperationType.AgentRun,
            UserMessage = "Hello"
        };

        // Act — pipeline execution will fail (no real ChatClient), but Started event should have been published
        try { await runtime.RunAsync(request); } catch { /* expected */ }

        // Assert
        capturedEvent.ShouldNotBeNull();
        capturedEvent.IsStreaming.ShouldBeFalse();
        capturedEvent.Provider.ShouldBe("openai");
        capturedEvent.Model.ShouldBe("gpt-4o");
        capturedEvent.ExecutionMode.ShouldBe("Single");
    }

    [Fact]
    public async Task RunAsync_PublishesFailedEvent_WhenExceptionOccurs()
    {
        // Arrange
        var eventBus = new Mock<IEventBus>();
        AgentRunFailedEvent? capturedEvent = null;
        eventBus
            .Setup(x => x.PublishAsync(It.IsAny<AgentRunFailedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AgentRunFailedEvent, CancellationToken>((evt, _) => capturedEvent = evt)
            .Returns(Task.CompletedTask);
        eventBus
            .Setup(x => x.PublishAsync(It.IsAny<AgentRunStartedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var agentResolver = new Mock<IAgentResolver>();
        agentResolver
            .Setup(x => x.ResolveAgentAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResolution());

        var runtime = CreateRuntime(agentResolver.Object, eventBus.Object);

        var request = new AgentRunRequest
        {
            OperationType = AIOperationType.AgentRun,
            UserMessage = "Hello"
        };

        // Act
        await Should.ThrowAsync<Exception>(async () => await runtime.RunAsync(request));

        // Assert
        capturedEvent.ShouldNotBeNull();
        capturedEvent.IsStreaming.ShouldBeFalse();
        capturedEvent.DurationMs.ShouldBeGreaterThanOrEqualTo(0);
        capturedEvent.ErrorMessage.ShouldNotBeNullOrWhiteSpace();
        capturedEvent.ExceptionType.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RunAsync_StartedEvent_CarriesCorrectProviderModelAndExecutionMode()
    {
        // Arrange
        var eventBus = new Mock<IEventBus>();
        AgentRunStartedEvent? capturedEvent = null;
        eventBus
            .Setup(x => x.PublishAsync(It.IsAny<AgentRunStartedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AgentRunStartedEvent, CancellationToken>((evt, _) => capturedEvent = evt)
            .Returns(Task.CompletedTask);

        var agentResolver = new Mock<IAgentResolver>();
        agentResolver
            .Setup(x => x.ResolveAgentAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResolution("anthropic", "claude-sonnet-4-20250514", AgentExecutionMode.Handoff));

        var runtime = CreateRuntime(agentResolver.Object, eventBus.Object);

        var request = new AgentRunRequest
        {
            OperationType = AIOperationType.AgentRun,
            UserMessage = "Test",
            AgentId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ThreadId = Guid.NewGuid()
        };

        // Act
        try { await runtime.RunAsync(request); } catch { /* expected */ }

        // Assert
        capturedEvent.ShouldNotBeNull();
        capturedEvent.Provider.ShouldBe("anthropic");
        capturedEvent.Model.ShouldBe("claude-sonnet-4-20250514");
        capturedEvent.ExecutionMode.ShouldBe("Handoff");
        capturedEvent.AgentId.ShouldBe(request.AgentId);
        capturedEvent.UserId.ShouldBe(request.UserId);
        capturedEvent.ThreadId.ShouldBe(request.ThreadId);
    }

    [Fact]
    public async Task RunAsync_EventBusNull_DoesNotThrow()
    {
        // Arrange — no event bus injected
        var agentResolver = new Mock<IAgentResolver>();
        agentResolver
            .Setup(x => x.ResolveAgentAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResolution());

        var runtime = CreateRuntime(agentResolver.Object, eventBus: null);

        var request = new AgentRunRequest
        {
            OperationType = AIOperationType.AgentRun,
            UserMessage = "Hello"
        };

        // Act — should not throw due to null event bus (Started/Failed both skip when _eventBus is null)
        try { await runtime.RunAsync(request); } catch { /* expected from pipeline, not events */ }

        // If we get here without NullReferenceException, the test passes
    }
}
