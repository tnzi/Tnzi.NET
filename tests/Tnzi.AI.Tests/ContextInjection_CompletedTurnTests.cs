namespace Tnzi.AI.Tests;

/// <summary>
/// 轮后回调（<see cref="IContextProvider.OnCompletedAsync"/>）必须拿到<b>完成的这一轮</b>，并且不受请求取消的影响。
/// </summary>
/// <remarks>
/// 本轮用户消息只在执行器的工作副本里，答复也从不写回 <c>context.Messages</c>：此前把后者原样交给提供器，
/// 记忆沉淀与实体提炼在新线程首轮什么都拿不到，之后每一轮提炼的都是上一轮。流式下客户端收到 [DONE]
/// 就断开、请求令牌随即取消，此时还在途的提炼调用跟着被取消。
/// 这里用真实的 HistoryMiddleware + ContextInjectionMiddleware 驱动，不往 context.Messages 里预放本轮消息。
/// </remarks>
[Collection("ContextInjectionCache")]
public class ContextInjection_CompletedTurnTests
{
    public ContextInjection_CompletedTurnTests()
    {
        ContextInjectionMiddleware.ClearAllCachesForTesting();
    }

    [Fact]
    public async Task NonStreaming_ProvidersSeeTheCurrentQuestionAndTheReply()
    {
        var harness = new Harness(history:
        [
            new ChatMessage(ChatRole.User, "previous question"),
            new ChatMessage(ChatRole.Assistant, "previous answer")
        ]);

        await harness.RunAsync("Remember my VAT number is CA123", reply: "Noted, your VAT number is CA123.");

        var call = harness.Provider.Calls.ShouldHaveSingleItem();
        call.Messages[^2].Role.ShouldBe(ChatRole.User);
        call.Messages[^2].Text.ShouldBe("Remember my VAT number is CA123");
        call.Messages[^1].Role.ShouldBe(ChatRole.Assistant);
        call.Messages[^1].Text.ShouldBe("Noted, your VAT number is CA123.");
        call.Messages.ShouldContain(m => m.Text == "previous question");
    }

    [Fact]
    public async Task NonStreaming_RejectedTurn_IsNotHandedToProviders()
    {
        var harness = new Harness(history: []);

        await harness.RunAsync("anything", reply: "blocked", finishReason: FinishReasons.GuardrailRejected);

        harness.Provider.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task NonStreaming_FailedRun_IsNotHandedToProviders()
    {
        var harness = new Harness(history: []);

        await Should.ThrowAsync<InvalidOperationException>(() => harness.RunFailingAsync("anything"));

        harness.Provider.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task NonStreaming_ProvidersRunOnTheirOwnTokenNotTheRequestToken()
    {
        var harness = new Harness(history: []);
        using var request = new CancellationTokenSource();

        await harness.RunAsync("question", reply: "answer", cancelRequestBeforeCompletion: request);

        var call = harness.Provider.Calls.ShouldHaveSingleItem();
        call.TokenWasCancelled.ShouldBeFalse();
        call.TokenCanBeCancelled.ShouldBeTrue("the completion hook must still be bounded by its own timeout");
    }

    [Fact]
    public async Task Streaming_ProvidersSeeTheAccumulatedReply()
    {
        var harness = new Harness(history: []);

        await harness.RunStreamingAsync("What is my VAT number?", ["Your VAT ", "number is ", "CA123."]);

        var call = harness.Provider.Calls.ShouldHaveSingleItem();
        call.Messages[^2].Text.ShouldBe("What is my VAT number?");
        call.Messages[^1].Role.ShouldBe(ChatRole.Assistant);
        call.Messages[^1].Text.ShouldBe("Your VAT number is CA123.");
    }

    /// <summary>
    /// 消费方在终止 chunk 后写出 [DONE]，客户端随即断开、请求令牌取消，之后的排空中断。
    /// 答复已经交付，轮后回调照样要跑，而且拿到的令牌不能是那个已取消的请求令牌。
    /// </summary>
    [Fact]
    public async Task Streaming_ClientDisconnectsAfterDone_ProvidersStillRunWithALiveToken()
    {
        var harness = new Harness(history: []);
        using var request = new CancellationTokenSource();

        await harness.RunStreamingAndDisconnectAfterDoneAsync("question", ["the ", "answer"], request);

        var call = harness.Provider.Calls.ShouldHaveSingleItem();
        call.TokenWasCancelled.ShouldBeFalse();
        call.Messages[^1].Text.ShouldBe("the answer");
    }

    [Fact]
    public async Task Streaming_AbandonedBeforeAnyAnswer_IsNotHandedToProviders()
    {
        var harness = new Harness(history: []);

        await harness.RunStreamingAndAbandonBeforeDoneAsync("question");

        harness.Provider.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task CompletedTurn_DoesNotLeakIntoTheModelMessages()
    {
        var harness = new Harness(history: []);

        var context = await harness.RunAsync("question", reply: "answer");

        context.Messages.ShouldNotContain(m => m.Role == ChatRole.User && m.Text == "question");
        context.Messages.ShouldNotContain(m => m.Role == ChatRole.Assistant && m.Text == "answer");
    }

    internal sealed record CompletionCall(List<ChatMessage> Messages, bool TokenWasCancelled, bool TokenCanBeCancelled);

    internal sealed class CapturingProvider : IContextProvider
    {
        public List<CompletionCall> Calls { get; } = [];

        public Task<ContextInjection> GetContextAsync(List<ChatMessage> messages, CancellationToken ct = default)
            => Task.FromResult(ContextInjection.Empty);

        public Task OnCompletedAsync(List<ChatMessage> messages, CancellationToken ct = default)
        {
            Calls.Add(new CompletionCall([.. messages], ct.IsCancellationRequested, ct.CanBeCanceled));
            return Task.CompletedTask;
        }
    }

    private sealed class Harness
    {
        private readonly Guid _threadId = Guid.NewGuid();
        private readonly HistoryMiddleware _history;
        private readonly ContextInjectionMiddleware _contextInjection;

        public Harness(List<ChatMessage> history)
        {
            var threadService = new Mock<IAgentThreadInternalService>();
            threadService
                .Setup(s => s.GetOrCreateThreadAsync(_threadId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((_threadId, false));
            threadService
                .Setup(s => s.GetMessageHistoryAsync(_threadId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => history.ToList());

            var aiOptions = new AIOptions();
            aiOptions.ContextProviders.Enabled = true;
            var options = new StaticOptionsMonitor<AIOptions>(aiOptions);
            _history = new HistoryMiddleware(threadService.Object, options, NullLogger<HistoryMiddleware>.Instance);

            var factory = new CompositeContextProviderFactory(
                [new SingleProviderContributor(Provider)],
                options,
                new HeuristicTokenEstimator(),
                NullLoggerFactory.Instance,
                NullLogger<CompositeContextProviderFactory>.Instance);
            _contextInjection = new ContextInjectionMiddleware(factory, NullLogger<ContextInjectionMiddleware>.Instance);
        }

        public CapturingProvider Provider { get; } = new();

        private AiMiddlewareContext NewContext(string userMessage) => new()
        {
            Request = new AgentRunRequest { ThreadId = _threadId, UserMessage = userMessage, UserId = Guid.NewGuid() },
            Agent = AgentResolution.Success(agent: null!, provider: "test", model: "test-model", agentId: null),
            ServiceProvider = new ServiceCollection().BuildServiceProvider()
        };

        public async Task<AiMiddlewareContext> RunAsync(
            string userMessage, string reply, string? finishReason = null, CancellationTokenSource? cancelRequestBeforeCompletion = null)
        {
            var context = NewContext(userMessage);
            var token = cancelRequestBeforeCompletion?.Token ?? CancellationToken.None;

            await _history.InvokeAsync(context, (ctx, ct) =>
                _contextInjection.InvokeAsync(ctx, (_, _) =>
                {
                    // 模拟客户端在答复产出后断开：请求令牌在轮后回调之前被取消
                    cancelRequestBeforeCompletion?.Cancel();
                    return Task.FromResult(new AgentRunResult { Response = reply, FinishReason = finishReason });
                }, ct), token);
            return context;
        }

        public async Task RunFailingAsync(string userMessage)
        {
            var context = NewContext(userMessage);
            await _history.InvokeAsync(context, (ctx, ct) =>
                _contextInjection.InvokeAsync(ctx, (_, _) => throw new InvalidOperationException("model failed"), ct));
        }

        public async Task RunStreamingAsync(string userMessage, IReadOnlyList<string> textChunks)
        {
            var context = NewContext(userMessage);
            await foreach (var _ in _history.InvokeStreamingAsync(context, (ctx, ct) =>
                               _contextInjection.InvokeStreamingAsync(ctx, (_, _) => Stream(textChunks), ct)))
            {
            }
        }

        /// <summary>
        /// 读到终止 chunk 后取消请求令牌，再继续排空：内层在终止 chunk 之后的收尾看到取消就抛出。
        /// </summary>
        public async Task RunStreamingAndDisconnectAfterDoneAsync(string userMessage, IReadOnlyList<string> textChunks, CancellationTokenSource request)
        {
            var context = NewContext(userMessage);
            var stream = _contextInjection.InvokeStreamingAsync(context, (_, ct) => StreamThenObserveCancellation(textChunks, ct), request.Token);

            try
            {
                await foreach (var chunk in stream)
                {
                    if (chunk.FinishReason != null)
                    {
                        request.Cancel();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 排空被请求取消打断：这正是生产里 [DONE] 之后的情形
            }
        }

        public async Task RunStreamingAndAbandonBeforeDoneAsync(string userMessage)
        {
            var context = NewContext(userMessage);
            var stream = _contextInjection.InvokeStreamingAsync(context, (_, _) => Stream(["partial ", "never finished"]));

            await using var enumerator = stream.GetAsyncEnumerator();
            (await enumerator.MoveNextAsync()).ShouldBeTrue();
        }

        private static async IAsyncEnumerable<AgentStreamChunk> Stream(IReadOnlyList<string> textChunks)
        {
            foreach (var text in textChunks)
            {
                await Task.Yield();
                yield return new AgentStreamChunk { Text = text };
            }

            yield return new AgentStreamChunk { FinishReason = FinishReasons.Stop };
        }

        private static async IAsyncEnumerable<AgentStreamChunk> StreamThenObserveCancellation(
            IReadOnlyList<string> textChunks, [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var chunk in Stream(textChunks))
            {
                yield return chunk;
            }

            // 终止 chunk 之后内层还有收尾（写历史、记用量……），它们看到的是已取消的请求令牌
            ct.ThrowIfCancellationRequested();
        }
    }

    private sealed class SingleProviderContributor(IContextProvider provider) : IContextProviderContributor
    {
        public int Order => 0;

        public IContextProvider? TryCreate(ContextProviderCreationContext context) => provider;
    }
}
