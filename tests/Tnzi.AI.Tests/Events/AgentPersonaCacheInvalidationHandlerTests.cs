namespace Tnzi.AI.Tests.Events;

/// <summary>
/// AgentPersonaCacheInvalidationHandler — verifies that AgentPersonaUpdatedEvent /
/// AgentPersonaDeletedEvent properly trigger ContextInjectionMiddleware static cache
/// eviction so admin edits take effect immediately rather than at the 5-minute TTL
/// boundary.
/// </summary>
/// <remarks>
/// Same xUnit Collection as ContextInjection_PersonaTests so the shared static
/// cache cannot be wiped by a parallel sibling test.
/// </remarks>
[Collection("ContextInjectionCache")]
public class AgentPersonaCacheInvalidationHandlerTests
{
    private readonly AgentPersonaCacheInvalidationHandler _handler;

    public AgentPersonaCacheInvalidationHandlerTests()
    {
        // Clean static cache between tests so observations are isolated.
        ContextInjectionMiddleware.ClearAllCachesForTesting();
        _handler = new AgentPersonaCacheInvalidationHandler(NullLogger<AgentPersonaCacheInvalidationHandler>.Instance);
    }

    [Fact]
    public async Task HandleAsync_UpdatedEvent_EvictsCachedPersonaContent()
    {
        // Arrange — populate the middleware's cache for a persona
        var personaId = Guid.NewGuid();
        var personaService = new Mock<IAgentPersonaService>();
        int dbCallCount = 0;
        personaService.Setup(s => s.GetByIdAsync(personaId, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Interlocked.Increment(ref dbCallCount);
                return Task.FromResult(Result<AgentPersonaDto>.Success(new AgentPersonaDto
                {
                    Id = personaId,
                    Content = $"content-call-{dbCallCount}"
                }));
            });

        var sp = new ServiceCollection()
            .AddLogging()
            .AddSingleton(personaService.Object)
            .BuildServiceProvider();

        var middleware = new ContextInjectionMiddleware(
            new CompositeContextProviderFactory(
                contributors: [],
                options: Microsoft.Extensions.Options.Options.Create(new AIOptions()),
                tokenEstimator: new HeuristicTokenEstimator(),
                loggerFactory: NullLoggerFactory.Instance,
                logger: NullLogger<CompositeContextProviderFactory>.Instance),
            NullLogger<ContextInjectionMiddleware>.Instance);

        // 1st run — populates cache, 1 DB call
        await RunAsync(middleware, sp, personaId);
        // 2nd run — cache hit, still 1 DB call
        await RunAsync(middleware, sp, personaId);
        Assert.Equal(1, dbCallCount);

        // Act — fire the Updated event
        await _handler.HandleAsync(new AgentPersonaUpdatedEvent
        {
            PersonaId = personaId,
            Slug = "test-persona"
        });

        // Assert — next run forces fresh DB lookup
        await RunAsync(middleware, sp, personaId);
        Assert.Equal(2, dbCallCount);
    }

    [Fact]
    public async Task HandleAsync_DeletedEvent_EvictsCachedPersonaContent()
    {
        var personaId = Guid.NewGuid();
        var personaService = new Mock<IAgentPersonaService>();
        int dbCallCount = 0;
        personaService.Setup(s => s.GetByIdAsync(personaId, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Interlocked.Increment(ref dbCallCount);
                return Task.FromResult(Result<AgentPersonaDto>.Success(new AgentPersonaDto
                {
                    Id = personaId, Content = "still-here"
                }));
            });

        var sp = new ServiceCollection()
            .AddLogging()
            .AddSingleton(personaService.Object)
            .BuildServiceProvider();

        var middleware = new ContextInjectionMiddleware(
            new CompositeContextProviderFactory(
                contributors: [],
                options: Microsoft.Extensions.Options.Options.Create(new AIOptions()),
                tokenEstimator: new HeuristicTokenEstimator(),
                loggerFactory: NullLoggerFactory.Instance,
                logger: NullLogger<CompositeContextProviderFactory>.Instance),
            NullLogger<ContextInjectionMiddleware>.Instance);

        await RunAsync(middleware, sp, personaId);
        Assert.Equal(1, dbCallCount);

        await _handler.HandleAsync(new AgentPersonaDeletedEvent { PersonaId = personaId });

        await RunAsync(middleware, sp, personaId);
        Assert.Equal(2, dbCallCount);
    }

    /// <summary>
    /// Invalidating one persona must not affect other personas' cached entries.
    /// </summary>
    [Fact]
    public async Task HandleAsync_InvalidationIsScopedToSinglePersona()
    {
        var personaA = Guid.NewGuid();
        var personaB = Guid.NewGuid();
        var personaService = new Mock<IAgentPersonaService>();
        var callsByPersona = new ConcurrentDictionary<Guid, int>();
        personaService.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid id, CancellationToken _) =>
            {
                callsByPersona.AddOrUpdate(id, 1, (_, v) => v + 1);
                return Task.FromResult(Result<AgentPersonaDto>.Success(new AgentPersonaDto
                {
                    Id = id, Content = $"content-for-{id}"
                }));
            });

        var sp = new ServiceCollection()
            .AddLogging()
            .AddSingleton(personaService.Object)
            .BuildServiceProvider();

        var middleware = new ContextInjectionMiddleware(
            new CompositeContextProviderFactory(
                contributors: [],
                options: Microsoft.Extensions.Options.Options.Create(new AIOptions()),
                tokenEstimator: new HeuristicTokenEstimator(),
                loggerFactory: NullLoggerFactory.Instance,
                logger: NullLogger<CompositeContextProviderFactory>.Instance),
            NullLogger<ContextInjectionMiddleware>.Instance);

        // Populate cache for both personas
        await RunAsync(middleware, sp, personaA);
        await RunAsync(middleware, sp, personaB);
        Assert.Equal(1, callsByPersona[personaA]);
        Assert.Equal(1, callsByPersona[personaB]);

        // Invalidate only A
        await _handler.HandleAsync(new AgentPersonaUpdatedEvent { PersonaId = personaA });

        // A re-fetches; B still cached
        await RunAsync(middleware, sp, personaA);
        await RunAsync(middleware, sp, personaB);
        Assert.Equal(2, callsByPersona[personaA]);
        Assert.Equal(1, callsByPersona[personaB]);
    }

    private static async Task RunAsync(ContextInjectionMiddleware middleware, IServiceProvider sp, Guid personaId)
    {
        var ctx = new AiMiddlewareContext
        {
            Request = new AgentRunRequest { AgentId = Guid.NewGuid(), UserMessage = "hi" },
            Agent = AgentResolution.Success(
                agent: new AgentExecutor(new Mock<IChatClient>().Object, new AgentExecutorOptions()),
                provider: "openai", model: "gpt-4", agentId: Guid.NewGuid(),
                personaId: personaId),
            Messages = [new ChatMessage(ChatRole.User, "hi")],
            ServiceProvider = sp
        };
        await middleware.InvokeAsync(ctx, (c, t) =>
            Task.FromResult(new AgentRunResult { Response = "ok" }), CancellationToken.None);
    }
}
