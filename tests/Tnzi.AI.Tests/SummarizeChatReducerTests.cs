namespace Tnzi.AI.Tests;

/// <summary>
/// SummarizeChatReducer 单元测试 — 验证历史消息摘要压缩策略
/// </summary>
public class SummarizeChatReducerTests
{
    [Fact]
    public async Task Reduce_BelowThreshold_ReturnsOriginal()
    {
        // 消息数未超阈值 → 不压缩
        var reducer = CreateReducer(messageThreshold: 50);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "System"),
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant, "Hi")
        };

        var result = await reducer.ReduceAsync(messages);

        result.Count.ShouldBe(3);
        result.ShouldBe(messages);
    }

    [Fact]
    public async Task Reduce_AboveMessageThreshold_TriggersSummary()
    {
        // 消息数超阈值 → 触发摘要，保留最近 N 轮
        var mockClient = CreateMockChatClient("This is a summary.");
        var reducer = CreateReducer(messageThreshold: 3, keepRecentTurns: 1, chatClient: mockClient.Object);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Turn 1"),
            new(ChatRole.Assistant, "Reply 1"),
            new(ChatRole.User, "Turn 2"),
            new(ChatRole.Assistant, "Reply 2"),
            new(ChatRole.User, "Turn 3"),
            new(ChatRole.Assistant, "Reply 3")
        };

        var result = await reducer.ReduceAsync(messages);

        // 应包含摘要系统消息 + 最后 1 轮
        result.ShouldContain(m => m.Role == ChatRole.System && m.Text!.Contains("summary", StringComparison.OrdinalIgnoreCase));
        result.ShouldContain(m => m.Text == "Turn 3");
        result.ShouldContain(m => m.Text == "Reply 3");
        // 旧消息被摘要替代
        result.ShouldNotContain(m => m.Text == "Turn 1");
        result.ShouldNotContain(m => m.Text == "Reply 1");
    }

    [Fact]
    public async Task Reduce_AboveTokenThreshold_TriggersSummary()
    {
        // Token 超阈值 → 触发摘要
        var mockClient = CreateMockChatClient("Token summary.");
        var reducer = CreateReducer(
            messageThreshold: 100, // 消息数不触发
            tokenThreshold: 10,    // 极低阈值确保触发
            keepRecentTurns: 1,
            chatClient: mockClient.Object);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "This is a very long message that should exceed the token threshold easily"),
            new(ChatRole.Assistant, "This is an equally long response to make sure we exceed the threshold"),
            new(ChatRole.User, "Last question"),
            new(ChatRole.Assistant, "Last answer")
        };

        var result = await reducer.ReduceAsync(messages);

        // 应触发摘要
        result.ShouldContain(m => m.Role == ChatRole.System && m.Text!.Contains("summary", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Reduce_SystemMessagesAlwaysPreserved()
    {
        var mockClient = CreateMockChatClient("Summary text.");
        var reducer = CreateReducer(messageThreshold: 3, keepRecentTurns: 1, chatClient: mockClient.Object);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a helpful assistant."),
            new(ChatRole.User, "Turn 1"),
            new(ChatRole.Assistant, "Reply 1"),
            new(ChatRole.User, "Turn 2"),
            new(ChatRole.Assistant, "Reply 2")
        };

        var result = await reducer.ReduceAsync(messages);

        // 原有系统消息保留
        result.ShouldContain(m => m.Text == "You are a helpful assistant.");
        // 摘要也作为系统消息注入
        result.Count(m => m.Role == ChatRole.System).ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Reduce_SummaryInjectedAsSystemMessage()
    {
        var mockClient = CreateMockChatClient("The users discussed weather.");
        var reducer = CreateReducer(messageThreshold: 3, keepRecentTurns: 1, chatClient: mockClient.Object);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Turn 1"),
            new(ChatRole.Assistant, "Reply 1"),
            new(ChatRole.User, "Turn 2"),
            new(ChatRole.Assistant, "Reply 2")
        };

        var result = await reducer.ReduceAsync(messages);

        var summaryMessage = result.FirstOrDefault(m =>
            m.Role == ChatRole.System && m.Text!.Contains("[Previous conversation summary]"));
        summaryMessage.ShouldNotBeNull();
        summaryMessage.Text.ShouldContain("The users discussed weather.");
    }

    [Fact]
    public async Task Reduce_CacheHit_ReusesSummary()
    {
        var mockClient = CreateMockChatClient("Cached summary.");
        var reducer = CreateReducer(messageThreshold: 3, keepRecentTurns: 1, chatClient: mockClient.Object);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Turn 1"),
            new(ChatRole.Assistant, "Reply 1"),
            new(ChatRole.User, "Turn 2"),
            new(ChatRole.Assistant, "Reply 2")
        };

        // 第一次调用 → 生成摘要
        await reducer.ReduceAsync(messages);

        // 第二次调用相同内容 → 应复用缓存
        var result = await reducer.ReduceAsync(messages);

        // LLM 应只被调用一次
        mockClient.Verify(
            c => c.GetResponseAsync(It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
        result.ShouldContain(m => m.Text!.Contains("Cached summary."));
    }

    [Fact]
    public async Task Reduce_CacheMiss_RegeneratesWhenContentChanges()
    {
        var callCount = 0;
        var mockClient = new Mock<IChatClient>();
        mockClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, $"Summary {callCount}"));
            });

        var reducer = CreateReducer(messageThreshold: 3, keepRecentTurns: 1, chatClient: mockClient.Object);

        var messages1 = new List<ChatMessage>
        {
            new(ChatRole.User, "Turn 1"),
            new(ChatRole.Assistant, "Reply 1"),
            new(ChatRole.User, "Turn 2"),
            new(ChatRole.Assistant, "Reply 2")
        };

        await reducer.ReduceAsync(messages1);

        // 修改内容 → 缓存失效
        var messages2 = new List<ChatMessage>
        {
            new(ChatRole.User, "Different turn 1"),
            new(ChatRole.Assistant, "Different reply 1"),
            new(ChatRole.User, "Turn 2"),
            new(ChatRole.Assistant, "Reply 2")
        };

        var result = await reducer.ReduceAsync(messages2);

        // LLM 应被调用两次（缓存未命中）
        callCount.ShouldBe(2);
        result.ShouldContain(m => m.Text!.Contains("Summary 2"));
    }

    [Fact]
    public async Task Reduce_LlmFailure_ReturnsOriginalMessages()
    {
        // LLM 调用失败 → 优雅降级
        var mockClient = new Mock<IChatClient>();
        mockClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM unavailable"));

        var reducer = CreateReducer(messageThreshold: 3, keepRecentTurns: 1, chatClient: mockClient.Object);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Turn 1"),
            new(ChatRole.Assistant, "Reply 1"),
            new(ChatRole.User, "Turn 2"),
            new(ChatRole.Assistant, "Reply 2")
        };

        var result = await reducer.ReduceAsync(messages);

        // 应返回原始消息
        result.Count.ShouldBe(messages.Count);
    }

    [Fact]
    public async Task Reduce_ToolMessages_FormattedCorrectlyInSummaryInput()
    {
        // 工具消息在摘要输入中正确格式化
        string? capturedConversation = null;
        var mockClient = new Mock<IChatClient>();
        mockClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((msgs, _, _) =>
            {
                capturedConversation = msgs.LastOrDefault()?.Text;
            })
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Tool summary.")));

        var reducer = CreateReducer(messageThreshold: 3, keepRecentTurns: 1, chatClient: mockClient.Object);

        var functionCall = new FunctionCallContent("call_abc", "get_weather", new Dictionary<string, object?> { ["city"] = "Tokyo" });
        var functionResult = new FunctionResultContent("call_abc", "Sunny, 25°C");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "What's the weather?"),
            new(ChatRole.Assistant, [functionCall]),
            new(ChatRole.Tool, [functionResult]),
            new(ChatRole.User, "Thanks"),
            new(ChatRole.Assistant, "You're welcome")
        };

        await reducer.ReduceAsync(messages);

        // 验证工具消息被正确格式化（而非 "[non-text content]"）
        capturedConversation.ShouldNotBeNull();
        capturedConversation.ShouldContain("get_weather");
        capturedConversation.ShouldContain("Sunny");
        capturedConversation.ShouldNotContain("[non-text content]");
    }

    [Fact]
    public async Task Reduce_EmptyMessages_ReturnsEmpty()
    {
        var reducer = CreateReducer(messageThreshold: 3);

        var result = await reducer.ReduceAsync([]);

        result.ShouldBeEmpty();
    }

    #region Helpers

    private static SummarizeChatReducer CreateReducer(
        int messageThreshold = 20,
        int? tokenThreshold = null,
        int keepRecentTurns = 5,
        int maxSummaryTokens = 1000,
        IChatClient? chatClient = null)
    {
        var options = new SummarizeOptions
        {
            MessageThreshold = messageThreshold,
            TokenThreshold = tokenThreshold,
            KeepRecentTurns = keepRecentTurns,
            MaxSummaryTokens = maxSummaryTokens
        };

        chatClient ??= CreateMockChatClient("Default summary.").Object;

        return new SummarizeChatReducer(
            options,
            chatClient,
            new HeuristicTokenEstimator(),
            Mock.Of<ILogger<SummarizeChatReducer>>());
    }

    private static Mock<IChatClient> CreateMockChatClient(string summaryText)
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, summaryText)));
        return mock;
    }

    #endregion
}
