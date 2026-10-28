namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// QuotaMiddleware 单元测试：预算门 + 按用户的用量上限（一轮开始前开窗口判上限，跑完按实际费用结算）。
/// </summary>
public class QuotaMiddlewareTests
{
    private const string TestProvider = "OpenAI";
    private const string TestModel = "gpt-4o";

    #region Helper

    private static QuotaMiddleware CreateMiddleware(
        Mock<IUsageLimitService>? usageLimits = null,
        Mock<IBudgetService>? budgetService = null,
        Mock<ICostCalculator>? costCalculator = null)
    {
        var limits = usageLimits ?? UsageLimitMocks.Allowing();
        var bs = budgetService ?? new Mock<IBudgetService>();

        // 默认预算检查通过
        if (budgetService == null)
        {
            bs.Setup(x => x.CheckBudgetAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BudgetCheckResult { IsAllowed = true, Status = BudgetStatus.WithinBudget });
        }

        return new QuotaMiddleware(
            limits.Object,
            bs.Object,
            Mock.Of<ILogger<QuotaMiddleware>>(),
            costCalculator?.Object);
    }

    /// <summary>按 (输入, 输出, 缓存读, 缓存写) 每百万 token 各 1 美元计价的计算器，便于断言结算金额。</summary>
    private static Mock<ICostCalculator> FlatRateCalculator()
    {
        var calculator = new Mock<ICostCalculator>();
        calculator
            .Setup(c => c.CalculateCost(TestProvider, It.IsAny<string>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<long>()))
            .Returns((string _, string _, long input, long output, long _, long _) => (input + output) / 1_000_000m);
        return calculator;
    }

    private static AiMiddlewareContext CreateContext(Guid? userId = null, string? userMessage = "Hello")
    {
        return new AiMiddlewareContext
        {
            Request = new AgentRunRequest
            {
                UserMessage = userMessage,
                UserId = userId
            },
            Agent = AgentResolution.Success(
                agent: null!,
                provider: TestProvider,
                model: TestModel,
                agentId: null),
            ServiceProvider = new Mock<IServiceProvider>().Object
        };
    }

    private static AgentRunResult CreateSuccessResult(int inputTokens = 50, int outputTokens = 30)
    {
        return new AgentRunResult
        {
            Response = "ok",
            Usage = new TokenUsageDto { InputTokens = inputTokens, OutputTokens = outputTokens }
        };
    }

    #endregion

    #region InvokeAsync

    [Fact]
    public async Task InvokeAsync_UnderTheLimit_PassesThrough_AndSettlesTheTurnsCost()
    {
        var userId = Guid.NewGuid();
        var limits = UsageLimitMocks.Allowing();
        var middleware = CreateMiddleware(limits, costCalculator: FlatRateCalculator());

        var result = await middleware.InvokeAsync(CreateContext(userId), (_, _) => Task.FromResult(CreateSuccessResult(1_000_000, 500_000)));

        result.Response.ShouldBe("ok");
        limits.Verify(x => x.BeginAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
        limits.Verify(x => x.SettleAsync(It.Is<UsageLimitReservation>(r => r.UserId == userId), 1.5m, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_CostIncludesCacheReadsAndWrites()
    {
        // API 等价费用含缓存读写：缓存的 token 各按各的价目进到计算器里，不是只算输入 + 输出
        var userId = Guid.NewGuid();
        var calculator = new Mock<ICostCalculator>();
        calculator.Setup(c => c.CalculateCost(TestProvider, TestModel, 1200L, 80L, 900L, 200L)).Returns(0.0123m);
        var limits = UsageLimitMocks.Allowing();
        var middleware = CreateMiddleware(limits, costCalculator: calculator);
        var result = new AgentRunResult
        {
            Response = "ok",
            Usage = new TokenUsageDto { InputTokens = 1200, OutputTokens = 80, CachedInputTokens = 900, CacheCreationTokens = 200 }
        };

        await middleware.InvokeAsync(CreateContext(userId), (_, _) => Task.FromResult(result));

        limits.Verify(x => x.SettleAsync(It.IsAny<UsageLimitReservation>(), 0.0123m, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_ModelWithoutACostRate_SettlesZero_ButStillSettles()
    {
        // 量不出成本的一轮记 0：照样结算（窗口与「最后使用」照常更新），只是上限对它拦不住
        var userId = Guid.NewGuid();
        var limits = UsageLimitMocks.Allowing();
        var middleware = CreateMiddleware(limits, costCalculator: new Mock<ICostCalculator>());

        await middleware.InvokeAsync(CreateContext(userId), (_, _) => Task.FromResult(CreateSuccessResult()));

        limits.Verify(x => x.SettleAsync(It.IsAny<UsageLimitReservation>(), 0m, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_LimitReached_DoesNotRun_AndReturnsTheWindowAndResetTime()
    {
        var userId = Guid.NewGuid();
        var resetsAt = new DateTime(2026, 10, 22, 3, 0, 0, DateTimeKind.Utc);
        var limits = UsageLimitMocks.Allowing();
        limits.Setup(x => x.BeginAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UsageLimitMocks.Denial(UsageLimitWindow.Session, resetsAt));
        var middleware = CreateMiddleware(limits);
        var called = false;

        var result = await middleware.InvokeAsync(CreateContext(userId), (_, _) =>
        {
            called = true;
            return Task.FromResult(CreateSuccessResult());
        });

        called.ShouldBeFalse();
        result.FinishReason.ShouldBe(FinishReasons.QuotaExceeded);
        result.ErrorCode.ShouldBe(ErrorCodes.UsageLimitReached);
        result.UsageLimitWindow.ShouldBe(UsageLimitWindow.Session);
        result.UsageLimitResetsAt.ShouldBe(resetsAt);
        limits.Verify(x => x.SettleAsync(It.IsAny<UsageLimitReservation>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_ReplacedServiceAnswers429WithoutACode_IsStillAUsageLimit()
    {
        // IUsageLimitService 是可替换契约：自定义实现按 HTTP 语义返回 429 而不带框架错误码，照样判为用量上限
        var userId = Guid.NewGuid();
        var limits = UsageLimitMocks.Allowing();
        limits.Setup(x => x.BeginAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UsageLimitReservation>.Failure("Limit reached", 429));
        var middleware = CreateMiddleware(limits);

        var result = await middleware.InvokeAsync(CreateContext(userId), (_, _) => Task.FromResult(CreateSuccessResult()));

        result.FinishReason.ShouldBe(FinishReasons.QuotaExceeded);
        result.ErrorCode.ShouldBe(ErrorCodes.UsageLimitReached);
    }

    [Fact]
    public async Task InvokeAsync_MeterFailure_IsAnError_NotAUsageLimit()
    {
        // 计量子系统自身失败不是「到上限了」：不跑（失败关闭），结束原因是 error，错误码原样带出
        var userId = Guid.NewGuid();
        var limits = UsageLimitMocks.Allowing();
        limits.Setup(x => x.BeginAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UsageLimitReservation>.Failure("The usage meter is unavailable.", 500, ErrorCodes.UsageLimitCheckFailed));
        var middleware = CreateMiddleware(limits);

        var result = await middleware.InvokeAsync(CreateContext(userId), (_, _) => Task.FromResult(CreateSuccessResult()));

        result.FinishReason.ShouldBe(FinishReasons.Error);
        result.ErrorCode.ShouldBe(ErrorCodes.UsageLimitCheckFailed);
    }

    [Fact]
    public async Task InvokeAsync_BudgetExceeded_ShortCircuits_BeforeAnyWindowIsOpened()
    {
        var userId = Guid.NewGuid();
        var limits = UsageLimitMocks.Allowing();
        var budget = new Mock<IBudgetService>();
        budget.Setup(x => x.CheckBudgetAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BudgetCheckResult { IsAllowed = false, Status = BudgetStatus.BudgetExceeded, Reason = "Monthly budget exceeded" });
        var middleware = CreateMiddleware(limits, budget);

        var result = await middleware.InvokeAsync(CreateContext(userId), (_, _) => Task.FromResult(CreateSuccessResult()));

        result.FinishReason.ShouldBe(FinishReasons.QuotaExceeded);
        result.Response.ShouldBe("Monthly budget exceeded");
        limits.Verify(x => x.BeginAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_NoUser_SkipsBothGates()
    {
        var limits = UsageLimitMocks.Allowing();
        var middleware = CreateMiddleware(limits);

        var result = await middleware.InvokeAsync(CreateContext(userId: null), (_, _) => Task.FromResult(CreateSuccessResult()));

        result.Response.ShouldBe("ok");
        limits.Verify(x => x.BeginAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_DownstreamThrows_StillSettles()
    {
        var userId = Guid.NewGuid();
        var limits = UsageLimitMocks.Allowing();
        var middleware = CreateMiddleware(limits);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            middleware.InvokeAsync(CreateContext(userId), (_, _) => throw new InvalidOperationException("boom")));

        limits.Verify(x => x.SettleAsync(It.Is<UsageLimitReservation>(r => r.UserId == userId), 0m, CancellationToken.None), Times.Once);
    }

    #endregion

    #region InvokeStreamingAsync

    [Fact]
    public async Task InvokeStreamingAsync_LimitReached_YieldsOneErrorChunk_WithTheWindow_AndNoText()
    {
        var userId = Guid.NewGuid();
        var limits = UsageLimitMocks.Allowing();
        limits.Setup(x => x.BeginAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UsageLimitMocks.Denial(UsageLimitWindow.Weekly));
        var middleware = CreateMiddleware(limits);

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in middleware.InvokeStreamingAsync(CreateContext(userId),
                           (_, _) => new[] { new AgentStreamChunk { Text = "never" } }.ToAsyncEnumerable()))
        {
            chunks.Add(chunk);
        }

        var only = chunks.ShouldHaveSingleItem();
        only.Text.ShouldBeNull();
        only.Error.ShouldNotBeNullOrWhiteSpace();
        only.ErrorCode.ShouldBe(ErrorCodes.UsageLimitReached);
        only.FinishReason.ShouldBe(FinishReasons.QuotaExceeded);
        only.UsageLimitWindow.ShouldBe(UsageLimitWindow.Weekly);
        only.UsageLimitResetsAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task InvokeStreamingAsync_SeveralUsageChunks_SettlesTheCostOfTheirSum()
    {
        // 每个 chunk 的 Usage 是一次 LLM 调用的增量：工具循环里有几次调用就有几个，结算的是总和而不是最后一个
        var userId = Guid.NewGuid();
        var limits = UsageLimitMocks.Allowing();
        var middleware = CreateMiddleware(limits, costCalculator: FlatRateCalculator());
        var upstream = new[]
        {
            new AgentStreamChunk { Usage = new TokenUsageDto { InputTokens = 100_000, OutputTokens = 10_000 } },
            new AgentStreamChunk { IsToolCall = true },
            new AgentStreamChunk { Usage = new TokenUsageDto { InputTokens = 200_000, OutputTokens = 20_000 } },
            new AgentStreamChunk { Text = "done", FinishReason = FinishReasons.Stop, Usage = new TokenUsageDto { InputTokens = 300_000, OutputTokens = 30_000 } }
        };

        await foreach (var _ in middleware.InvokeStreamingAsync(CreateContext(userId), (_, _) => upstream.ToAsyncEnumerable()))
        {
        }

        limits.Verify(x => x.SettleAsync(It.IsAny<UsageLimitReservation>(), 0.66m, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task InvokeStreamingAsync_CallerStopsEarly_SettlesWhatWasUsedSoFar()
    {
        var userId = Guid.NewGuid();
        var limits = UsageLimitMocks.Allowing();
        var middleware = CreateMiddleware(limits, costCalculator: FlatRateCalculator());
        var upstream = new[]
        {
            new AgentStreamChunk { Text = "part", Usage = new TokenUsageDto { InputTokens = 400_000, OutputTokens = 100_000 } },
            new AgentStreamChunk { Text = "never read" }
        };

        await foreach (var _ in middleware.InvokeStreamingAsync(CreateContext(userId), (_, _) => upstream.ToAsyncEnumerable()))
        {
            break;
        }

        limits.Verify(x => x.SettleAsync(It.IsAny<UsageLimitReservation>(), 0.5m, CancellationToken.None), Times.Once);
    }

    #endregion
}
