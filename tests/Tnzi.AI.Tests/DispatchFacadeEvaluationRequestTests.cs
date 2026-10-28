namespace Tnzi.AI.Tests;

/// <summary>
/// What an evaluation asks of a CLI-bound agent: an ephemeral run leaves no thread behind, and a pinned agent
/// version, which the external runtime cannot run, is refused instead of silently running the live definition.
/// </summary>
/// <remarks>
/// The evaluator goes through the dispatch facade with <c>Ephemeral = true</c> and the case's version number.
/// The built-in path honours both; the external path ignored them: every case opened a thread under the
/// evaluator with the question and the answer in it, and the score was recorded against a version that never ran.
/// </remarks>
public class DispatchFacadeEvaluationRequestTests
{
    [Fact]
    public async Task RunAsync_EphemeralExternalRun_CreatesNoThreadAndWritesNoMessages()
    {
        var threads = new RecordingThreadService();
        var dispatcher = new RecordingDispatcher("answer");
        var facade = CreateFacade(dispatcher, threads);

        var result = await facade.RunAsync(new AgentRunRequest { AgentId = Guid.NewGuid(), UserMessage = "case input", Ephemeral = true });

        result.Status.ShouldBe(AgentRunStatus.Completed);
        result.Response.ShouldBe("answer");
        threads.ThreadsResolved.ShouldBe(0, "an ephemeral run does not create a thread");
        threads.Saved.ShouldBeEmpty("neither the question nor the answer is written anywhere");
        dispatcher.Enqueued.ShouldHaveSingleItem().ThreadId.ShouldBeNull();
        result.ThreadId.ShouldBeNull();
    }

    [Fact]
    public async Task RunStreamingAsync_EphemeralExternalRun_CreatesNoThreadAndWritesNoMessages()
    {
        var threads = new RecordingThreadService();
        var dispatcher = new RecordingDispatcher("answer");
        var facade = CreateFacade(dispatcher, threads);

        await foreach (var _ in facade.RunStreamingAsync(new AgentRunRequest { AgentId = Guid.NewGuid(), UserMessage = "case input", Ephemeral = true }))
        {
        }

        threads.ThreadsResolved.ShouldBe(0);
        threads.Saved.ShouldBeEmpty();
        dispatcher.Enqueued.ShouldHaveSingleItem().ThreadId.ShouldBeNull();
    }

    [Fact]
    public async Task RunAsync_NonEphemeralExternalRun_StillKeepsTheConversation()
    {
        var threads = new RecordingThreadService();
        var facade = CreateFacade(new RecordingDispatcher("answer"), threads);

        await facade.RunAsync(new AgentRunRequest { AgentId = Guid.NewGuid(), UserMessage = "hi" });

        threads.ThreadsResolved.ShouldBe(1);
        // The reply is written by the run itself when it ends (Tnzi.AI.Cli), not by whoever is waiting on it.
        threads.Saved.Select(m => m.Role).ShouldBe(["user"]);
    }

    [Fact]
    public async Task RunAsync_PinnedAgentVersion_IsRefusedBeforeAnythingIsEnqueued()
    {
        var threads = new RecordingThreadService();
        var dispatcher = new RecordingDispatcher("answer");
        var facade = CreateFacade(dispatcher, threads);

        var result = await facade.RunAsync(new AgentRunRequest
        {
            AgentId = Guid.NewGuid(),
            UserMessage = "case input",
            AgentVersionNumber = 3,
            Ephemeral = true
        });

        result.Status.ShouldBe(AgentRunStatus.Failed);
        result.FinishReason.ShouldBe(FinishReasons.Error);
        result.Response.ShouldContain("pinned agent version");
        dispatcher.Enqueued.ShouldBeEmpty("the live definition must not run in place of the pinned version");
        threads.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunStreamingAsync_PinnedAgentVersion_EndsWithAnErrorChunk()
    {
        var dispatcher = new RecordingDispatcher("answer");
        var facade = CreateFacade(dispatcher, new RecordingThreadService());

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in facade.RunStreamingAsync(new AgentRunRequest { AgentId = Guid.NewGuid(), UserMessage = "x", AgentVersionNumber = 2 }))
        {
            chunks.Add(chunk);
        }

        var only = chunks.ShouldHaveSingleItem();
        only.FinishReason.ShouldBe(FinishReasons.Error);
        only.Error.ShouldNotBeNullOrWhiteSpace();
        dispatcher.Enqueued.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_ModelOverride_StillRunsOnTheBindingsModel()
    {
        // Chat clients send the selected model along with the agent; the binding decides the model of an
        // external run, and refusing the override would fail every such chat.
        var dispatcher = new RecordingDispatcher("answer");
        var facade = CreateFacade(dispatcher, new RecordingThreadService());

        var result = await facade.RunAsync(new AgentRunRequest { AgentId = Guid.NewGuid(), UserMessage = "hi", Provider = "openai", Model = "gpt-x" });

        result.Status.ShouldBe(AgentRunStatus.Completed);
        dispatcher.Enqueued.ShouldHaveSingleItem();
    }

    private static AgentDispatchFacade CreateFacade(ICliAgentDispatcher dispatcher, IAgentThreadInternalService threads)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => dispatcher);
        services.AddScoped(_ => threads);

        return new AgentDispatchFacade(
            Mock.Of<IAgentRuntime>(),
            new BoundBindingService(),
            dispatcher,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentDispatchFacade>.Instance);
    }

    private sealed record SavedMessage(Guid ThreadId, string Role, string Content);

    private sealed class RecordingThreadService : IAgentThreadInternalService
    {
        public int ThreadsResolved { get; private set; }

        public List<SavedMessage> Saved { get; } = [];

        public Task<(Guid threadId, bool isNewThread)> GetOrCreateThreadAsync(
            Guid? threadId, Guid? agentId, CancellationToken ct = default)
        {
            ThreadsResolved++;
            return Task.FromResult((threadId ?? Guid.NewGuid(), threadId is null));
        }

        public Task<Guid> SaveMessageAsync(Guid threadId, string role, string content,
            string? toolCalls = null, string? usage = null, Guid? messageId = null, CancellationToken ct = default)
        {
            Saved.Add(new SavedMessage(threadId, role, content));
            return Task.FromResult(messageId ?? Guid.NewGuid());
        }

        public Task<List<ChatMessage>> GetMessageHistoryAsync(Guid threadId, int? limit = null, CancellationToken ct = default)
            => Task.FromResult(new List<ChatMessage>());

        public Task<string?> GetMetadataValueAsync(Guid threadId, string key, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task SetMetadataValueAsync(Guid threadId, string key, string? valueJson, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    /// <summary>Records each enqueued request; every run completes with <c>output</c>.</summary>
    private sealed class RecordingDispatcher(string output) : ICliAgentDispatcher
    {
        private readonly Dictionary<Guid, CliRunRequestDto> _runs = [];

        public List<CliRunRequestDto> Enqueued { get; } = [];

        public Task<Result<Guid>> EnqueueAsync(CliRunRequestDto request, CancellationToken cancellationToken = default)
        {
            var runId = Guid.NewGuid();
            Enqueued.Add(request);
            _runs[runId] = request;
            return Task.FromResult(Result<Guid>.Success(runId));
        }

        public async IAsyncEnumerable<CliAgentEvent> StreamAsync(Guid runId, int fromSequence = 0,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new CliAgentEvent { Type = CliAgentEventType.Text, Content = output };
            await Task.Yield();
        }

        public Task<Result<CliRunDto>> GetAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<CliRunDto>.Success(new CliRunDto
            {
                Id = runId,
                ThreadId = _runs[runId].ThreadId,
                Status = CliRunStatus.Completed,
                Output = output
            }));

        public Task<Result<IPagedList<CliRunDto>>> GetListAsync(CliRunQueryDto query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<List<CliRunMessageDto>>> GetMessagesAsync(Guid runId, int fromSequence = 0, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result> CancelAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class BoundBindingService : ICliAgentBindingService
    {
        public Task<CliAgentBindingDto?> GetByAgentIdAsync(Guid agentId, CancellationToken cancellationToken = default)
            => Task.FromResult<CliAgentBindingDto?>(new CliAgentBindingDto { Id = Guid.NewGuid(), AgentId = agentId, CliRuntimeId = Guid.NewGuid() });

        public Task<Result<CliAgentBindingDto>> UpsertAsync(Guid agentId, UpsertCliAgentBindingDto input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result> DeleteAsync(Guid agentId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
