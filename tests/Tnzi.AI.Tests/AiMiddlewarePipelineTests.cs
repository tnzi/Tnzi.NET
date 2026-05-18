namespace Tnzi.AI.Tests;

/// <summary>
/// AI 中间件管道单元测试
/// </summary>
public class AiMiddlewarePipelineTests
{
    #region Helper Methods

    private static IBudgetService CreatePassthroughBudgetService()
    {
        var mock = new Mock<IBudgetService>();
        mock.Setup(x => x.CheckBudgetAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BudgetCheckResult { IsAllowed = true, Status = BudgetStatus.WithinBudget });
        return mock.Object;
    }

    private static AiMiddlewareContext CreateContext(string userMessage = "Hello", Guid? userId = null, Guid? threadId = null)
    {
        return new AiMiddlewareContext
        {
            Request = new AgentRunRequest
            {
                UserMessage = userMessage,
                UserId = userId,
                ThreadId = threadId
            },
            Agent = AgentResolution.Success(
                agent: null!,
                provider: "TestProvider",
                model: "test-model",
                agentId: null),
            ServiceProvider = new Mock<IServiceProvider>().Object
        };
    }

    private static AgentRunResult CreateSuccessResult(string response = "Hi there", int promptTokens = 10, int completionTokens = 5)
    {
        return new AgentRunResult
        {
            Response = response,
            Usage = new TokenUsageDto { InputTokens = promptTokens, OutputTokens = completionTokens, TotalTokens = promptTokens + completionTokens }
        };
    }

    private static IOptions<AIOptions> CreateOptions(Action<AIOptions>? configure = null)
    {
        var options = new AIOptions();
        configure?.Invoke(options);
        return Microsoft.Extensions.Options.Options.Create(options);
    }

    private static async IAsyncEnumerable<AgentStreamChunk> AppendSuffix(
        IAsyncEnumerable<AgentStreamChunk> source,
        string suffix,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in source.WithCancellation(cancellationToken))
        {
            yield return new AgentStreamChunk
            {
                Text = chunk.Text + suffix,
                Error = chunk.Error,
                Usage = chunk.Usage,
                FinishReason = chunk.FinishReason
            };
        }
    }

    #endregion

    #region AiMiddlewarePipeline

    [Fact]
    public async Task Pipeline_Build_ExecutesMiddlewaresInOrder()
    {
        var pipeline = new AiMiddlewarePipeline();
        var executionOrder = new List<string>();

        pipeline.Use(200, async (ctx, next, ct) =>
        {
            executionOrder.Add("B-before");
            var result = await next(ctx, ct);
            executionOrder.Add("B-after");
            return result;
        });

        pipeline.Use(100, async (ctx, next, ct) =>
        {
            executionOrder.Add("A-before");
            var result = await next(ctx, ct);
            executionOrder.Add("A-after");
            return result;
        });

        var context = CreateContext();
        var coreResult = CreateSuccessResult();
        var built = pipeline.Build((ctx, ct) => Task.FromResult(coreResult));

        var result = await built(context, CancellationToken.None);

        result.Response.ShouldBe("Hi there");
        // 洋葱模型: A(100) 先 before, B(200) 后 before, B 先 after, A 后 after
        executionOrder.ShouldBe(["A-before", "B-before", "B-after", "A-after"]);
    }

    [Fact]
    public async Task Pipeline_BuildStreaming_ExecutesMiddlewaresInOrder()
    {
        var pipeline = new AiMiddlewarePipeline();
        var executionOrder = new List<string>();

        // 添加一个跟踪中间件（通过 IAiMiddleware 实现）
        pipeline.Use(new TrackingMiddleware(100, executionOrder, "A"));
        pipeline.Use(new TrackingMiddleware(200, executionOrder, "B"));

        var context = CreateContext();
        var coreChunks = new[] { new AgentStreamChunk { Text = "Hello" }, new AgentStreamChunk { Text = " World" } };

        var built = pipeline.BuildStreaming((ctx, ct) => ToAsyncEnumerable(coreChunks));

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in built(context, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        chunks.Count.ShouldBe(2);
        chunks[0].Text.ShouldBe("Hello");
        chunks[1].Text.ShouldBe(" World");
    }

    [Fact]
    public async Task Pipeline_BuildStreaming_WithNonStreamingLambda_ThrowsExplicitly()
    {
        var pipeline = new AiMiddlewarePipeline();
        pipeline.Use(100, async (ctx, next, ct) => await next(ctx, ct));

        var built = pipeline.BuildStreaming((ctx, ct) => ToAsyncEnumerable([new AgentStreamChunk { Text = "Hello" }]));

        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in built(CreateContext(), CancellationToken.None))
            {
            }
        });
    }

    [Fact]
    public async Task Pipeline_BuildStreaming_WithStreamingLambda_UsesStreamingHandler()
    {
        var pipeline = new AiMiddlewarePipeline();
        pipeline.Use(
            100,
            async (ctx, next, ct) => await next(ctx, ct),
            (ctx, next, ct) => AppendSuffix(next(ctx, ct), "!", ct));

        var built = pipeline.BuildStreaming((ctx, ct) => ToAsyncEnumerable([new AgentStreamChunk { Text = "Hello" }]));

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in built(CreateContext(), CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        chunks.ShouldHaveSingleItem();
        chunks[0].Text.ShouldBe("Hello!");
    }

    [Fact]
    public void Pipeline_Count_ReturnsCorrectCount()
    {
        var pipeline = new AiMiddlewarePipeline();
        pipeline.Count.ShouldBe(0);

        pipeline.Use(100, (ctx, next, ct) => next(ctx, ct));
        pipeline.Count.ShouldBe(1);

        pipeline.Use(new TrackingMiddleware(200, [], "test"));
        pipeline.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Pipeline_EmptyPipeline_ExecutesCoreDirectly()
    {
        var pipeline = new AiMiddlewarePipeline();
        var coreResult = CreateSuccessResult("direct");
        var built = pipeline.Build((ctx, ct) => Task.FromResult(coreResult));

        var result = await built(CreateContext(), CancellationToken.None);
        result.Response.ShouldBe("direct");
    }

    #endregion

    #region QuotaMiddleware

    [Fact]
    public async Task Quota_NoUserId_SkipsQuotaCheck()
    {
        var quotaService = new Mock<IQuotaService>();
        var middleware = new QuotaMiddleware(
            quotaService.Object,
            CreatePassthroughBudgetService(),
            new HeuristicTokenEstimator(),
            Mock.Of<ILogger<QuotaMiddleware>>());

        var context = CreateContext(userId: null);
        var expected = CreateSuccessResult();
        var result = await middleware.InvokeAsync(context,
            (ctx, ct) => Task.FromResult(expected));

        result.ShouldBe(expected);
        quotaService.Verify(q => q.ReserveQuotaAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Quota_ReservationFails_ReturnsQuotaExceeded()
    {
        var userId = Guid.NewGuid();
        var quotaService = new Mock<IQuotaService>();
        quotaService.Setup(q => q.ReserveQuotaAsync(userId, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<QuotaReservation>.Failure("Daily quota exceeded"));

        var middleware = new QuotaMiddleware(
            quotaService.Object,
            CreatePassthroughBudgetService(),
            new HeuristicTokenEstimator(),
            Mock.Of<ILogger<QuotaMiddleware>>());

        var context = CreateContext(userId: userId);
        var result = await middleware.InvokeAsync(context,
            (ctx, ct) => Task.FromResult(CreateSuccessResult()));

        result.FinishReason.ShouldBe("quota_exceeded");
    }

    [Fact]
    public async Task Quota_Success_SettlesWithActualUsage()
    {
        var userId = Guid.NewGuid();
        var reservation = new QuotaReservation { ReservedTokens = 100, ReservedAt = DateTime.UtcNow };

        var quotaService = new Mock<IQuotaService>();
        quotaService.Setup(q => q.ReserveQuotaAsync(userId, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<QuotaReservation>.Success(reservation));

        var middleware = new QuotaMiddleware(
            quotaService.Object,
            CreatePassthroughBudgetService(),
            new HeuristicTokenEstimator(),
            Mock.Of<ILogger<QuotaMiddleware>>());

        var context = CreateContext(userId: userId);
        var expected = CreateSuccessResult(promptTokens: 20, completionTokens: 10);
        var result = await middleware.InvokeAsync(context,
            (ctx, ct) => Task.FromResult(expected));

        result.Response.ShouldBe("Hi there");
        quotaService.Verify(q => q.SettleQuotaAsync(userId, reservation, 30, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region InputGuardrailMiddleware

    [Fact]
    public async Task InputGuardrail_Allowed_PassesThrough()
    {
        var runner = CreateGuardrailRunner(guardrailsEnabled: true);
        var middleware = new InputGuardrailMiddleware(runner, Mock.Of<ILogger<InputGuardrailMiddleware>>());

        var expected = CreateSuccessResult();
        var result = await middleware.InvokeAsync(CreateContext("Hello"),
            (ctx, ct) => Task.FromResult(expected));

        result.ShouldBe(expected);
    }

    [Fact]
    public async Task InputGuardrail_Rejected_ReturnsGuardrailRejected()
    {
        // 配置 MaxLengthGuardrail，限制为 5 字符
        var options = CreateOptions(o =>
        {
            o.Guardrails.Enabled = true;
            o.Guardrails.EnableMaxLength = true;
            o.Guardrails.MaxInputLength = 5;
        });

        var runner = new GuardrailRunner(
            [new MaxLengthGuardrail(options)],
            [],
            options,
            Mock.Of<ILogger<GuardrailRunner>>());

        var middleware = new InputGuardrailMiddleware(runner, Mock.Of<ILogger<InputGuardrailMiddleware>>());

        var result = await middleware.InvokeAsync(CreateContext("This is a very long input"),
            (ctx, ct) => Task.FromResult(CreateSuccessResult()));

        result.FinishReason.ShouldBe("guardrail_rejected");
        result.Status.ShouldBe(AgentRunStatus.Failed);
    }

    [Fact]
    public async Task InputGuardrail_GuardrailsDisabled_PassesThrough()
    {
        var runner = CreateGuardrailRunner(guardrailsEnabled: false);
        var middleware = new InputGuardrailMiddleware(runner, Mock.Of<ILogger<InputGuardrailMiddleware>>());

        var expected = CreateSuccessResult();
        var result = await middleware.InvokeAsync(CreateContext("Any input"),
            (ctx, ct) => Task.FromResult(expected));

        result.ShouldBe(expected);
    }

    #endregion

    #region HistoryMiddleware

    [Fact]
    public async Task History_WithThreadId_LoadsAndSavesMessages()
    {
        var threadId = Guid.NewGuid();
        var threadService = new Mock<IAgentThreadInternalService>();

        // 模拟历史消息
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "Previous question"),
            new(ChatRole.Assistant, "Previous answer")
        };
        threadService.Setup(s => s.GetMessageHistoryAsync(threadId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(history);

        var middleware = new HistoryMiddleware(
            threadService.Object,
            CreateOptions(),
            Mock.Of<ILogger<HistoryMiddleware>>());

        var context = CreateContext(threadId: threadId);
        var expected = CreateSuccessResult("New answer");

        var result = await middleware.InvokeAsync(context,
            (ctx, ct) =>
            {
                // 验证历史已加载到 Messages
                ctx.Messages.Count.ShouldBe(2);
                return Task.FromResult(expected);
            });

        result.Response.ShouldBe("New answer");

        // 验证保存了用户消息和助手回复
        threadService.Verify(s => s.SaveMessageAsync(threadId, "user", "Hello", null, null, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
        threadService.Verify(s => s.SaveMessageAsync(threadId, "assistant", "New answer", null, It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task History_NoThreadId_AutoCreatesThreadAndSaves()
    {
        var autoThreadId = Guid.NewGuid();
        var threadService = new Mock<IAgentThreadInternalService>();
        threadService.Setup(s => s.GetOrCreateThreadAsync(null, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((default(ConversationContext)!, autoThreadId, true));
        threadService.Setup(s => s.GetMessageHistoryAsync(autoThreadId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChatMessage>());

        var middleware = new HistoryMiddleware(
            threadService.Object,
            CreateOptions(),
            Mock.Of<ILogger<HistoryMiddleware>>());

        var context = CreateContext(threadId: null);
        var expected = CreateSuccessResult();

        var result = await middleware.InvokeAsync(context,
            (ctx, ct) => Task.FromResult(expected));

        // 验证 ThreadId 被自动创建并写回
        context.Request.ThreadId.ShouldBe(autoThreadId);
        result.ThreadId.ShouldBe(autoThreadId);

        // 验证自动创建了线程并保存了用户消息和助手回复
        threadService.Verify(s => s.GetOrCreateThreadAsync(null, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
        threadService.Verify(s => s.SaveMessageAsync(autoThreadId, "user", "Hello", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
        threadService.Verify(s => s.SaveMessageAsync(autoThreadId, "assistant", "Hi there", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task History_GuardrailRejected_DoesNotSaveMessages()
    {
        var threadId = Guid.NewGuid();
        var threadService = new Mock<IAgentThreadInternalService>();
        threadService.Setup(s => s.GetMessageHistoryAsync(threadId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChatMessage>());

        var middleware = new HistoryMiddleware(
            threadService.Object,
            CreateOptions(),
            Mock.Of<ILogger<HistoryMiddleware>>());

        var context = CreateContext(threadId: threadId);
        var guardrailResult = new AgentRunResult
        {
            Response = "",
            FinishReason = "guardrail_rejected",
            Status = AgentRunStatus.Failed
        };

        await middleware.InvokeAsync(context, (ctx, ct) => Task.FromResult(guardrailResult));

        // Guardrail 拒绝时不应保存任何消息
        threadService.Verify(s => s.SaveMessageAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region ContextInjectionMiddleware

    [Fact]
    public async Task ContextInjection_WithProviders_InjectsContext()
    {
        var mockProvider = new Mock<IContextProvider>();
        mockProvider.Setup(p => p.IsEnabled(It.IsAny<AiMiddlewareContext?>())).Returns(true);
        mockProvider.Setup(p => p.GetContextAsync(It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextInjection
            {
                Messages = [new ChatMessage(ChatRole.System, "Context from RAG")],
                Citations = [new CitationDto { SourceName = "Source 1" }]
            });

        var middleware = new ContextInjectionMiddleware(
            CreateProviderFactory([new TestContextContributor(mockProvider.Object)]),
            Mock.Of<ILogger<ContextInjectionMiddleware>>());

        var context = CreateContext();
        var expected = CreateSuccessResult();

        var result = await middleware.InvokeAsync(context,
            (ctx, ct) =>
            {
                // 验证上下文已注入
                ctx.Messages.Count.ShouldBe(1);
                ctx.Messages[0].Text.ShouldBe("Context from RAG");
                ctx.Citations.Count.ShouldBe(1);
                return Task.FromResult(expected);
            });

        result.ShouldBe(expected);
    }

    [Fact]
    public async Task ContextInjection_NoProviders_PassesThrough()
    {
        var middleware = new ContextInjectionMiddleware(
            CreateProviderFactory([]),
            Mock.Of<ILogger<ContextInjectionMiddleware>>());

        var context = CreateContext();
        var expected = CreateSuccessResult();

        var result = await middleware.InvokeAsync(context,
            (ctx, ct) =>
            {
                ctx.Messages.Count.ShouldBe(0);
                ctx.Citations.Count.ShouldBe(0);
                return Task.FromResult(expected);
            });

        result.ShouldBe(expected);
    }

    private static CompositeContextProviderFactory CreateProviderFactory(IEnumerable<IContextProviderContributor> contributors)
        => new(
            contributors,
            CreateOptions(o => o.ContextProviders.Enabled = true),
            new HeuristicTokenEstimator(),
            NullLoggerFactory.Instance,
            NullLogger<CompositeContextProviderFactory>.Instance);

    private sealed class TestContextContributor : IContextProviderContributor
    {
        private readonly IContextProvider _provider;
        public TestContextContributor(IContextProvider provider) => _provider = provider;
        public int Order => 0;
        public IContextProvider? TryCreate(ContextProviderCreationContext context) => _provider;
    }

    #endregion

    #region UsageLoggingMiddleware

    [Fact]
    public async Task UsageLogging_Success_LogsUsage()
    {
        var usageLogService = new Mock<IUsageLogService>();

        var middleware = new UsageLoggingMiddleware(
            usageLogService.Object,
            Mock.Of<IServiceScopeFactory>(),
            Mock.Of<ILogger<UsageLoggingMiddleware>>());

        var context = CreateContext();
        var expected = CreateSuccessResult(promptTokens: 50, completionTokens: 25);

        var result = await middleware.InvokeAsync(context,
            (ctx, ct) => Task.FromResult(expected));

        result.ShouldBe(expected);
        usageLogService.Verify(s => s.LogUsageAsync(
            AIOperationType.Chat, "TestProvider", "test-model", 50, 25,
            It.IsAny<long>(), true, null, null, null,
            0, 0,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UsageLogging_Exception_LogsFailure()
    {
        var usageLogService = new Mock<IUsageLogService>();

        var middleware = new UsageLoggingMiddleware(
            usageLogService.Object,
            Mock.Of<IServiceScopeFactory>(),
            Mock.Of<ILogger<UsageLoggingMiddleware>>());

        var context = CreateContext();

        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await middleware.InvokeAsync(context,
                (ctx, ct) => throw new InvalidOperationException("Test error"));
        });

        usageLogService.Verify(s => s.LogUsageAsync(
            AIOperationType.Chat, "TestProvider", "test-model", 0, 0,
            It.IsAny<long>(), false, "Test error", null, null,
            0, 0,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region OutputGuardrailMiddleware

    [Fact]
    public async Task OutputGuardrail_Allowed_ReturnsOriginal()
    {
        var runner = CreateGuardrailRunner(guardrailsEnabled: true);
        var middleware = new OutputGuardrailMiddleware(
            runner, Enumerable.Empty<IOutputGuardrail>(),
            CreateOptions(o => o.Guardrails.Enabled = true),
            Mock.Of<ILogger<OutputGuardrailMiddleware>>());

        var expected = CreateSuccessResult("Safe output");
        var result = await middleware.InvokeAsync(CreateContext(),
            (ctx, ct) => Task.FromResult(expected));

        result.Response.ShouldBe("Safe output");
    }

    [Fact]
    public async Task OutputGuardrail_EmptyResponse_SkipsCheck()
    {
        var runner = CreateGuardrailRunner(guardrailsEnabled: true);
        var middleware = new OutputGuardrailMiddleware(
            runner, Enumerable.Empty<IOutputGuardrail>(),
            CreateOptions(o => o.Guardrails.Enabled = true),
            Mock.Of<ILogger<OutputGuardrailMiddleware>>());

        var expected = new AgentRunResult { Response = "" };
        var result = await middleware.InvokeAsync(CreateContext(),
            (ctx, ct) => Task.FromResult(expected));

        result.Response.ShouldBe("");
    }

    #endregion

    #region Integration: Full Pipeline

    [Fact]
    public async Task FullPipeline_AllMiddlewares_ExecuteInOrder()
    {
        var executionOrder = new List<string>();

        var pipeline = new AiMiddlewarePipeline();

        // 模拟中间件，仅跟踪执行顺序
        pipeline.Use(100, async (ctx, next, ct) => { executionOrder.Add("Quota-before"); var r = await next(ctx, ct); executionOrder.Add("Quota-after"); return r; });
        pipeline.Use(200, async (ctx, next, ct) => { executionOrder.Add("InputGuardrail"); return await next(ctx, ct); });
        pipeline.Use(300, async (ctx, next, ct) => { executionOrder.Add("History-before"); var r = await next(ctx, ct); executionOrder.Add("History-after"); return r; });
        pipeline.Use(400, async (ctx, next, ct) => { executionOrder.Add("ContextInjection"); return await next(ctx, ct); });
        pipeline.Use(500, async (ctx, next, ct) => { executionOrder.Add("UsageLogging-before"); var r = await next(ctx, ct); executionOrder.Add("UsageLogging-after"); return r; });
        pipeline.Use(900, async (ctx, next, ct) => { executionOrder.Add("OutputGuardrail-before"); var r = await next(ctx, ct); executionOrder.Add("OutputGuardrail-after"); return r; });

        var built = pipeline.Build((ctx, ct) =>
        {
            executionOrder.Add("Core");
            return Task.FromResult(CreateSuccessResult());
        });

        await built(CreateContext(), CancellationToken.None);

        executionOrder.ShouldBe([
            "Quota-before",
            "InputGuardrail",
            "History-before",
            "ContextInjection",
            "UsageLogging-before",
            "OutputGuardrail-before",
            "Core",
            "OutputGuardrail-after",
            "UsageLogging-after",
            "History-after",
            "Quota-after"
        ]);
    }

    #endregion

    #region Helper Classes

    private static GuardrailRunner CreateGuardrailRunner(bool guardrailsEnabled)
    {
        var options = CreateOptions(o => o.Guardrails.Enabled = guardrailsEnabled);
        return new GuardrailRunner([], [], options, Mock.Of<ILogger<GuardrailRunner>>());
    }

    private static async IAsyncEnumerable<AgentStreamChunk> ToAsyncEnumerable(IEnumerable<AgentStreamChunk> chunks)
    {
        foreach (var chunk in chunks)
        {
            yield return chunk;
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// 跟踪执行顺序的中间件
    /// </summary>
    private sealed class TrackingMiddleware : IAiMiddleware
    {
        private readonly List<string> _executionOrder;
        private readonly string _name;

        public int Order { get; }

        public TrackingMiddleware(int order, List<string> executionOrder, string name)
        {
            Order = order;
            _executionOrder = executionOrder;
            _name = name;
        }

        public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
        {
            _executionOrder.Add($"{_name}-before");
            var result = await next(context, cancellationToken);
            _executionOrder.Add($"{_name}-after");
            return result;
        }
    }

    #endregion
}
