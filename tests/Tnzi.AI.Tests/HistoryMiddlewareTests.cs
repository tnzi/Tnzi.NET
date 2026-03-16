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

    #endregion
}
