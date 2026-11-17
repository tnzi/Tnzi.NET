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

    private static AiMiddlewareContext CreateContext(string provider = TestProvider, string model = TestModel, Guid? agentId = null, Guid? threadId = null, string? operationType = null, Guid? userId = null)
    {
        return new AiMiddlewareContext
        {
            Request = new AgentRunRequest
            {
                OperationType = operationType,
                UserMessage = "Hello",
                ThreadId = threadId,
                UserId = userId
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
            It.Is<UsageLogEntry>(e =>
                e.OperationType == AIOperationType.Chat &&
                e.Provider == TestProvider &&
                e.Model == TestModel &&
                e.InputTokens == 100 &&
                e.OutputTokens == 50 &&
                e.IsSuccess &&
                e.ErrorMessage == null &&
                e.AgentId == null &&
                e.ThreadId == null &&
                e.CachedInputTokens == 10 &&
                e.CacheCreationTokens == 5),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_MetersTheUsageToTheRequestsUser()
    {
        // 流式收尾可能改在独立作用域重试，那里没有登录用户：用户必须随这一行显式带过去
        var usageLogService = new Mock<IUsageLogService>();
        var middleware = CreateMiddleware(usageLogService);
        var userId = Guid.NewGuid();
        var context = CreateContext(userId: userId);

        await middleware.InvokeAsync(context, (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "ok" }));

        usageLogService.Verify(x => x.LogUsageAsync(It.Is<UsageLogEntry>(e => e.UserId == userId), It.IsAny<CancellationToken>()), Times.Once);
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
            It.Is<UsageLogEntry>(e =>
                e.OperationType == AIOperationType.Chat &&
                e.Provider == TestProvider &&
                e.Model == TestModel &&
                e.InputTokens == 0 &&
                e.OutputTokens == 0 &&
                !e.IsSuccess &&
                e.ErrorMessage == "downstream error" &&
                e.AgentId == null &&
                e.ThreadId == null &&
                e.CachedInputTokens == 0 &&
                e.CacheCreationTokens == 0),
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

        // Assert - 无 Usage 时记录 0
        usageLogService.Verify(x => x.LogUsageAsync(
            It.Is<UsageLogEntry>(e =>
                e.OperationType == AIOperationType.Chat &&
                e.Provider == "Anthropic" &&
                e.Model == "claude-3" &&
                e.InputTokens == 0 &&
                e.OutputTokens == 0 &&
                e.IsSuccess &&
                e.ErrorMessage == null &&
                e.AgentId == null &&
                e.ThreadId == null &&
                e.CachedInputTokens == 0 &&
                e.CacheCreationTokens == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_FailureFinishReason_LogsUsageAsFailure()
    {
        var usageLogService = new Mock<IUsageLogService>();
        var middleware = CreateMiddleware(usageLogService);
        var context = CreateContext();
        var result = new AgentRunResult
        {
            Response = "Blocked by guardrail",
            FinishReason = FinishReasons.GuardrailRejected,
            Usage = new TokenUsageDto { InputTokens = 12, OutputTokens = 3 }
        };

        await middleware.InvokeAsync(context, (ctx, ct) => Task.FromResult(result));

        usageLogService.Verify(x => x.LogUsageAsync(
            It.Is<UsageLogEntry>(e =>
                e.OperationType == AIOperationType.Chat &&
                e.Provider == TestProvider &&
                e.Model == TestModel &&
                e.InputTokens == 12 &&
                e.OutputTokens == 3 &&
                !e.IsSuccess &&
                e.ErrorMessage == "Blocked by guardrail" &&
                e.AgentId == null &&
                e.ThreadId == null &&
                e.CachedInputTokens == 0 &&
                e.CacheCreationTokens == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_UsesActualProviderAndModelFromExecutionContext()
    {
        var usageLogService = new Mock<IUsageLogService>();
        var middleware = CreateMiddleware(usageLogService);
        var context = CreateContext();
        context.EffectiveProvider = "Anthropic";
        context.EffectiveModel = "claude-3-7-sonnet";

        var result = new AgentRunResult
        {
            Response = "ok",
            Model = "claude-3-7-sonnet",
            Usage = new TokenUsageDto { InputTokens = 10, OutputTokens = 4 }
        };

        await middleware.InvokeAsync(context, (ctx, ct) => Task.FromResult(result));

        usageLogService.Verify(x => x.LogUsageAsync(
            It.Is<UsageLogEntry>(e =>
                e.OperationType == AIOperationType.Chat &&
                e.Provider == "Anthropic" &&
                e.Model == "claude-3-7-sonnet" &&
                e.InputTokens == 10 &&
                e.OutputTokens == 4 &&
                e.IsSuccess &&
                e.ErrorMessage == null &&
                e.AgentId == null &&
                e.ThreadId == null &&
                e.CachedInputTokens == 0 &&
                e.CacheCreationTokens == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_AgentRunRequest_LogsAgentRunOperationType()
    {
        var usageLogService = new Mock<IUsageLogService>();
        var middleware = CreateMiddleware(usageLogService);
        var context = CreateContext(agentId: Guid.NewGuid(), operationType: AIOperationType.AgentRun);
        var result = new AgentRunResult
        {
            Response = "ok",
            Usage = new TokenUsageDto { InputTokens = 5, OutputTokens = 2 }
        };

        await middleware.InvokeAsync(context, (ctx, ct) => Task.FromResult(result));

        usageLogService.Verify(x => x.LogUsageAsync(
            It.Is<UsageLogEntry>(e =>
                e.OperationType == AIOperationType.AgentRun &&
                e.Provider == TestProvider &&
                e.Model == TestModel &&
                e.InputTokens == 5 &&
                e.OutputTokens == 2 &&
                e.IsSuccess &&
                e.ErrorMessage == null &&
                e.AgentId == context.Agent.AgentId &&
                e.ThreadId == null &&
                e.CachedInputTokens == 0 &&
                e.CacheCreationTokens == 0),
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
            It.Is<UsageLogEntry>(e =>
                e.OperationType == AIOperationType.ChatStreaming &&
                e.Provider == TestProvider &&
                e.Model == TestModel &&
                e.InputTokens == 80 &&
                e.OutputTokens == 40 &&
                e.IsSuccess &&
                e.ErrorMessage == null &&
                e.AgentId == null &&
                e.ThreadId == null &&
                e.CachedInputTokens == 0 &&
                e.CacheCreationTokens == 0),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task InvokeStreamingAsync_SeveralUsageChunks_LogsTheirSum()
    {
        // 每个 chunk 的 Usage 是一次 LLM 调用的增量：UsageLog 与成本按全部调用之和记，不按最后一次
        var usageLogService = new Mock<IUsageLogService>();
        var middleware = CreateMiddleware(usageLogService);

        var streamChunks = new[]
        {
            new AgentStreamChunk { Usage = new TokenUsageDto { InputTokens = 100, OutputTokens = 10, CachedInputTokens = 40, CacheCreationTokens = 5 } },
            new AgentStreamChunk { Text = "answer" },
            new AgentStreamChunk { FinishReason = FinishReasons.Stop, Usage = new TokenUsageDto { InputTokens = 200, OutputTokens = 20, CachedInputTokens = 60 } }
        };

        await foreach (var _ in middleware.InvokeStreamingAsync(CreateContext(), (ctx, ct) => streamChunks.ToAsyncEnumerable()))
        {
        }

        usageLogService.Verify(x => x.LogUsageAsync(
            It.Is<UsageLogEntry>(e =>
                e.OperationType == AIOperationType.ChatStreaming &&
                e.Provider == TestProvider &&
                e.Model == TestModel &&
                e.InputTokens == 300 &&
                e.OutputTokens == 30 &&
                e.IsSuccess &&
                e.ErrorMessage == null &&
                e.AgentId == null &&
                e.ThreadId == null &&
                e.CachedInputTokens == 100 &&
                e.CacheCreationTokens == 5),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task InvokeStreamingAsync_FailureFinishReason_LogsUsageAsFailure()
    {
        var usageLogService = new Mock<IUsageLogService>();
        var middleware = CreateMiddleware(usageLogService);
        var context = CreateContext();

        var streamChunks = new[]
        {
            new AgentStreamChunk { Text = "partial" },
            new AgentStreamChunk
            {
                FinishReason = FinishReasons.MaxHandoffs,
                Usage = new TokenUsageDto { InputTokens = 21, OutputTokens = 8 }
            }
        };

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in middleware.InvokeStreamingAsync(context, (ctx, ct) => streamChunks.ToAsyncEnumerable()))
        {
            chunks.Add(chunk);
        }

        chunks.Count.ShouldBe(2);
        usageLogService.Verify(x => x.LogUsageAsync(
            It.Is<UsageLogEntry>(e =>
                e.OperationType == AIOperationType.ChatStreaming &&
                e.Provider == TestProvider &&
                e.Model == TestModel &&
                e.InputTokens == 21 &&
                e.OutputTokens == 8 &&
                !e.IsSuccess &&
                e.ErrorMessage == $"AI request finished unsuccessfully: {FinishReasons.MaxHandoffs}" &&
                e.AgentId == null &&
                e.ThreadId == null &&
                e.CachedInputTokens == 0 &&
                e.CacheCreationTokens == 0),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task InvokeStreamingAsync_UsesActualProviderAndChunkModel()
    {
        var usageLogService = new Mock<IUsageLogService>();
        var middleware = CreateMiddleware(usageLogService);
        var context = CreateContext();
        context.EffectiveProvider = "Anthropic";

        var streamChunks = new[]
        {
            new AgentStreamChunk
            {
                Text = "partial",
                Model = "claude-3-7-sonnet"
            },
            new AgentStreamChunk
            {
                FinishReason = FinishReasons.Stop,
                Model = "claude-3-7-sonnet",
                Usage = new TokenUsageDto { InputTokens = 33, OutputTokens = 12 }
            }
        };

        await foreach (var _ in middleware.InvokeStreamingAsync(context, (ctx, ct) => streamChunks.ToAsyncEnumerable()))
        {
        }

        usageLogService.Verify(x => x.LogUsageAsync(
            It.Is<UsageLogEntry>(e =>
                e.OperationType == AIOperationType.ChatStreaming &&
                e.Provider == "Anthropic" &&
                e.Model == "claude-3-7-sonnet" &&
                e.InputTokens == 33 &&
                e.OutputTokens == 12 &&
                e.IsSuccess &&
                e.ErrorMessage == null &&
                e.AgentId == null &&
                e.ThreadId == null &&
                e.CachedInputTokens == 0 &&
                e.CacheCreationTokens == 0),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task InvokeStreamingAsync_AgentRunRequest_LogsAgentRunStreamingOperationType()
    {
        var usageLogService = new Mock<IUsageLogService>();
        var middleware = CreateMiddleware(usageLogService);
        var agentId = Guid.NewGuid();
        var context = CreateContext(agentId: agentId, operationType: AIOperationType.AgentRun);

        var streamChunks = new[]
        {
            new AgentStreamChunk { Text = "partial" },
            new AgentStreamChunk
            {
                FinishReason = FinishReasons.Stop,
                Usage = new TokenUsageDto { InputTokens = 17, OutputTokens = 9 }
            }
        };

        await foreach (var _ in middleware.InvokeStreamingAsync(context, (ctx, ct) => streamChunks.ToAsyncEnumerable()))
        {
        }

        usageLogService.Verify(x => x.LogUsageAsync(
            It.Is<UsageLogEntry>(e =>
                e.OperationType == AIOperationType.AgentRunStreaming &&
                e.Provider == TestProvider &&
                e.Model == TestModel &&
                e.InputTokens == 17 &&
                e.OutputTokens == 9 &&
                e.IsSuccess &&
                e.ErrorMessage == null &&
                e.AgentId == agentId &&
                e.ThreadId == null &&
                e.CachedInputTokens == 0 &&
                e.CacheCreationTokens == 0),
            CancellationToken.None), Times.Once);
    }

    #endregion
}
