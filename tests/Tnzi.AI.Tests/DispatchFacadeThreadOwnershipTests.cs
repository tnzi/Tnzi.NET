using Tnzi.Exceptions;

namespace Tnzi.AI.Tests;

/// <summary>
/// The facade must not let a caller-supplied ThreadId survive the thread service's refusal.
/// </summary>
/// <remarks>
/// <para>
/// <c>AgentThreadService.GetOrCreateThreadAsync</c> answers a foreign or unknown thread id with a
/// <see cref="BusinessException"/> (404). The facade used to catch every exception around that
/// call, log a warning and hand the <b>original</b> request id on to the CLI dispatcher: the
/// ownership refusal was swallowed and the run resumed the victim's CLI session inside the
/// victim's per-thread workspace, then wrote the reply into the victim's thread.
/// </para>
/// <para>
/// This is the same shape the built-in path closed in <c>HistoryMiddleware.EnsureThreadAsync</c>
/// (<c>catch (BusinessException) { throw; }</c>); the external path was written before that fix
/// and never picked it up.
/// </para>
/// </remarks>
public class DispatchFacadeThreadOwnershipTests
{
    private static readonly Guid ForeignThread = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    /// <summary>A refusal is a refusal: it propagates and nothing is enqueued.</summary>
    [Fact]
    public async Task RunAsync_WhenTheThreadServiceRefusesTheThread_PropagatesAndEnqueuesNothing()
    {
        var dispatcher = new RecordingDispatcher();
        var facade = CreateFacade(dispatcher, new RefusingThreadService());

        var request = new AgentRunRequest { AgentId = Guid.NewGuid(), ThreadId = ForeignThread, UserMessage = "x" };

        var ex = await Should.ThrowAsync<BusinessException>(() => facade.RunAsync(request));

        ex.HttpStatusCode.ShouldBe(404);
        dispatcher.EnqueueCalls.ShouldBe(0, "a run enqueued after the refusal would still resume the victim's session");
    }

    [Fact]
    public async Task RunStreamingAsync_WhenTheThreadServiceRefusesTheThread_PropagatesAndEnqueuesNothing()
    {
        var dispatcher = new RecordingDispatcher();
        var facade = CreateFacade(dispatcher, new RefusingThreadService());

        var request = new AgentRunRequest { AgentId = Guid.NewGuid(), ThreadId = ForeignThread, UserMessage = "x" };

        await Should.ThrowAsync<BusinessException>(async () =>
        {
            await foreach (var _ in facade.RunStreamingAsync(request))
            {
            }
        });

        dispatcher.EnqueueCalls.ShouldBe(0);
    }

    /// <summary>
    /// An infrastructure failure still degrades (the turn runs without continuity), but the
    /// unverified id must not be the thing that survives: the run is enqueued with no thread.
    /// </summary>
    [Fact]
    public async Task RunAsync_WhenTheThreadServiceFailsForInfrastructureReasons_EnqueuesWithoutAThread()
    {
        var dispatcher = new RecordingDispatcher();
        var facade = CreateFacade(dispatcher, new BrokenThreadService());

        var request = new AgentRunRequest { AgentId = Guid.NewGuid(), ThreadId = ForeignThread, UserMessage = "x" };

        var result = await facade.RunAsync(request);

        dispatcher.EnqueueCalls.ShouldBe(1);
        dispatcher.LastRequest!.ThreadId.ShouldBeNull("an id nobody verified must not reach the dispatcher");
        result.ThreadId.ShouldBeNull("nor be echoed back to the caller as if it had been resolved");
    }

    private static AgentDispatchFacade CreateFacade(RecordingDispatcher dispatcher, IAgentThreadInternalService threadService)
    {
        var services = new ServiceCollection();
        services.AddScoped<ICliAgentDispatcher>(_ => dispatcher);
        services.AddScoped(_ => threadService);

        return new AgentDispatchFacade(
            new ThrowingRuntime(),
            new AlwaysBoundBindingService(),
            dispatcher,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            threadService,
            NullLogger<AgentDispatchFacade>.Instance);
    }

    private sealed class RecordingDispatcher : ICliAgentDispatcher
    {
        public int EnqueueCalls { get; private set; }
        public CliRunRequestDto? LastRequest { get; private set; }

        public Task<Result<Guid>> EnqueueAsync(CliRunRequestDto request, CancellationToken cancellationToken = default)
        {
            EnqueueCalls++;
            LastRequest = request;
            return Task.FromResult(Result<Guid>.Failure("stop here", 500, "TEST_STOP"));
        }

        public IAsyncEnumerable<CliAgentEvent> StreamAsync(Guid runId, int fromSequence = 0, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<CliRunDto>> GetAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<IPagedList<CliRunDto>>> GetListAsync(CliRunQueryDto query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<List<CliRunMessageDto>>> GetMessagesAsync(Guid runId, int fromSequence = 0, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result> CancelAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class AlwaysBoundBindingService : ICliAgentBindingService
    {
        public Task<CliAgentBindingDto?> GetByAgentIdAsync(Guid agentId, CancellationToken cancellationToken = default)
            => Task.FromResult<CliAgentBindingDto?>(new CliAgentBindingDto
            {
                Id = Guid.NewGuid(),
                AgentId = agentId,
                CliRuntimeId = Guid.NewGuid()
            });

        public Task<Result<CliAgentBindingDto>> UpsertAsync(Guid agentId, UpsertCliAgentBindingDto input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result> DeleteAsync(Guid agentId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>Answers exactly like <c>AgentThreadService</c> does for a thread that is not yours.</summary>
    private sealed class RefusingThreadService : IAgentThreadInternalService
    {
        public Task<(ConversationContext context, Guid threadId, bool isNewThread)> GetOrCreateThreadAsync(
            Guid? threadId, Guid? agentId, CancellationToken ct = default)
            => throw new BusinessException("Thread not found", ErrorCodes.ThreadNotFound, 404);

        public Task<Guid> SaveMessageAsync(Guid threadId, string role, string content,
            string? toolCalls = null, string? usage = null, Guid? messageId = null, CancellationToken ct = default)
            => throw new InvalidOperationException("nothing may be written into a refused thread");

        public Task<List<ChatMessage>> GetMessageHistoryAsync(Guid threadId, int? limit = null, CancellationToken ct = default)
            => Task.FromResult(new List<ChatMessage>());

        // Every write path refuses, not only SaveMessageAsync: a fake that accepts a metadata
        // or serialized-state write would keep the ownership test green if the facade ever
        // persisted into the thread around the check.
        public Task SaveThreadSerializedDataAsync(Guid threadId, ConversationContext context, CancellationToken ct = default)
            => throw new InvalidOperationException("nothing may be written into a refused thread");

        public Task<string?> GetMetadataValueAsync(Guid threadId, string key, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task SetMetadataValueAsync(Guid threadId, string key, string? valueJson, CancellationToken ct = default)
            => throw new InvalidOperationException("nothing may be written into a refused thread");
    }

    /// <summary>The database is down: not a verdict on the thread, just no answer.</summary>
    private sealed class BrokenThreadService : IAgentThreadInternalService
    {
        public Task<(ConversationContext context, Guid threadId, bool isNewThread)> GetOrCreateThreadAsync(
            Guid? threadId, Guid? agentId, CancellationToken ct = default)
            => throw new InvalidOperationException("connection refused");

        public Task<Guid> SaveMessageAsync(Guid threadId, string role, string content,
            string? toolCalls = null, string? usage = null, Guid? messageId = null, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid());

        public Task<List<ChatMessage>> GetMessageHistoryAsync(Guid threadId, int? limit = null, CancellationToken ct = default)
            => Task.FromResult(new List<ChatMessage>());

        public Task SaveThreadSerializedDataAsync(Guid threadId, ConversationContext context, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<string?> GetMetadataValueAsync(Guid threadId, string key, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task SetMetadataValueAsync(Guid threadId, string key, string? valueJson, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class ThrowingRuntime : IAgentRuntime
    {
        public Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("built-in path must not be taken when a binding exists");

        public IAsyncEnumerable<AgentStreamChunk> RunStreamingAsync(AgentRunRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("built-in path must not be taken when a binding exists");

        public Task<AgentRunResult> ResumeAsync(Guid runId, ResumeRunInput? input = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
