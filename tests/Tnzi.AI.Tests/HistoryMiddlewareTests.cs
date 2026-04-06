namespace Tnzi.AI.Tests;

/// <summary>
/// HistoryMiddleware 单元测试 — 验证 limit 参数正确传递
/// </summary>
public class HistoryMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_PassesMaxLoadedMessagesToGetMessageHistory()
    {
        // Arrange
        const int maxLoaded = 100;
        var threadId = Guid.NewGuid();
        int? capturedLimit = null;

        var mockThreadService = new Mock<IAgentThreadInternalService>();
        mockThreadService
            .Setup(s => s.GetMessageHistoryAsync(threadId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, int?, CancellationToken>((_, limit, _) => capturedLimit = limit)
            .ReturnsAsync(new List<ChatMessage>());

        var options = CreateOptions(maxLoaded);
        var middleware = new HistoryMiddleware(mockThreadService.Object, options, Mock.Of<ILogger<HistoryMiddleware>>());

        var context = CreateContext(threadId);

        AiMiddlewareDelegate next = (_, ct) =>
            Task.FromResult(new AgentRunResult { Response = "ok" });

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        capturedLimit.ShouldBe(maxLoaded);
    }

    [Fact]
    public async Task InvokeAsync_DefaultMaxLoadedMessages_Is100()
    {
        // Arrange
        var threadId = Guid.NewGuid();
        int? capturedLimit = null;

        var mockThreadService = new Mock<IAgentThreadInternalService>();
        mockThreadService
            .Setup(s => s.GetMessageHistoryAsync(threadId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, int?, CancellationToken>((_, limit, _) => capturedLimit = limit)
            .ReturnsAsync(new List<ChatMessage>());

        // 使用默认配置
        var options = CreateOptions(maxLoadedMessages: null, useDefault: true);
        var middleware = new HistoryMiddleware(mockThreadService.Object, options, Mock.Of<ILogger<HistoryMiddleware>>());

        var context = CreateContext(threadId);

        AiMiddlewareDelegate next = (_, ct) =>
            Task.FromResult(new AgentRunResult { Response = "ok" });

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert — 默认值应为 100
        capturedLimit.ShouldBe(100);
    }

    [Fact]
    public async Task InvokeAsync_NullMaxLoadedMessages_PassesNull()
    {
        // Arrange
        var threadId = Guid.NewGuid();
        int? capturedLimit = -1; // sentinel

        var mockThreadService = new Mock<IAgentThreadInternalService>();
        mockThreadService
            .Setup(s => s.GetMessageHistoryAsync(threadId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, int?, CancellationToken>((_, limit, _) => capturedLimit = limit)
            .ReturnsAsync(new List<ChatMessage>());

        // 显式设为 null（不限制）
        var options = CreateOptions(maxLoadedMessages: null, useDefault: false);
        var middleware = new HistoryMiddleware(mockThreadService.Object, options, Mock.Of<ILogger<HistoryMiddleware>>());

        var context = CreateContext(threadId);

        AiMiddlewareDelegate next = (_, ct) =>
            Task.FromResult(new AgentRunResult { Response = "ok" });

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        capturedLimit.ShouldBeNull();
    }

    [Fact]
    public async Task InvokeAsync_QuotaExceeded_DoesNotPersistMessages()
    {
        var threadId = Guid.NewGuid();
        var mockThreadService = new Mock<IAgentThreadInternalService>();
        mockThreadService
            .Setup(s => s.GetMessageHistoryAsync(threadId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChatMessage>());

        var middleware = new HistoryMiddleware(
            mockThreadService.Object,
            CreateOptions(maxLoadedMessages: 100),
            Mock.Of<ILogger<HistoryMiddleware>>());

        var context = CreateContext(threadId);

        _ = await middleware.InvokeAsync(context, (_, _) => Task.FromResult(new AgentRunResult
        {
            Response = "Daily quota exceeded",
            FinishReason = FinishReasons.QuotaExceeded
        }));

        mockThreadService.Verify(
            s => s.SaveMessageAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task InvokeStreamingAsync_QuotaExceeded_DoesNotPersistMessages()
    {
        var threadId = Guid.NewGuid();
        var mockThreadService = new Mock<IAgentThreadInternalService>();
        mockThreadService
            .Setup(s => s.GetMessageHistoryAsync(threadId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChatMessage>());

        var middleware = new HistoryMiddleware(
            mockThreadService.Object,
            CreateOptions(maxLoadedMessages: 100),
            Mock.Of<ILogger<HistoryMiddleware>>());

        var context = CreateContext(threadId);

        await foreach (var _ in middleware.InvokeStreamingAsync(
            context,
            (_, _) => CreateQuotaExceededStream(),
            CancellationToken.None))
        {
        }

        mockThreadService.Verify(
            s => s.SaveMessageAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task InvokeStreamingAsync_TruncatesLargeToolResultsInHistory()
    {
        var threadId = Guid.NewGuid();
        var longToolResult = new string('x', 40);
        var history = new List<ChatMessage>
        {
            new(ChatRole.Tool, [new FunctionResultContent("call-1", longToolResult)])
        };

        var mockThreadService = new Mock<IAgentThreadInternalService>();
        mockThreadService
            .Setup(s => s.GetMessageHistoryAsync(threadId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(history);

        var aiOptions = new AIOptions();
        aiOptions.History.Store.MaxLoadedMessages = 100;
        aiOptions.ToolResultBudget.Enabled = true;
        aiOptions.ToolResultBudget.MaxResultChars = 10;
        aiOptions.ToolResultBudget.PreviewChars = 5;

        var middleware = new HistoryMiddleware(
            mockThreadService.Object,
            Microsoft.Extensions.Options.Options.Create(aiOptions),
            Mock.Of<ILogger<HistoryMiddleware>>());

        var context = CreateContext(threadId);

        await foreach (var _ in middleware.InvokeStreamingAsync(
            context,
            (_, _) => CreateCompletedStream(),
            CancellationToken.None))
        {
        }

        var toolMessage = context.Messages.Single();
        var toolResult = toolMessage.Contents.OfType<FunctionResultContent>().Single();
        toolResult.Result.ShouldBe("xxxxx\n\n[truncated: original 40 chars]");
    }

    [Fact]
    public async Task InvokeAsync_Failed_DoesNotPersistMessages()
    {
        var threadId = Guid.NewGuid();
        var mockThreadService = new Mock<IAgentThreadInternalService>();
        mockThreadService
            .Setup(s => s.GetMessageHistoryAsync(threadId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChatMessage>());

        var middleware = new HistoryMiddleware(
            mockThreadService.Object,
            CreateOptions(maxLoadedMessages: 100),
            Mock.Of<ILogger<HistoryMiddleware>>());

        var context = CreateContext(threadId);

        _ = await middleware.InvokeAsync(context, (_, _) => Task.FromResult(new AgentRunResult
        {
            Response = "Tool execution failed",
            FinishReason = FinishReasons.Failed
        }));

        mockThreadService.Verify(
            s => s.SaveMessageAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task InvokeStreamingAsync_MaxHandoffs_DoesNotPersistMessages()
    {
        var threadId = Guid.NewGuid();
        var mockThreadService = new Mock<IAgentThreadInternalService>();
        mockThreadService
            .Setup(s => s.GetMessageHistoryAsync(threadId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChatMessage>());

        var middleware = new HistoryMiddleware(
            mockThreadService.Object,
            CreateOptions(maxLoadedMessages: 100),
            Mock.Of<ILogger<HistoryMiddleware>>());

        var context = CreateContext(threadId);

        await foreach (var _ in middleware.InvokeStreamingAsync(
            context,
            (_, _) => CreateMaxHandoffsStream(),
            CancellationToken.None))
        {
        }

        mockThreadService.Verify(
            s => s.SaveMessageAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #region Helpers

    private static IOptions<AIOptions> CreateOptions(int? maxLoadedMessages, bool useDefault = false)
    {
        var aiOptions = new AIOptions();
        if (useDefault)
        {
            // 使用默认值（MaxLoadedMessages = 100）
        }
        else
        {
            aiOptions.History.Store.MaxLoadedMessages = maxLoadedMessages;
        }
        return Microsoft.Extensions.Options.Options.Create(aiOptions);
    }

    private static AiMiddlewareContext CreateContext(Guid threadId)
    {
        return new AiMiddlewareContext
        {
            Request = new AgentRunRequest
            {
                ThreadId = threadId,
                UserMessage = "test"
            },
            Agent = new AgentResolution
            {
                Provider = "test",
                Model = "test-model"
            },
            ServiceProvider = new Mock<IServiceProvider>().Object
        };
    }

    private static async IAsyncEnumerable<AgentStreamChunk> CreateQuotaExceededStream()
    {
        await Task.Yield();
        yield return new AgentStreamChunk
        {
            Text = "Daily quota exceeded",
            FinishReason = FinishReasons.QuotaExceeded
        };
    }

    private static async IAsyncEnumerable<AgentStreamChunk> CreateCompletedStream()
    {
        await Task.Yield();
        yield return new AgentStreamChunk
        {
            Text = "ok",
            FinishReason = FinishReasons.Stop
        };
    }

    private static async IAsyncEnumerable<AgentStreamChunk> CreateMaxHandoffsStream()
    {
        await Task.Yield();
        yield return new AgentStreamChunk
        {
            Text = "Max handoff limit reached",
            FinishReason = FinishReasons.MaxHandoffs
        };
    }

    #endregion
}
