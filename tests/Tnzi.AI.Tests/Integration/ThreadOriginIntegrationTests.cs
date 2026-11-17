using Tnzi.Exceptions;

namespace Tnzi.AI.Tests.Integration;

/// <summary>
/// A thread carries the origin its creator declared: a chat message that starts a new thread (only an origin the
/// application allows clients to declare), server code creating a thread, and an in-process run; the thread DTOs and
/// the list return it as stored.
/// </summary>
/// <remarks>
/// Real <see cref="ChatService"/>, <see cref="AgentRuntime"/> with <see cref="ThreadResolutionMiddleware"/> and
/// <see cref="AgentThreadService"/> on SQLite. The dispatch facade passes straight to the runtime (no agent here is
/// bound to an external CLI; that path has its own tests).
/// </remarks>
public class ThreadOriginIntegrationTests : AiIntegrationTestBase
{
    private readonly AiChatOptions _chatOptions = new() { AllowedThreadOrigins = ["Card", "Email"] };

    protected override void ConfigureMiddlewares(IServiceCollection services)
    {
        services.AddScoped<AgentThreadService>();
        services.AddScoped<IAgentThreadService>(sp => sp.GetRequiredService<AgentThreadService>());
        services.AddScoped<IAgentThreadInternalService>(sp => sp.GetRequiredService<AgentThreadService>());
        services.AddScoped<IAiMiddleware, ThreadResolutionMiddleware>();
    }

    protected override void ConfigureAdditionalServices(IServiceCollection services)
    {
        var chatOptions = new Mock<IOptionsMonitor<AiChatOptions>>();
        chatOptions.Setup(m => m.CurrentValue).Returns(_chatOptions);
        services.AddSingleton(chatOptions.Object);
        services.AddScoped<IThreadOriginPolicy, ConfiguredThreadOriginPolicy>();
        services.AddScoped<IAgentDispatchFacade, RuntimeOnlyFacade>();
        services.AddScoped<ChatService>();
    }

    [Fact]
    public async Task ChatMessage_StartingAThread_WithAnAllowedOrigin_StoresIt_AndTheListDetailAndByIdReturnIt()
    {
        MockProvider.EnqueueResponse("Hello");

        var response = await Chat.ChatAsync(NewChat("hi", origin: "Card"));

        response.Succeeded.ShouldBeTrue(response.Message);
        var threadId = response.Data!.ThreadId!.Value;
        (await StoredOriginAsync(threadId)).ShouldBe("Card");

        var list = (await Threads.GetListAsync(new ThreadListQueryDto())).Data!;
        list.Items.Single(t => t.Id == threadId).Origin.ShouldBe("Card");
        (await Threads.GetByIdAsync(threadId)).Data!.Origin.ShouldBe("Card");
        (await Threads.GetDetailAsync(threadId)).Data!.Origin.ShouldBe("Card");
    }

    [Fact]
    public async Task ChatMessage_WithAnOriginTheApplicationDoesNotAllow_IsRefusedWith400_AndLeavesNoThread()
    {
        var response = await Chat.ChatAsync(NewChat("hi", origin: "Schedule"));

        response.Succeeded.ShouldBeFalse();
        response.Code.ShouldBe(400);
        response.ErrorCode.ShouldBe(ErrorCodes.ThreadOriginNotAllowed);
        (await ThreadCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task ChatMessage_WithAMalformedOrigin_IsRefusedWith400_EvenWhenTheListWouldAllowAnything()
    {
        var response = await Chat.ChatAsync(NewChat("hi", origin: "1 card"));

        response.Succeeded.ShouldBeFalse();
        response.Code.ShouldBe(400);
        response.ErrorCode.ShouldBe(ErrorCodes.InvalidThreadOrigin);
        (await ThreadCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task OriginsAreCompared_Ordinally()
    {
        var response = await Chat.ChatAsync(NewChat("hi", origin: "card"));

        response.ErrorCode.ShouldBe(ErrorCodes.ThreadOriginNotAllowed);
    }

    [Fact]
    public async Task ChatMessage_WithoutAnOrigin_LeavesItNull_AndAContinuingMessageDoesNotChangeIt()
    {
        MockProvider.EnqueueResponse("one");
        MockProvider.EnqueueResponse("two");

        var first = await Chat.ChatAsync(NewChat("hi"));
        var threadId = first.Data!.ThreadId!.Value;
        (await StoredOriginAsync(threadId)).ShouldBeNull();

        var second = await Chat.ChatAsync(new ChatRequestDto { Message = "again", ThreadId = threadId, Provider = "MockProvider", Model = "mock-model", Origin = "Card" });

        second.Succeeded.ShouldBeTrue(second.Message);
        second.Data!.ThreadId.ShouldBe(threadId);
        (await StoredOriginAsync(threadId)).ShouldBeNull();
    }

    [Fact]
    public async Task ServerCode_CreatingAThread_StoresAnyWellFormedOrigin_WithoutTheClientList()
    {
        var created = await Threads.CreateAsync(new CreateAgentThreadDto { Title = "From a rule", Origin = "Webhook" });

        created.Succeeded.ShouldBeTrue(created.Message);
        created.Data!.Origin.ShouldBe("Webhook");
        (await StoredOriginAsync(created.Data.Id)).ShouldBe("Webhook");
        (await Threads.GetListAsync(new ThreadListQueryDto())).Data!.Items.Single().Origin.ShouldBe("Webhook");
    }

    [Fact]
    public async Task ServerCode_CreatingAThread_WithAMalformedOrigin_Fails_AndAnEmptyOneMeansNone()
    {
        var bad = await Threads.CreateAsync(new CreateAgentThreadDto { Origin = "has space" });
        bad.Succeeded.ShouldBeFalse();
        bad.Code.ShouldBe(400);
        bad.ErrorCode.ShouldBe(ErrorCodes.InvalidThreadOrigin);

        var empty = await Threads.CreateAsync(new CreateAgentThreadDto { Origin = "" });
        empty.Data!.Origin.ShouldBeNull();
        (await ThreadCountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task InProcessRun_StartingAThread_StoresTheRequestsThreadOrigin()
    {
        MockProvider.EnqueueResponse("done");

        var result = await Runtime.RunAsync(new AgentRunRequest
        {
            UserMessage = "daily digest",
            Provider = "MockProvider",
            Model = "mock-model",
            ThreadOrigin = "Schedule"
        });

        (await StoredOriginAsync(result.ThreadId!.Value)).ShouldBe("Schedule");
    }

    [Fact]
    public async Task InProcessRun_WithAMalformedThreadOrigin_IsRefused_AndLeavesNoThread()
    {
        await Should.ThrowAsync<BusinessException>(() => Runtime.RunAsync(new AgentRunRequest
        {
            UserMessage = "x",
            Provider = "MockProvider",
            Model = "mock-model",
            ThreadOrigin = "-dash"
        }));

        (await ThreadCountAsync()).ShouldBe(0);
    }

    private ChatService Chat => ServiceProvider.GetRequiredService<ChatService>();

    private AgentThreadService Threads => ServiceProvider.GetRequiredService<AgentThreadService>();

    private static ChatRequestDto NewChat(string message, string? origin = null)
        => new() { Message = message, Provider = "MockProvider", Model = "mock-model", Origin = origin };

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

    /// <summary>The facade's built-in path: every request goes to the runtime.</summary>
    private sealed class RuntimeOnlyFacade(IAgentRuntime runtime) : IAgentDispatchFacade
    {
        public Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken = default)
            => runtime.RunAsync(request, cancellationToken);

        public IAsyncEnumerable<AgentStreamChunk> RunStreamingAsync(AgentRunRequest request, CancellationToken cancellationToken = default)
            => runtime.RunStreamingAsync(request, cancellationToken);
    }
}
