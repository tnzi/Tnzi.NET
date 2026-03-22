namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// UsageLoggingMiddleware 单元测试
/// </summary>
public class UsageLoggingMiddlewareTests
{
    private const string TestProvider = "OpenAI";
    private const string TestModel = "gpt-4o";

    #region Helper

    private static UsageLoggingMiddleware CreateMiddleware(Mock<IUsageLogService>? usageLogService = null)
    {
        var uls = usageLogService ?? new Mock<IUsageLogService>();
        var scopeFactory = new Mock<IServiceScopeFactory>();

        return new UsageLoggingMiddleware(
            uls.Object,
            scopeFactory.Object,
            Mock.Of<ILogger<UsageLoggingMiddleware>>());
    }

    private static AiMiddlewareContext CreateContext(string provider = TestProvider, string model = TestModel, Guid? agentId = null, Guid? threadId = null)
    {
        return new AiMiddlewareContext
        {
            Request = new AgentRunRequest
            {
                UserMessage = "Hello",
                ThreadId = threadId
            },
            Agent = AgentResolution.Success(
                agent: null!,
                provider: provider,
                model: model,
                agentId: agentId),
            ServiceProvider = new Mock<IServiceProvider>().Object
        };
    }

    #endregion

    #region InvokeAsync

    [Fact]
    public async Task InvokeAsync_SuccessfulCall_LogsUsageWithCorrectTokens()
    {
        // Arrange
        var usageLogService = new Mock<IUsageLogService>();
        var middleware = CreateMiddleware(usageLogService);
        var context = CreateContext();
        var result = new AgentRunResult
        {
            Response = "ok",
            Usage = new TokenUsageDto { InputTokens = 100, OutputTokens = 50, CachedInputTokens = 10, CacheCreationTokens = 5 }
        };

        // Act
        var actual = await middleware.InvokeAsync(context, (ctx, ct) => Task.FromResult(result));

        // Assert
        actual.Response.ShouldBe("ok");
        usageLogService.Verify(x => x.LogUsageAsync(
            AIOperationType.Chat,
            TestProvider,
            TestModel,
            100, 50,
            It.IsAny<long>(),
            true,
            null,
            null, null,
            10, 5,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_NextThrows_LogsUsageWithFailureAndRethrows()
    {
        // Arrange
        var usageLogService = new Mock<IUsageLogService>();
        var middleware = CreateMiddleware(usageLogService);
        var context = CreateContext();

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await middleware.InvokeAsync(context, (ctx, ct) =>
                throw new InvalidOperationException("downstream error"));
        });

        // 失败时 isSuccess=false, errorMessage 有值
        usageLogService.Verify(x => x.LogUsageAsync(
            AIOperationType.Chat,
            TestProvider,
            TestModel,
            0, 0,
            It.IsAny<long>(),
            false,
            "downstream error",
            null, null,
            0, 0,
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_NoUsage_LogsWithZeroTokens()
    {
        // Arrange
        var usageLogService = new Mock<IUsageLogService>();
        var middleware = CreateMiddleware(usageLogService);
        var context = CreateContext(provider: "Anthropic", model: "claude-3");
        var result = new AgentRunResult { Response = "ok", Usage = null };

        // Act
        await middleware.InvokeAsync(context, (ctx, ct) => Task.FromResult(result));

        // Assert — 无 Usage 时记录 0
        usageLogService.Verify(x => x.LogUsageAsync(
            AIOperationType.Chat,
            "Anthropic",
            "claude-3",
            0, 0,
            It.IsAny<long>(),
            true,
            null,
            null, null,
            0, 0,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region InvokeStreamingAsync

    [Fact]
    public async Task InvokeStreamingAsync_CompletesNormally_LogsUsageAfterStreamEnds()
    {
        // Arrange
        var usageLogService = new Mock<IUsageLogService>();
        var middleware = CreateMiddleware(usageLogService);
        var context = CreateContext();

        var streamChunks = new[]
        {
            new AgentStreamChunk { Text = "Hello " },
            new AgentStreamChunk { Text = "World", Usage = new TokenUsageDto { InputTokens = 80, OutputTokens = 40 } }
        };

        // Act
        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in middleware.InvokeStreamingAsync(context, (ctx, ct) => streamChunks.ToAsyncEnumerable()))
        {
            chunks.Add(chunk);
        }

        // Assert
        chunks.Count.ShouldBe(2);
        // 流式完成后记录最后一个 Usage 的值
        usageLogService.Verify(x => x.LogUsageAsync(
            AIOperationType.Chat,
            TestProvider,
            TestModel,
            80, 40,
            It.IsAny<long>(),
            true,
            null,
            null, null,
            0, 0,
            CancellationToken.None), Times.Once);
    }

    #endregion
}
