
namespace Tnzi.AI.Tests.Events;

/// <summary>
/// SubAgentSpawnedEvent 发布行为测试
/// </summary>
public class SubAgentSpawnedEventTests
{
    private static IOptionsMonitor<SubAgentOptions> DefaultOptions()
    {
        return new ConstantSubAgentOptionsMonitor(new SubAgentOptions());
    }

    private static SubAgentExecutionService CreateService(
        IAgentResolver resolver,
        IRunStore runStore,
        ISubAgentRegistry? subAgentRegistry = null,
        IServiceProvider? serviceProvider = null,
        IEventBus? eventBus = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (eventBus != null)
            services.AddSingleton(eventBus);

        var provider = serviceProvider ?? services.BuildServiceProvider();

        return new SubAgentExecutionService(
            resolver,
            runStore,
            subAgentRegistry ?? Mock.Of<ISubAgentRegistry>(),
            new AgentExecutionContextAccessor(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SubAgentExecutionService>.Instance,
            provider,
            DefaultOptions());
    }

    private static IAgentResolver CreateResolver(Guid? agentId = null)
    {
        var resolver = new Mock<IAgentResolver>();
        resolver.Setup(x => x.ResolveAgentAsync(
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentResolution.Success(Mock.Of<IAgentExecutor>(), "openai", "gpt-5.4", agentId, null, AgentExecutionMode.Single));
        return resolver.Object;
    }

    private static IRunStore CreateRunStore(Guid runId, Guid? agentId = null, Guid? threadId = null)
    {
        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.CreateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRun run, CancellationToken _) =>
            {
                run.Id = runId;
                if (agentId.HasValue) run.AgentId = agentId;
                if (threadId.HasValue) run.ThreadId = threadId;
                return run;
            });
        runStore.Setup(x => x.CountDescendantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        runStore.Setup(x => x.GetParentRunIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);
        return runStore.Object;
    }

    [Fact]
    public async Task SpawnAsync_PublishesSubAgentSpawnedEvent_WithCorrectFields()
    {
        var agentId = Guid.NewGuid();
        var childRunId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        const string subAgentType = "researcher";
        SubAgentSpawnedEvent? capturedEvent = null;

        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(x => x.PublishAsync(It.IsAny<SubAgentSpawnedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<SubAgentSpawnedEvent, CancellationToken>((e, _) => capturedEvent = e)
            .Returns(Task.CompletedTask);

        var registry = new Mock<ISubAgentRegistry>();
        registry.Setup(x => x.Get(subAgentType))
            .Returns(new SubAgentTypeDefinition(
                Name: subAgentType,
                Description: "Research tasks",
                ToolGroups: ["web-search"],
                ExcludedToolGroups: [],
                MaxTurns: 10,
                DefaultModel: "gpt-5.4"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(eventBus.Object);
        var provider = services.BuildServiceProvider();

        var service = new SubAgentExecutionService(
            CreateResolver(agentId),
            CreateRunStore(childRunId, agentId, threadId),
            registry.Object,
            new AgentExecutionContextAccessor(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SubAgentExecutionService>.Instance,
            provider,
            DefaultOptions());

        var result = await service.SpawnAsync(new SpawnAgentRunInput
        {
            AgentId = agentId,
            ThreadId = threadId,
            Message = "do research",
            SubAgentType = subAgentType
        });

        result.Succeeded.ShouldBeTrue();

        eventBus.Verify(
            x => x.PublishAsync(It.IsAny<SubAgentSpawnedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);

        capturedEvent.ShouldNotBeNull();
        capturedEvent.ChildRunId.ShouldBe(childRunId);
        capturedEvent.AgentId.ShouldBe(agentId);
        capturedEvent.ThreadId.ShouldBe(threadId);
        capturedEvent.SubAgentType.ShouldBe(subAgentType);
        capturedEvent.ParentRunId.ShouldBeNull(); // no parent context in this test
    }

    [Fact]
    public async Task SpawnAsync_EventPublishingFailure_DoesNotAffectSpawnResult()
    {
        var childRunId = Guid.NewGuid();

        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(x => x.PublishAsync(It.IsAny<SubAgentSpawnedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Event bus unavailable"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(eventBus.Object);
        var provider = services.BuildServiceProvider();

        var service = CreateService(
            CreateResolver(),
            CreateRunStore(childRunId),
            serviceProvider: provider,
            eventBus: eventBus.Object);

        var result = await service.SpawnAsync(new SpawnAgentRunInput
        {
            Message = "background task"
        });

        // spawn should succeed regardless of event bus failure
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.RunId.ShouldBe(childRunId);
    }

    [Fact]
    public async Task SpawnAsync_WithParentRunId_PublishesCorrectParentRunId()
    {
        var parentRunId = Guid.NewGuid();
        var childRunId = Guid.NewGuid();
        SubAgentSpawnedEvent? capturedEvent = null;

        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(x => x.PublishAsync(It.IsAny<SubAgentSpawnedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<SubAgentSpawnedEvent, CancellationToken>((e, _) => capturedEvent = e)
            .Returns(Task.CompletedTask);

        // simulate a parent run context
        var executionContext = new AgentExecutionContextAccessor();
        executionContext.Properties[ContextPropertyKeys.CurrentRunId] = parentRunId;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(eventBus.Object);
        var provider = services.BuildServiceProvider();

        var service = new SubAgentExecutionService(
            CreateResolver(),
            CreateRunStore(childRunId),
            Mock.Of<ISubAgentRegistry>(),
            executionContext,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SubAgentExecutionService>.Instance,
            provider,
            DefaultOptions());

        var result = await service.SpawnAsync(new SpawnAgentRunInput
        {
            Message = "child task"
        });

        result.Succeeded.ShouldBeTrue();

        capturedEvent.ShouldNotBeNull();
        capturedEvent.ChildRunId.ShouldBe(childRunId);
        capturedEvent.ParentRunId.ShouldBe(parentRunId);
    }

    private sealed class ConstantSubAgentOptionsMonitor(SubAgentOptions value) : IOptionsMonitor<SubAgentOptions>
    {
        public SubAgentOptions CurrentValue => value;
        public SubAgentOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<SubAgentOptions, string?> listener) => null;
    }
}
