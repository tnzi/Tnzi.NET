namespace Tnzi.AI.Tests;

/// <summary>
/// HistoryMiddleware 单元测试 - 验证 limit 参数正确传递
/// </summary>
public class HistoryMiddlewareTests
{
    /// <summary>
    /// 客户端给了 ThreadId 时，<b>必须</b>仍然经 GetOrCreateThreadAsync 做归属校验。
    /// </summary>
    /// <remarks>
    /// EnsureThreadAsync 此前是 <c>if (context.Request.ThreadId != null) return;</c> ——
    /// 归属校验的唯一所在地就是 GetOrCreateThreadAsync（比对 CreatorId，不匹配一律 404），
    /// 早退等于「客户端给了 id 就不查了」：拿到他人 threadId 即可读出其全部历史，
    /// 并把本轮问答写进他人的线程。ThreadId 直接来自请求体，是完全可控的对象引用。
    /// </remarks>
    [Fact]
    public async Task InvokeAsync_ClientSuppliedThreadId_StillGoesThroughOwnershipCheck()
    {
        var threadId = Guid.NewGuid();

        var mockThreadService = new Mock<IAgentThreadInternalService>();
        mockThreadService
            .Setup(s => s.GetOrCreateThreadAsync(threadId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((default(ConversationContext)!, threadId, false));
        mockThreadService
            .Setup(s => s.GetMessageHistoryAsync(threadId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChatMessage>());

        var middleware = new HistoryMiddleware(mockThreadService.Object, CreateOptions(100), Mock.Of<ILogger<HistoryMiddleware>>());

        await middleware.InvokeAsync(CreateContext(threadId),
            (_, _) => Task.FromResult(new AgentRunResult { Response = "ok" }));

        mockThreadService.Verify(
            s => s.GetOrCreateThreadAsync(threadId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "客户端提供的 ThreadId 必须经过归属校验，不能因为「已有值」就跳过");
    }

    /// <summary>归属校验失败（他人的线程）必须中断整条管线，而不是被吞掉继续跑。</summary>
    [Fact]
    public async Task InvokeAsync_ThreadOwnershipRejected_PropagatesAndDoesNotRunNext()
    {
        var foreignThreadId = Guid.NewGuid();
        var nextRan = false;

        var mockThreadService = new Mock<IAgentThreadInternalService>();
        mockThreadService
            .Setup(s => s.GetOrCreateThreadAsync(foreignThreadId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Tnzi.Exceptions.BusinessException("Thread not found", "AI_THREAD_NOT_FOUND", 404));

        var middleware = new HistoryMiddleware(mockThreadService.Object, CreateOptions(100), Mock.Of<ILogger<HistoryMiddleware>>());

        await Should.ThrowAsync<Tnzi.Exceptions.BusinessException>(async () =>
            await middleware.InvokeAsync(CreateContext(foreignThreadId),
                (_, _) =>
                {
                    nextRan = true;
                    return Task.FromResult(new AgentRunResult { Response = "ok" });
                }));

        nextRan.ShouldBeFalse("归属校验失败时不得继续执行管线 —— 否则等于放行一个未经校验的 threadId");
    }

    [Fact]
    public async Task InvokeAsync_PassesMaxLoadedMessagesToGetMessageHistory()
    {
        // Arrange
        const int maxLoaded = 100;
        var threadId = Guid.NewGuid();
        int? capturedLimit = null;

        var mockThreadService = new Mock<IAgentThreadInternalService>();
        // 线程存在且归当前用户所有：EnsureThreadAsync 现在**总是**经 GetOrCreateThreadAsync
        // 做归属校验（早退曾让客户端给的 threadId 完全绕过它），故必须显式建模这一步。
        mockThreadService
            .Setup(s => s.GetOrCreateThreadAsync(threadId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((default(ConversationContext)!, threadId, false));
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
        // 线程存在且归当前用户所有：EnsureThreadAsync 现在**总是**经 GetOrCreateThreadAsync
        // 做归属校验（早退曾让客户端给的 threadId 完全绕过它），故必须显式建模这一步。
        mockThreadService
            .Setup(s => s.GetOrCreateThreadAsync(threadId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((default(ConversationContext)!, threadId, false));
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

        // Assert - 默认值应为 100
        capturedLimit.ShouldBe(100);
    }

    [Fact]
    public async Task InvokeAsync_NullMaxLoadedMessages_PassesNull()
    {
        // Arrange
        var threadId = Guid.NewGuid();
        int? capturedLimit = -1; // sentinel

        var mockThreadService = new Mock<IAgentThreadInternalService>();
        // 线程存在且归当前用户所有：EnsureThreadAsync 现在**总是**经 GetOrCreateThreadAsync
        // 做归属校验（早退曾让客户端给的 threadId 完全绕过它），故必须显式建模这一步。
        mockThreadService
            .Setup(s => s.GetOrCreateThreadAsync(threadId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((default(ConversationContext)!, threadId, false));
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
        // 线程存在且归当前用户所有：EnsureThreadAsync 现在**总是**经 GetOrCreateThreadAsync
        // 做归属校验（早退曾让客户端给的 threadId 完全绕过它），故必须显式建模这一步。
        mockThreadService
            .Setup(s => s.GetOrCreateThreadAsync(threadId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((default(ConversationContext)!, threadId, false));
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
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task InvokeStreamingAsync_QuotaExceeded_DoesNotPersistMessages()
    {
        var threadId = Guid.NewGuid();
        var mockThreadService = new Mock<IAgentThreadInternalService>();
        // 线程存在且归当前用户所有：EnsureThreadAsync 现在**总是**经 GetOrCreateThreadAsync
        // 做归属校验（早退曾让客户端给的 threadId 完全绕过它），故必须显式建模这一步。
        mockThreadService
            .Setup(s => s.GetOrCreateThreadAsync(threadId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((default(ConversationContext)!, threadId, false));
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
                It.IsAny<Guid?>(),
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
        // 线程存在且归当前用户所有：EnsureThreadAsync 现在**总是**经 GetOrCreateThreadAsync
        // 做归属校验（早退曾让客户端给的 threadId 完全绕过它），故必须显式建模这一步。
        mockThreadService
            .Setup(s => s.GetOrCreateThreadAsync(threadId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((default(ConversationContext)!, threadId, false));
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
            new StaticOptionsMonitor<AIOptions>(aiOptions),
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
        // 线程存在且归当前用户所有：EnsureThreadAsync 现在**总是**经 GetOrCreateThreadAsync
        // 做归属校验（早退曾让客户端给的 threadId 完全绕过它），故必须显式建模这一步。
        mockThreadService
            .Setup(s => s.GetOrCreateThreadAsync(threadId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((default(ConversationContext)!, threadId, false));
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
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task InvokeStreamingAsync_MaxHandoffs_DoesNotPersistMessages()
    {
        var threadId = Guid.NewGuid();
        var mockThreadService = new Mock<IAgentThreadInternalService>();
        // 线程存在且归当前用户所有：EnsureThreadAsync 现在**总是**经 GetOrCreateThreadAsync
        // 做归属校验（早退曾让客户端给的 threadId 完全绕过它），故必须显式建模这一步。
        mockThreadService
            .Setup(s => s.GetOrCreateThreadAsync(threadId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((default(ConversationContext)!, threadId, false));
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
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task InvokeStreamingAsync_SuccessfulStream_PreGeneratesAndExposesMessageIdsViaProperties()
    {
        var threadId = Guid.NewGuid();
        var mockThreadService = new Mock<IAgentThreadInternalService>();
        // 线程存在且归当前用户所有：EnsureThreadAsync 现在**总是**经 GetOrCreateThreadAsync
        // 做归属校验（早退曾让客户端给的 threadId 完全绕过它），故必须显式建模这一步。
        mockThreadService
            .Setup(s => s.GetOrCreateThreadAsync(threadId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((default(ConversationContext)!, threadId, false));
        mockThreadService
            .Setup(s => s.GetMessageHistoryAsync(threadId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChatMessage>());
        mockThreadService
            .Setup(s => s.SaveMessageAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, string _, string _, string? _, string? _, Guid? messageId, CancellationToken _) =>
                Task.FromResult(messageId ?? Guid.NewGuid()));

        var middleware = new HistoryMiddleware(
            mockThreadService.Object,
            CreateOptions(maxLoadedMessages: 100),
            Mock.Of<ILogger<HistoryMiddleware>>());

        var context = CreateContext(threadId);

        Guid? observedUserId = null;
        Guid? observedAssistantId = null;
        await foreach (var chunk in middleware.InvokeStreamingAsync(
            context,
            (_, _) => CreateCompletedStream(),
            CancellationToken.None))
        {
            // HistoryMiddleware stamps persisted ids directly onto the terminal chunk so the
            // downstream streaming service can surface them on the SSE event. AsyncLocal does
            // not propagate writes across async iterator yield boundaries.
            if (chunk.FinishReason != null && observedUserId == null)
            {
                observedUserId = chunk.UserMessageId;
                observedAssistantId = chunk.AssistantMessageId;
            }
        }

        observedUserId.ShouldNotBeNull();
        observedAssistantId.ShouldNotBeNull();
        observedUserId!.Value.ShouldNotBe(Guid.Empty);
        observedAssistantId!.Value.ShouldNotBe(Guid.Empty);
        // The IDs surfaced mid-stream must match what was finally persisted.
        mockThreadService.Verify(s => s.SaveMessageAsync(
            threadId, "user", "test", It.IsAny<string?>(), It.IsAny<string?>(),
            observedUserId, It.IsAny<CancellationToken>()), Times.Once);
        mockThreadService.Verify(s => s.SaveMessageAsync(
            threadId, "assistant", "ok", It.IsAny<string?>(), It.IsAny<string?>(),
            observedAssistantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    #region Helpers

    private static IOptionsMonitor<AIOptions> CreateOptions(int? maxLoadedMessages, bool useDefault = false)
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
        return new StaticOptionsMonitor<AIOptions>(aiOptions);
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
