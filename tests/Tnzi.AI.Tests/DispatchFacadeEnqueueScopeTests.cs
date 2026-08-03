namespace Tnzi.AI.Tests;

/// <summary>
/// The facade must enqueue an external run in its own DI scope.
/// </summary>
/// <remarks>
/// Found by running the thing: with a host that has <c>EnableGlobalUnitOfWork</c> on,
/// the non-streaming path deadlocked. The queue row was written inside the request's
/// ambient transaction, which does not commit until the response is on its way out -
/// but the facade then waits for that very run to finish. The queue processor runs on
/// another connection and cannot see an uncommitted row, so the run never starts and
/// the request never returns. It timed out after three minutes and the rollback took
/// the row with it, leaving a chat that hung with nothing in the database to explain it.
/// <para>
/// Resolving the dispatcher from a fresh scope is what makes the insert commit on its
/// own. It is also the honest model: an external run lasts minutes to hours, so its
/// existence should not be decided by the transaction of the HTTP request that asked
/// for it.
/// </para>
/// </remarks>
public class DispatchFacadeEnqueueScopeTests
{
    private sealed class RecordingDispatcher : ICliAgentDispatcher
    {
        public int EnqueueCalls { get; private set; }

        public Task<Result<Guid>> EnqueueAsync(CliRunRequestDto request, CancellationToken cancellationToken = default)
        {
            EnqueueCalls++;
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

    /// <summary>The dispatcher used for the insert must come from a scope the facade opened.</summary>
    [Fact]
    public async Task Enqueue_ResolvesTheDispatcherFromAFreshScope_NotTheAmbientOne()
    {
        var ambient = new RecordingDispatcher();
        var scoped = new RecordingDispatcher();

        // Only the scope factory can hand out `scoped`; the facade's own injected
        // instance is `ambient`. Whichever one records the call tells us which
        // transaction the queue row would have been written in.
        var services = new ServiceCollection();
        services.AddScoped<ICliAgentDispatcher>(_ => scoped);

        var facade = new AgentDispatchFacade(
            new ThrowingRuntime(),
            new AlwaysBoundBindingService(),
            ambient,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new StubThreadService(),
            NullLogger<AgentDispatchFacade>.Instance);

        await facade.RunAsync(new AgentRunRequest { AgentId = Guid.NewGuid(), UserMessage = "x" });

        scoped.EnqueueCalls.ShouldBe(1, "the queue row must be written in its own scope so it commits immediately");
        ambient.EnqueueCalls.ShouldBe(0, "using the ambient dispatcher puts the insert inside the caller's transaction, which deadlocks under a global unit of work");
    }

    /// <summary>A thread always resolves; this test is about which scope enqueues.</summary>
    private sealed class StubThreadService : IAgentThreadInternalService
    {
        public Task<(ConversationContext context, Guid threadId, bool isNewThread)> GetOrCreateThreadAsync(
            Guid? threadId, Guid? agentId, CancellationToken ct = default)
            => Task.FromResult((new ConversationContext(), threadId ?? Guid.NewGuid(), threadId is null));

        public Task<Guid> SaveMessageAsync(Guid threadId, string role, string content,
            string? toolCalls = null, string? usage = null, Guid? messageId = null, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid());

        public Task<List<ChatMessage>> GetMessageHistoryAsync(Guid threadId, int? limit = null, CancellationToken ct = default)
            => Task.FromResult(new List<ChatMessage>());

        public Task SaveThreadSerializedDataAsync(Guid threadId, ConversationContext context, CancellationToken ct = default)
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
