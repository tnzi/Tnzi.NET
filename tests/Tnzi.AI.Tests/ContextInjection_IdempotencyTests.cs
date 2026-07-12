namespace Tnzi.AI.Tests;

/// <summary>
/// B6: ContextInjectionMiddleware — idempotency guard prevents duplicate soul/profile/citations
/// when RetryMiddleware re-invokes the inner pipeline on the same shared context.
/// </summary>
[Collection("ContextInjectionCache")]
public class ContextInjection_IdempotencyTests
{
    private readonly CompositeContextProviderFactory _providerFactory;

    public ContextInjection_IdempotencyTests()
    {
        ContextInjectionMiddleware.ClearAllCachesForTesting();

        var aiOptions = new StaticOptionsMonitor<AIOptions>(new AIOptions());
        _providerFactory = new CompositeContextProviderFactory(
            contributors: [],
            options: aiOptions,
            tokenEstimator: new HeuristicTokenEstimator(),
            loggerFactory: NullLoggerFactory.Instance,
            logger: NullLogger<CompositeContextProviderFactory>.Instance);
    }

    private ContextInjectionMiddleware CreateMiddleware()
        => new(_providerFactory, NullLogger<ContextInjectionMiddleware>.Instance);

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static AiMiddlewareContext CreateContextWithInlinePersona(string personaContent)
    {
        var request = new AgentRunRequest { AgentId = Guid.NewGuid(), UserMessage = "Hello" };
        var resolution = AgentResolution.Success(
            agent: new AgentExecutor(new Mock<IChatClient>().Object, new AgentExecutorOptions()),
            provider: "openai",
            model: "gpt-4",
            agentId: request.AgentId,
            personaContent: personaContent);

        return new AiMiddlewareContext
        {
            Request = request,
            Agent = resolution,
            Messages = [new ChatMessage(ChatRole.User, "Hello")],
            ServiceProvider = new ServiceCollection().BuildServiceProvider()
        };
    }

    private static AiMiddlewareContext CreateContextWithProfile(Guid userId, IUserProfileService profileService)
    {
        var request = new AgentRunRequest { AgentId = Guid.NewGuid(), UserMessage = "Hello", UserId = userId };
        var resolution = AgentResolution.Success(
            agent: new AgentExecutor(new Mock<IChatClient>().Object, new AgentExecutorOptions()),
            provider: "openai",
            model: "gpt-4",
            agentId: request.AgentId);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(profileService);
        var sp = services.BuildServiceProvider();

        return new AiMiddlewareContext
        {
            Request = request,
            Agent = resolution,
            Messages = [new ChatMessage(ChatRole.User, "Hello")],
            ServiceProvider = sp
        };
    }

    private static int CountOccurrences(List<ChatMessage> messages, string needle)
    {
        var count = 0;
        foreach (var msg in messages)
        {
            var text = msg.Text ?? string.Empty;
            var idx = 0;
            while ((idx = text.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += needle.Length;
            }
        }
        return count;
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    /// <summary>
    /// Simulates RetryMiddleware re-invoking the inner pipeline twice on the SAME context.
    /// The second call must be a no-op — exactly one &lt;soul&gt; block in messages.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_CalledTwiceOnSameContext_InjectsPersonaExactlyOnce()
    {
        var middleware = CreateMiddleware();
        var context = CreateContextWithInlinePersona("You are a brave knight.");

        // First invocation (would normally be the real first try)
        await middleware.InvokeAsync(context, (ctx, ct) =>
            Task.FromResult(new AgentRunResult { Response = "first-try" }), CancellationToken.None);

        // Second invocation on the SAME context (simulates RetryMiddleware re-executing the inner pipeline)
        await middleware.InvokeAsync(context, (ctx, ct) =>
            Task.FromResult(new AgentRunResult { Response = "retry" }), CancellationToken.None);

        // Exactly one <soul> tag in the combined messages — not two
        CountOccurrences(context.Messages, "<soul>").ShouldBe(1);
    }

    [Fact]
    public async Task InvokeAsync_CalledTwiceOnSameContext_InjectsUserProfileExactlyOnce()
    {
        var userId = Guid.NewGuid();
        var profileService = new Mock<IUserProfileService>();
        profileService.Setup(s => s.FindByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserProfile { Id = Guid.NewGuid(), UserId = userId, DisplayName = "Bob" });

        var middleware = CreateMiddleware();
        var context = CreateContextWithProfile(userId, profileService.Object);

        await middleware.InvokeAsync(context, (ctx, ct) =>
            Task.FromResult(new AgentRunResult { Response = "first" }), CancellationToken.None);

        await middleware.InvokeAsync(context, (ctx, ct) =>
            Task.FromResult(new AgentRunResult { Response = "retry" }), CancellationToken.None);

        // Exactly one <user_profile> block
        CountOccurrences(context.Messages, "<user_profile>").ShouldBe(1);
    }

    [Fact]
    public async Task InvokeAsync_FreshContext_StillInjectsNormally()
    {
        // Baseline: a brand-new context (never injected) must still get its soul block
        var middleware = CreateMiddleware();
        var context = CreateContextWithInlinePersona("You are a sage.");

        await middleware.InvokeAsync(context, (ctx, ct) =>
            Task.FromResult(new AgentRunResult { Response = "ok" }), CancellationToken.None);

        CountOccurrences(context.Messages, "<soul>").ShouldBe(1);
    }

    /// <summary>
    /// Streaming path: calling InvokeStreamingAsync twice on the same context
    /// must also yield exactly one soul block.
    /// </summary>
    [Fact]
    public async Task InvokeStreamingAsync_CalledTwiceOnSameContext_InjectsPersonaExactlyOnce()
    {
        var middleware = CreateMiddleware();
        var context = CreateContextWithInlinePersona("You are a streaming ninja.");

        static async IAsyncEnumerable<AgentStreamChunk> FakeNext(AiMiddlewareContext ctx, CancellationToken _)
        {
            yield return new AgentStreamChunk { Text = "chunk" };
        }

        // First call
        await foreach (var _ in middleware.InvokeStreamingAsync(context, FakeNext)) { }
        // Retry call on same context
        await foreach (var _ in middleware.InvokeStreamingAsync(context, FakeNext)) { }

        CountOccurrences(context.Messages, "<soul>").ShouldBe(1);
    }
}
