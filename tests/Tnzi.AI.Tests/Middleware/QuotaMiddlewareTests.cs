namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// QuotaMiddleware 单元测试
/// </summary>
public class QuotaMiddlewareTests
{
    private const string TestProvider = "OpenAI";
    private const string TestModel = "gpt-4o";

    #region Helper

    private static QuotaMiddleware CreateMiddleware(
        Mock<IQuotaService>? quotaService = null,
        Mock<ITokenEstimator>? tokenEstimator = null,
        Mock<IEventBus>? eventBus = null,
        Mock<IBudgetService>? budgetService = null)
    {
        var qs = quotaService ?? new Mock<IQuotaService>();
        var te = tokenEstimator ?? new Mock<ITokenEstimator>();
        var bs = budgetService ?? new Mock<IBudgetService>();

        // 默认 token 估算返回 100
        if (tokenEstimator == null)
        {
            te.Setup(x => x.Estimate(It.IsAny<string>(), It.IsAny<int>())).Returns(100);
        }

        // 默认预算检查通过
        if (budgetService == null)
        {
            bs.Setup(x => x.CheckBudgetAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BudgetCheckResult { IsAllowed = true, Status = BudgetStatus.WithinBudget });
        }

        return new QuotaMiddleware(
            qs.Object,
            bs.Object,
            te.Object,
            Mock.Of<ILogger<QuotaMiddleware>>(),
            eventBus?.Object);
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
    public async Task InvokeAsync_QuotaAvailable_PassesThroughAndSettles()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var reservation = new QuotaReservation { ReservedTokens = 100, ReservedAt = DateTime.UtcNow };
        var quotaService = new Mock<IQuotaService>();
        quotaService.Setup(x => x.ReserveQuotaAsync(userId, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(reservation));
        quotaService.Setup(x => x.SettleQuotaAsync(userId, reservation, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var middleware = CreateMiddleware(quotaService: quotaService);
        var context = CreateContext(userId);
        var expectedResult = CreateSuccessResult();

        // Act
        var result = await middleware.InvokeAsync(context, (ctx, ct) => Task.FromResult(expectedResult));

        // Assert
        result.Response.ShouldBe("ok");
        quotaService.Verify(x => x.ReserveQuotaAsync(userId, It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
        quotaService.Verify(x => x.SettleQuotaAsync(userId, reservation, 80, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_QuotaExceeded_ReturnsQuotaExceededResult()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var quotaService = new Mock<IQuotaService>();
        // 错误码是「是否真的超限」的判据（见 QuotaMiddleware.IsQuotaExceeded），
        // 真实服务始终带码，测试也照真实形态给
        quotaService.Setup(x => x.ReserveQuotaAsync(userId, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<QuotaReservation>("Daily quota exceeded", 429, ErrorCodes.QuotaExceeded));

        var middleware = CreateMiddleware(quotaService: quotaService);
        var context = CreateContext(userId);
        var nextCalled = false;

        // Act
        var result = await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            nextCalled = true;
            return Task.FromResult(CreateSuccessResult());
        });

        // Assert
        nextCalled.ShouldBeFalse();
        result.FinishReason.ShouldBe(FinishReasons.QuotaExceeded);
        result.Response.ShouldBe("Daily quota exceeded");
    }

    [Theory]
    [InlineData(ErrorCodes.QuotaCheckFailed)]
    [InlineData(ErrorCodes.QuotaConcurrencyConflict)]
    public async Task InvokeAsync_ReservationFailsForNonQuotaReason_DoesNotReportQuotaExceeded(string errorCode)
    {
        // 配额记录缺失 / 并发冲突都不是「配额用完了」。贴成 quota_exceeded 会把排查方向
        // 一路带到配额配置上去，而那里什么问题都没有。
        var userId = Guid.NewGuid();
        var quotaService = new Mock<IQuotaService>();
        quotaService.Setup(x => x.ReserveQuotaAsync(userId, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<QuotaReservation>("Quota record is unavailable.", 500, errorCode));

        var eventBus = new Mock<IEventBus>();
        var middleware = CreateMiddleware(quotaService: quotaService, eventBus: eventBus);
        var context = CreateContext(userId);
        var nextCalled = false;

        var result = await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            nextCalled = true;
            return Task.FromResult(CreateSuccessResult());
        });

        nextCalled.ShouldBeFalse();
        result.FinishReason.ShouldBe(FinishReasons.Error);
        result.FinishReason.ShouldNotBe(FinishReasons.QuotaExceeded);
        result.Response.ShouldBe("Quota record is unavailable.");

        // 服务层错误码必须带上结果：HTTP 边界靠它把并发冲突还原成可重试的 409
        result.ErrorCode.ShouldBe(errorCode);

        // QuotaExceededEvent 喂告警与用量分析，混进基础设施故障会让超限统计失真
        eventBus.Verify(
            b => b.PublishAsync(It.IsAny<QuotaExceededEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_ReplaceableService429WithoutErrorCode_TreatsAsQuotaExceeded()
    {
        // IQuotaService 是可替换契约：自定义实现按 HTTP 语义返回 429 而不带框架错误码，
        // 完全合法。只认框架常量会把这类超限贴成通用错误——429 丢了，告警事件也不发。
        var userId = Guid.NewGuid();
        var quotaService = new Mock<IQuotaService>();
        quotaService.Setup(x => x.ReserveQuotaAsync(userId, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<QuotaReservation>("Quota exceeded", 429));

        var eventBus = new Mock<IEventBus>();
        var middleware = CreateMiddleware(quotaService: quotaService, eventBus: eventBus);
        var context = CreateContext(userId);
        var nextCalled = false;

        var result = await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            nextCalled = true;
            return Task.FromResult(CreateSuccessResult());
        });

        nextCalled.ShouldBeFalse();
        result.FinishReason.ShouldBe(FinishReasons.QuotaExceeded);
        result.ErrorCode.ShouldBe(ErrorCodes.QuotaExceeded);

        eventBus.Verify(
            b => b.PublishAsync(It.IsAny<QuotaExceededEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokeStreamingAsync_ReservationFailsForNonQuotaReason_DoesNotReportQuotaExceeded()
    {
        var userId = Guid.NewGuid();
        var quotaService = new Mock<IQuotaService>();
        quotaService.Setup(x => x.ReserveQuotaAsync(userId, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<QuotaReservation>(
                "Quota record is unavailable.", 500, ErrorCodes.QuotaCheckFailed));

        var eventBus = new Mock<IEventBus>();
        var middleware = CreateMiddleware(quotaService: quotaService, eventBus: eventBus);
        var context = CreateContext(userId);

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in middleware.InvokeStreamingAsync(context, (ctx, ct) => AsyncEnumerable.Empty<AgentStreamChunk>()))
        {
            chunks.Add(chunk);
        }

        chunks.Count.ShouldBe(1);
        chunks[0].FinishReason.ShouldBe(FinishReasons.Error);
        chunks[0].FinishReason.ShouldNotBe(FinishReasons.QuotaExceeded);

        eventBus.Verify(
            b => b.PublishAsync(It.IsAny<QuotaExceededEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_NoUserId_SkipsQuotaCheckAndPassesThrough()
    {
        // Arrange
        var quotaService = new Mock<IQuotaService>();
        var middleware = CreateMiddleware(quotaService: quotaService);
        var context = CreateContext(userId: null);
        var expectedResult = CreateSuccessResult();

        // Act
        var result = await middleware.InvokeAsync(context, (ctx, ct) => Task.FromResult(expectedResult));

        // Assert
        result.Response.ShouldBe("ok");
        quotaService.Verify(x => x.ReserveQuotaAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_NextThrows_SettlesWithEstimatedTokensAndRethrows()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var reservation = new QuotaReservation { ReservedTokens = 100, ReservedAt = DateTime.UtcNow };
        var quotaService = new Mock<IQuotaService>();
        quotaService.Setup(x => x.ReserveQuotaAsync(userId, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(reservation));
        quotaService.Setup(x => x.SettleQuotaAsync(userId, reservation, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var tokenEstimator = new Mock<ITokenEstimator>();
        tokenEstimator.Setup(x => x.Estimate(It.IsAny<string>(), It.IsAny<int>())).Returns(200);

        var middleware = CreateMiddleware(quotaService: quotaService, tokenEstimator: tokenEstimator);
        var context = CreateContext(userId);

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await middleware.InvokeAsync(context, (ctx, ct) =>
                throw new InvalidOperationException("downstream error"));
        });

        // 异常时使用预估值结算
        quotaService.Verify(x => x.SettleQuotaAsync(userId, reservation, 200, CancellationToken.None), Times.Once);
    }

    #endregion

    #region InvokeStreamingAsync

    [Fact]
    public async Task InvokeStreamingAsync_QuotaExceeded_YieldsQuotaExceededChunk()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var quotaService = new Mock<IQuotaService>();
        quotaService.Setup(x => x.ReserveQuotaAsync(userId, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<QuotaReservation>("Monthly quota exceeded", 429, ErrorCodes.QuotaExceeded));

        var middleware = CreateMiddleware(quotaService: quotaService);
        var context = CreateContext(userId);

        // Act
        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in middleware.InvokeStreamingAsync(context, (ctx, ct) => AsyncEnumerable.Empty<AgentStreamChunk>()))
        {
            chunks.Add(chunk);
        }

        // Assert
        chunks.Count.ShouldBe(1);
        chunks[0].FinishReason.ShouldBe(FinishReasons.QuotaExceeded);
        chunks[0].Text.ShouldBe("Monthly quota exceeded");
    }

    #endregion
}
