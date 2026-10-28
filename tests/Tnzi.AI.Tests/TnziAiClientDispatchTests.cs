using System.Text;

namespace Tnzi.AI.Tests;

/// <summary>
/// 嵌入式客户端与 HTTP 聊天走同一个分支点：绑定了外部 CLI 运行时的 Agent 交给那个运行时，
/// 没有绑定的照常进内建引擎。此前客户端直调 <see cref="IAgentRuntime"/>，绑定的 Agent 静默跑在内建引擎上。
/// </summary>
public class TnziAiClientDispatchTests
{
    private static readonly Guid BoundAgentId = Guid.NewGuid();
    private static readonly Guid ThreadId = Guid.NewGuid();
    private const string CliReply = "answered by the bound CLI runtime";

    [Fact]
    public async Task ChatAsync_BoundAgent_RunsOnTheBoundRuntime()
    {
        var runtime = new Mock<IAgentRuntime>(MockBehavior.Strict);
        var client = new TnziAiClient(CreateFacade(runtime.Object));

        var response = await client.ChatAsync("hi", options: new AiClientOptions { AgentId = BoundAgentId });

        response.Text.ShouldBe(CliReply);
        runtime.Verify(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChatStreamingAsync_BoundAgent_StreamsFromTheBoundRuntime()
    {
        var runtime = new Mock<IAgentRuntime>(MockBehavior.Strict);
        var client = new TnziAiClient(CreateFacade(runtime.Object));

        var text = new StringBuilder();
        await foreach (var evt in client.ChatStreamingAsync("hi", options: new AiClientOptions { AgentId = BoundAgentId }))
        {
            text.Append(evt.Text);
        }

        text.ToString().ShouldBe(CliReply);
    }

    [Fact]
    public async Task ChatAsync_UnboundAgent_StillRunsOnTheBuiltInEngine()
    {
        var unbound = Guid.NewGuid();
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(r => r.RunAsync(It.Is<AgentRunRequest>(q => q.AgentId == unbound), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult { Response = "built-in", FinishReason = FinishReasons.Stop });
        var client = new TnziAiClient(CreateFacade(runtime.Object));

        var response = await client.ChatAsync("hi", options: new AiClientOptions { AgentId = unbound });

        response.Text.ShouldBe("built-in");
    }

    private static AgentDispatchFacade CreateFacade(IAgentRuntime runtime)
    {
        var dispatcher = new CompletingDispatcher();
        var threads = new FixedThreadService();
        var services = new ServiceCollection();
        services.AddScoped<ICliAgentDispatcher>(_ => dispatcher);
        services.AddScoped<IAgentThreadInternalService>(_ => threads);

        return new AgentDispatchFacade(
            runtime,
            new SingleAgentBindingService(),
            dispatcher,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentDispatchFacade>.Instance);
    }

    private sealed class SingleAgentBindingService : ICliAgentBindingService
    {
        public Task<CliAgentBindingDto?> GetByAgentIdAsync(Guid agentId, CancellationToken cancellationToken = default)
            => Task.FromResult(agentId == BoundAgentId
                ? new CliAgentBindingDto { Id = Guid.NewGuid(), AgentId = agentId, CliRuntimeId = Guid.NewGuid() }
                : null);

        public Task<Result<CliAgentBindingDto>> UpsertAsync(Guid agentId, UpsertCliAgentBindingDto input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result> DeleteAsync(Guid agentId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class CompletingDispatcher : ICliAgentDispatcher
    {
        public Task<Result<Guid>> EnqueueAsync(CliRunRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<Guid>.Success(Guid.NewGuid()));

        public async IAsyncEnumerable<CliAgentEvent> StreamAsync(Guid runId, int fromSequence = 0, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new CliAgentEvent { Type = CliAgentEventType.Text, Content = CliReply };
        }

        public Task<Result<CliRunDto>> GetAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<CliRunDto>.Success(new CliRunDto
            {
                Id = runId,
                ThreadId = ThreadId,
                Status = CliRunStatus.Completed,
                Output = CliReply
            }));

        public Task<Result<IPagedList<CliRunDto>>> GetListAsync(CliRunQueryDto query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<List<CliRunMessageDto>>> GetMessagesAsync(Guid runId, int fromSequence = 0, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result> CancelAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FixedThreadService : IAgentThreadInternalService
    {
        public Task<(Guid threadId, bool isNewThread)> GetOrCreateThreadAsync(Guid? threadId, Guid? agentId, CancellationToken ct = default)
            => Task.FromResult((ThreadId, true));

        public Task<Guid> SaveMessageAsync(Guid threadId, string role, string content,
            string? toolCalls = null, string? usage = null, Guid? messageId = null, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid());

        public Task<List<ChatMessage>> GetMessageHistoryAsync(Guid threadId, int? limit = null, CancellationToken ct = default)
            => Task.FromResult(new List<ChatMessage>());

        public Task<string?> GetMetadataValueAsync(Guid threadId, string key, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task SetMetadataValueAsync(Guid threadId, string key, string? valueJson, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
