namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// A new thread is titled the moment its first message is accepted: before the model is asked for the reply,
/// from the text the model actually receives (after the input guard), and only for top-level conversations.
/// </summary>
public class ThreadTitleMiddlewareTests
{
    private const string RawMessage = "my card is 4111 1111 1111 1111";
    private const string RedactedMessage = "my card is [REDACTED]";

    private static readonly Guid ThreadId = Guid.NewGuid();
    private static readonly Guid AgentId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewThread_TitleStartsBeforeTheReply(bool streaming)
    {
        var titles = new RecordingTitles();
        var context = Context(isNewThread: true);
        var titledBeforeReply = false;

        await RunAsync(new ThreadTitleMiddleware(titles), context, streaming, () => titledBeforeReply = titles.Started.Count == 1);

        titledBeforeReply.ShouldBeTrue("the title starts before the rest of the pipeline asks the model for the reply");
        titles.Started.ShouldHaveSingleItem().ShouldBe(new ThreadTitleRequest(ThreadId, "plan a trip", UserId, AgentId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GuardrailRedactedMessage_TitleUsesTheRedactedText(bool streaming)
    {
        var titles = new RecordingTitles();
        var context = Context(isNewThread: true, message: RawMessage);
        context.EffectiveUserMessage = RedactedMessage;

        await RunAsync(new ThreadTitleMiddleware(titles), context, streaming);

        titles.Started.ShouldHaveSingleItem().UserMessage.ShouldBe(RedactedMessage);
    }

    [Fact]
    public async Task ContentPartsOnly_TitleUsesThePartText()
    {
        var titles = new RecordingTitles();
        var context = Context(isNewThread: true, message: null,
            parts: [new TextContentPartDto { Text = "summarise the quarterly report" }]);

        await RunAsync(new ThreadTitleMiddleware(titles), context, streaming: false);

        titles.Started.ShouldHaveSingleItem().UserMessage.ShouldBe("summarise the quarterly report");
    }

    [Fact]
    public async Task ExistingThread_IsNotRetitled()
    {
        var titles = new RecordingTitles();

        await RunAsync(new ThreadTitleMiddleware(titles), Context(isNewThread: false), streaming: false);

        titles.Started.ShouldBeEmpty();
    }

    [Fact]
    public async Task SubAgentRun_IsNotTitled()
    {
        var titles = new RecordingTitles();

        await RunAsync(new ThreadTitleMiddleware(titles), Context(isNewThread: true, parentRunId: Guid.NewGuid()), streaming: false);

        titles.Started.ShouldBeEmpty();
    }

    [Fact]
    public void Order_IsAfterTheInputGuardAndTheUsageLimit_AndBeforeHistory()
    {
        var order = new ThreadTitleMiddleware(new RecordingTitles()).Order;

        order.ShouldBeGreaterThan(AiMiddlewareOrders.InputGuardrail, "a refused message is not accepted, and the title uses the guarded text");
        order.ShouldBeGreaterThan(AiMiddlewareOrders.Quota, "a turn over the usage limit is not accepted");
        order.ShouldBeLessThan(AiMiddlewareOrders.History);
    }

    private static AiMiddlewareContext Context(bool isNewThread, string? message = "plan a trip",
        List<ContentPartDto>? parts = null, Guid? parentRunId = null)
        => new()
        {
            Request = new AgentRunRequest
            {
                UserMessage = message, ContentParts = parts, ThreadId = ThreadId, AgentId = AgentId, UserId = UserId,
                ParentRunId = parentRunId
            },
            Agent = AgentResolution.Success(agent: null!, provider: "test", model: "test", agentId: AgentId),
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            Messages = [],
            IsNewThread = isNewThread
        };

    private static async Task RunAsync(ThreadTitleMiddleware middleware, AiMiddlewareContext context, bool streaming, Action? onReply = null)
    {
        if (!streaming)
        {
            await middleware.InvokeAsync(context, (_, _) =>
            {
                onReply?.Invoke();
                return Task.FromResult(new AgentRunResult { Response = "ok", FinishReason = FinishReasons.Stop });
            });
            return;
        }

        await foreach (var _ in middleware.InvokeStreamingAsync(context, (_, _) => Reply(onReply)))
        {
        }
    }

    private static async IAsyncEnumerable<AgentStreamChunk> Reply(Action? onReply)
    {
        onReply?.Invoke();
        await Task.Yield();
        yield return new AgentStreamChunk { Text = "ok", FinishReason = FinishReasons.Stop };
    }

    private sealed class RecordingTitles : IThreadTitleService
    {
        public List<ThreadTitleRequest> Started { get; } = [];

        public Task StartAsync(ThreadTitleRequest request, CancellationToken cancellationToken = default)
        {
            Started.Add(request);
            return Task.CompletedTask;
        }

        public Task<bool> GenerateAsync(ThreadTitleRequestedEvent request, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }
}
