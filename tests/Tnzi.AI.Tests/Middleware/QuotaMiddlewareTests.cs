using Tnzi.EventBus;

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
        Mock<IEventBus>? eventBus = null)
    {
        var qs = quotaService ?? new Mock<IQuotaService>();
        var te = tokenEstimator ?? new Mock<ITokenEstimator>();

        // 默认 token 估算返回 100
        if (tokenEstimator == null)
        {
            te.Setup(x => x.Estimate(It.IsAny<string>(), It.IsAny<int>())).Returns(100);
        }

        return new QuotaMiddleware(
            qs.Object,
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
        quotaService.Setup(x => x.ReserveQuotaAsync(userId, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<QuotaReservation>("Daily quota exceeded"));

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
            .ReturnsAsync(Result.Failure<QuotaReservation>("Monthly quota exceeded"));

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
