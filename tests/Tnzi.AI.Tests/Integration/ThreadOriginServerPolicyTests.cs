namespace Tnzi.AI.Tests.Integration;

/// <summary>
/// An application's <see cref="IThreadOriginPolicy"/> gives a new chat thread an origin only the server knows (a
/// conversation opened on a temporary pass device), replacing or standing in for what the page declared. The thread
/// is created with it: the row carries it from the insert on, while the turn is still running, so a list read at any
/// moment shows it and nothing writes it afterwards.
/// </summary>
/// <remarks>
/// Real <see cref="ChatService"/>, <see cref="AgentRuntime"/> with <see cref="ThreadResolutionMiddleware"/> and
/// <see cref="AgentThreadService"/> on SQLite; the policy is the test's stand-in for the application's.
/// </remarks>
public class ThreadOriginServerPolicyTests : AiIntegrationTestBase
{
    private readonly PassDevicePolicy _policy = new();
    private readonly List<string?> _seenDuringTheRun = [];

    protected override void ConfigureMiddlewares(IServiceCollection services)
    {
        services.AddScoped<AgentThreadService>();
        services.AddScoped<IAgentThreadService>(sp => sp.GetRequiredService<AgentThreadService>());
        services.AddScoped<IAgentThreadInternalService>(sp => sp.GetRequiredService<AgentThreadService>());
        services.AddScoped<IAiMiddleware, ThreadResolutionMiddleware>();
        services.AddScoped<IAiMiddleware>(_ => new OriginProbe(this));
    }

    protected override void ConfigureAdditionalServices(IServiceCollection services)
    {
        services.AddSingleton<IThreadOriginPolicy>(_policy);
        services.AddScoped<IAgentDispatchFacade, RuntimeOnlyFacade>();
        services.AddScoped<ChatService>();
    }

    [Fact]
    public async Task OnAPassDevice_TheServerGivesTheOrigin_WithNothingDeclared_AndTheRowHasItDuringTheTurn()
    {
        _policy.PassUserId = PassUser;
        MockProvider.EnqueueResponse("hi");

        var response = await Chat.ChatAsync(NewChat("hello", PassUser));

        response.Succeeded.ShouldBeTrue(response.Message);
        _seenDuringTheRun.ShouldBe(["Pass"]);
        (await StoredOriginAsync(response.Data!.ThreadId!.Value)).ShouldBe("Pass");
        (await Threads.GetListAsync(new ThreadListQueryDto())).Data!.Items.Single().Origin.ShouldBe("Pass");
        _policy.Requests.Single().ShouldSatisfyAllConditions(
            r => r.DeclaredOrigin.ShouldBeNull(),
            r => r.StartsThread.ShouldBeTrue(),
            r => r.UserId.ShouldBe(PassUser));
    }

    [Fact]
    public async Task OnAPassDevice_TheServersOriginReplacesWhatThePageDeclared()
    {
        _policy.PassUserId = PassUser;
        MockProvider.EnqueueResponse("hi");

        var response = await Chat.ChatAsync(NewChat("hello", PassUser, origin: "Card"));

        (await StoredOriginAsync(response.Data!.ThreadId!.Value)).ShouldBe("Pass");
        _policy.Requests.Single().DeclaredOrigin.ShouldBe("Card");
    }

    [Fact]
    public async Task ElsewhereThePolicyPassesTheDeclaredOriginThrough_AndNoneStaysNone()
    {
        MockProvider.EnqueueResponse("one");
        MockProvider.EnqueueResponse("two");

        var declared = await Chat.ChatAsync(NewChat("hello", Guid.NewGuid(), origin: "Card"));
        var plain = await Chat.ChatAsync(NewChat("hello", Guid.NewGuid()));

        (await StoredOriginAsync(declared.Data!.ThreadId!.Value)).ShouldBe("Card");
        (await StoredOriginAsync(plain.Data!.ThreadId!.Value)).ShouldBeNull();
    }

    [Fact]
    public async Task APolicyRefusal_FailsTheTurnBeforeAnyThreadIsCreated()
    {
        var response = await Chat.ChatAsync(NewChat("hello", Guid.NewGuid(), origin: "Email"));

        response.Succeeded.ShouldBeFalse();
        response.Code.ShouldBe(400);
        response.ErrorCode.ShouldBe(ErrorCodes.ThreadOriginNotAllowed);
        (await ThreadCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task AMalformedOriginFromThePolicy_IsAServerError_AndLeavesNoThread()
    {
        _policy.PassUserId = PassUser;
        _policy.PassOrigin = "pass device";

        var response = await Chat.ChatAsync(NewChat("hello", PassUser));

        response.Succeeded.ShouldBeFalse();
        response.Code.ShouldBe(500);
        response.ErrorCode.ShouldBe(ErrorCodes.InvalidThreadOrigin);
        (await ThreadCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task AMalformedDeclaredOrigin_IsRefusedBeforeThePolicyIsAsked()
    {
        var response = await Chat.ChatAsync(NewChat("hello", Guid.NewGuid(), origin: "not ok"));

        response.ErrorCode.ShouldBe(ErrorCodes.InvalidThreadOrigin);
        _policy.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task AContinuingMessage_DoesNotAskThePolicy_UnlessItDeclaresAnOrigin()
    {
        _policy.PassUserId = PassUser;
        MockProvider.EnqueueResponse("one");
        MockProvider.EnqueueResponse("two");
        MockProvider.EnqueueResponse("three");
        var first = await Chat.ChatAsync(NewChat("hello", PassUser));
        var threadId = first.Data!.ThreadId!.Value;

        await Chat.ChatAsync(NewChat("again", PassUser, threadId: threadId));
        _policy.Requests.Count.ShouldBe(1);

        var declared = await Chat.ChatAsync(NewChat("again", PassUser, origin: "Card", threadId: threadId));
        declared.Succeeded.ShouldBeTrue(declared.Message);
        _policy.Requests.Count.ShouldBe(2);
        _policy.Requests[1].StartsThread.ShouldBeFalse();
        (await StoredOriginAsync(threadId)).ShouldBe("Pass");
    }

    private static readonly Guid PassUser = Guid.NewGuid();

    private ChatService Chat => ServiceProvider.GetRequiredService<ChatService>();

    private AgentThreadService Threads => ServiceProvider.GetRequiredService<AgentThreadService>();

    private static ChatRequestDto NewChat(string message, Guid userId, string? origin = null, Guid? threadId = null)
        => new() { Message = message, Provider = "MockProvider", Model = "mock-model", UserId = userId, Origin = origin, ThreadId = threadId };

    private async Task<string?> StoredOriginAsync(Guid threadId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<AgentThread>().Where(t => t.Id == threadId).Select(t => t.Origin).SingleAsync();
    }

    private async Task<int> ThreadCountAsync()
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<AgentThread>().CountAsync();
    }

    /// <summary>
    /// The application's rule: a conversation opened on a temporary pass device is "Pass", whatever the page says;
    /// anywhere else only "Card" may be declared.
    /// </summary>
    private sealed class PassDevicePolicy : IThreadOriginPolicy
    {
        public Guid? PassUserId { get; set; }

        public string PassOrigin { get; set; } = "Pass";

        public List<ThreadOriginRequest> Requests { get; } = [];

        public Task<Result<string?>> ResolveAsync(ThreadOriginRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (request.UserId is { } user && user == PassUserId)
            {
                return Task.FromResult(Result<string?>.Success(PassOrigin));
            }

            return Task.FromResult(request.DeclaredOrigin switch
            {
                null => Result<string?>.Success(null),
                "Card" => Result<string?>.Success("Card"),
                var other => ConfiguredThreadOriginPolicy.NotAllowed(other)
            });
        }
    }

    /// <summary>Reads the thread's stored origin right after it is resolved, while the turn runs.</summary>
    private sealed class OriginProbe(ThreadOriginServerPolicyTests test) : IAiMiddleware
    {
        public int Order => AiMiddlewareOrders.ThreadResolution + 1;

        public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
        {
            if (context.IsNewThread && context.Request.ThreadId is { } threadId)
            {
                test._seenDuringTheRun.Add(await test.DbContext.Set<AgentThread>().AsNoTracking()
                    .Where(t => t.Id == threadId).Select(t => t.Origin).SingleAsync(cancellationToken));
            }

            return await next(context, cancellationToken);
        }
    }

    /// <summary>The facade's built-in path: every request goes to the runtime.</summary>
    private sealed class RuntimeOnlyFacade(IAgentRuntime runtime) : IAgentDispatchFacade
    {
        public Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken = default)
            => runtime.RunAsync(request, cancellationToken);

        public IAsyncEnumerable<AgentStreamChunk> RunStreamingAsync(AgentRunRequest request, CancellationToken cancellationToken = default)
            => runtime.RunStreamingAsync(request, cancellationToken);
    }
}
