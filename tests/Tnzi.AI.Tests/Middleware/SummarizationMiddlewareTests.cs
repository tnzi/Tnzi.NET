namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// SummarizationMiddleware 单元测试
/// </summary>
public class SummarizationMiddlewareTests
{
    #region Helper

    private static SummarizationMiddleware CreateMiddleware(
        Action<AIOptions>? configure = null,
        IChatClientFactory? chatClientFactory = null)
    {
        var options = new AIOptions();
        configure?.Invoke(options);
        var optionsMonitor = new SummarizationTestOptionsMonitor<AIOptions>(options);
        return new SummarizationMiddleware(
            optionsMonitor,
            chatClientFactory ?? Mock.Of<IChatClientFactory>(),
            new HeuristicTokenEstimator(),
            Mock.Of<ILogger<SummarizationMiddleware>>());
    }

    private static AiMiddlewareContext CreateContext(List<ChatMessage>? messages = null)
    {
        return new AiMiddlewareContext
        {
            Request = new AgentRunRequest { UserMessage = "test" },
            Agent = AgentResolution.Success(
                agent: null!,
                provider: "test",
                model: "test-model",
                agentId: null),
            Messages = messages ?? [],
            ServiceProvider = new Mock<IServiceProvider>().Object
        };
    }

    private static List<ChatMessage> CreateMessages(int count, bool includeSystem = false)
    {
        var messages = new List<ChatMessage>();

        if (includeSystem)
        {
            messages.Add(new ChatMessage(ChatRole.System, "You are a helpful assistant."));
        }

        for (var i = 0; i < count; i++)
        {
            var role = i % 2 == 0 ? ChatRole.User : ChatRole.Assistant;
            // 每条消息 ~100 字符 = ~25 tokens
            messages.Add(new ChatMessage(role, $"Message {i}: " + new string('x', 90)));
        }

        return messages;
    }

    #endregion

    #region Disabled → passes through

    [Fact]
    public async Task Disabled_PassesThroughUnchanged()
    {
        var middleware = CreateMiddleware(o => o.Summarization.Enabled = false);
        var messages = CreateMessages(100);
        var originalCount = messages.Count;
        var context = CreateContext(messages: messages);
        var nextCalled = false;

        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            nextCalled = true;
            ctx.Messages.Count.ShouldBe(originalCount);
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        nextCalled.ShouldBeTrue();
    }

    #endregion

    #region Below threshold → no summarization

    [Fact]
    public async Task BelowMessageThreshold_NoSummarization()
    {
        var middleware = CreateMiddleware(o =>
        {
            o.Summarization.Enabled = true;
            o.Summarization.Trigger.Type = SummarizationTriggerType.Messages;
            o.Summarization.Trigger.MessageThreshold = 50;
        });

        var messages = CreateMessages(10); // 远低于阈值
        var originalCount = messages.Count;
        var context = CreateContext(messages: messages);

        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            ctx.Messages.Count.ShouldBe(originalCount);
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });
    }

    [Fact]
    public async Task BelowTokenThreshold_NoSummarization()
    {
        var middleware = CreateMiddleware(o =>
        {
            o.Summarization.Enabled = true;
            o.Summarization.Trigger.Type = SummarizationTriggerType.Tokens;
            o.Summarization.Trigger.TokenThreshold = 100_000;
        });

        var messages = CreateMessages(5); // 约 125 tokens，远低于阈值
        var context = CreateContext(messages: messages);
        var originalCount = messages.Count;

        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            ctx.Messages.Count.ShouldBe(originalCount);
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });
    }

    #endregion

    #region Above threshold → summarizes

    [Fact]
    public async Task AboveMessageThreshold_SummarizesOldMessages()
    {
        var mockChatClient = new Mock<IChatClient>();
        mockChatClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Summary of conversation")));

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(x => x.GetChatClient("test", "test-model"))
            .Returns(mockChatClient.Object);

        var middleware = CreateMiddleware(o =>
        {
            o.Summarization.Enabled = true;
            o.Summarization.Trigger.Type = SummarizationTriggerType.Messages;
            o.Summarization.Trigger.MessageThreshold = 10;
            o.Summarization.Keep.KeepLastMessages = 4;
            o.Summarization.TrimTokensToSummarize = 10; // 低阈值，确保触发
        }, mockFactory.Object);

        // 1 系统消息 + 20 非系统消息
        var messages = CreateMessages(20, includeSystem: true);
        var context = CreateContext(messages: messages);

        List<ChatMessage>? capturedMessages = null;

        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            capturedMessages = [.. ctx.Messages];
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        capturedMessages.ShouldNotBeNull();

        // 应包含：系统消息(1) + 摘要消息(1) + 保留的最近消息(4) = 6
        capturedMessages!.Count.ShouldBe(6);

        // 第一条是原始系统消息
        capturedMessages[0].Role.ShouldBe(ChatRole.System);
        capturedMessages[0].Text.ShouldBe("You are a helpful assistant.");

        // 第二条是摘要消息
        capturedMessages[1].Role.ShouldBe(ChatRole.System);
        capturedMessages[1].Text.ShouldContain("[Conversation summary:");
        capturedMessages[1].Text.ShouldContain("Summary of conversation");

        // 验证 IChatClient 被调用
        mockChatClient.Verify(c => c.GetResponseAsync(
            It.IsAny<IList<ChatMessage>>(),
            It.IsAny<ChatOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
        mockFactory.Verify(x => x.GetChatClient("test", "test-model"), Times.Once);
    }

    [Fact]
    public async Task AboveMessageThreshold_KeepsRecentMessages()
    {
        var mockChatClient = new Mock<IChatClient>();
        mockChatClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Summary")));

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(x => x.GetChatClient("test", "test-model"))
            .Returns(mockChatClient.Object);

        var middleware = CreateMiddleware(o =>
        {
            o.Summarization.Enabled = true;
            o.Summarization.Trigger.Type = SummarizationTriggerType.Messages;
            o.Summarization.Trigger.MessageThreshold = 5;
            o.Summarization.Keep.KeepLastMessages = 2;
            o.Summarization.TrimTokensToSummarize = 10;
        }, mockFactory.Object);

        var messages = CreateMessages(10); // 无系统消息
        // 记住最后两条消息的文本
        var lastTwoTexts = messages.TakeLast(2).Select(m => m.Text).ToList();
        var context = CreateContext(messages: messages);

        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            // 摘要消息(1) + 保留消息(2) = 3
            ctx.Messages.Count.ShouldBe(3);

            // 最后两条应为保留的原始消息
            ctx.Messages[1].Text.ShouldBe(lastTwoTexts[0]);
            ctx.Messages[2].Text.ShouldBe(lastTwoTexts[1]);

            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });
    }

    #endregion

    #region Summarization failure → graceful fallback

    [Fact]
    public async Task SummarizationFailure_KeepsOriginalMessages()
    {
        var mockChatClient = new Mock<IChatClient>();
        mockChatClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM unavailable"));

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(x => x.GetChatClient("test", "test-model"))
            .Returns(mockChatClient.Object);

        var middleware = CreateMiddleware(o =>
        {
            o.Summarization.Enabled = true;
            o.Summarization.Trigger.Type = SummarizationTriggerType.Messages;
            o.Summarization.Trigger.MessageThreshold = 5;
            o.Summarization.TrimTokensToSummarize = 10;
        }, mockFactory.Object);

        var messages = CreateMessages(20);
        var originalCount = messages.Count;
        var context = CreateContext(messages: messages);

        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            // 摘要失败后应保留原始消息
            ctx.Messages.Count.ShouldBe(originalCount);
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });
    }

    #endregion

    #region No IChatClient available

    [Fact]
    public async Task ChatClientFactoryFailure_KeepsOriginalMessages()
    {
        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(x => x.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Throws(new InvalidOperationException("Factory unavailable"));

        var middleware = CreateMiddleware(o =>
        {
            o.Summarization.Enabled = true;
            o.Summarization.Trigger.Type = SummarizationTriggerType.Messages;
            o.Summarization.Trigger.MessageThreshold = 5;
            o.Summarization.TrimTokensToSummarize = 10;
        }, mockFactory.Object);

        var messages = CreateMessages(20);
        var originalCount = messages.Count;
        var context = CreateContext(messages: messages);

        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            ctx.Messages.Count.ShouldBe(originalCount);
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });
    }

    #endregion

    #region Token trigger

    [Fact]
    public async Task AboveTokenThreshold_TriggersSummarization()
    {
        var mockChatClient = new Mock<IChatClient>();
        mockChatClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Token summary")));

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(x => x.GetChatClient("test", "test-model"))
            .Returns(mockChatClient.Object);

        var middleware = CreateMiddleware(o =>
        {
            o.Summarization.Enabled = true;
            o.Summarization.Trigger.Type = SummarizationTriggerType.Tokens;
            o.Summarization.Trigger.TokenThreshold = 50; // 低阈值
            o.Summarization.Keep.KeepLastMessages = 2;
            o.Summarization.TrimTokensToSummarize = 10;
        }, mockFactory.Object);

        var messages = CreateMessages(20); // ~500 tokens，超过阈值
        var context = CreateContext(messages: messages);

        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            // 应该被摘要: 摘要消息(1) + 保留消息(2) = 3
            ctx.Messages.Count.ShouldBe(3);
            ctx.Messages[0].Text.ShouldContain("[Conversation summary:");
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });
    }

    #endregion

    #region Fraction trigger

    [Fact]
    public async Task AboveFractionThreshold_TriggersSummarization()
    {
        var mockChatClient = new Mock<IChatClient>();
        mockChatClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Fraction summary")));

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(x => x.GetChatClient("test", "test-model"))
            .Returns(mockChatClient.Object);

        var middleware = CreateMiddleware(o =>
        {
            o.Summarization.Enabled = true;
            o.Summarization.Trigger.Type = SummarizationTriggerType.Fraction;
            o.Summarization.Trigger.FractionThreshold = 0.1; // 10% 低阈值
            o.Summarization.ModelContextWindow = 200; // 小窗口
            o.Summarization.Keep.KeepLastMessages = 2;
            o.Summarization.TrimTokensToSummarize = 10;
        }, mockFactory.Object);

        var messages = CreateMessages(10); // ~250 tokens > 200 * 0.1 = 20
        var context = CreateContext(messages: messages);

        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            ctx.Messages.Count.ShouldBe(3);
            ctx.Messages[0].Text.ShouldContain("[Conversation summary:");
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });
    }

    #endregion

    #region ResolveContextWindow — provider-aware context window

    [Fact]
    public async Task ProviderContextWindow_SmallWindow_TriggersSummarization()
    {
        // Provider with ContextWindowSize = 32_000.
        // Messages total ~30K tokens (~93.75% of 32K) → exceeds fraction threshold of 0.8.
        // Global ModelContextWindow = 128_000 → 30K/128K ≈ 23%, would NOT trigger.
        const string providerName = "small-provider";
        const int providerWindow = 32_000;
        const double fractionThreshold = 0.8;

        // Each message: ~1500 chars ≈ 375 tokens; 80 messages × 375 ≈ 30_000 tokens
        var messages = Enumerable.Range(0, 80)
            .Select(i => new ChatMessage(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, new string('x', 1500)))
            .ToList();

        var mockChatClient = new Mock<IChatClient>();
        mockChatClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Provider window summary")));

        var mockFactory = new Mock<IChatClientFactory>();
        // CreateContext 使用 "test" 作为 Agent.Provider，因此 GetChatClient 以 "test" 调用
        mockFactory.Setup(x => x.GetChatClient("test", It.IsAny<string?>()))
            .Returns(mockChatClient.Object);

        var middleware = CreateMiddleware(o =>
        {
            o.Summarization.Enabled = true;
            o.Summarization.Trigger.Type = SummarizationTriggerType.Fraction;
            o.Summarization.Trigger.FractionThreshold = fractionThreshold;
            o.Summarization.ModelContextWindow = 128_000; // 全局默认：30K/128K=23% → 不触发
            o.Summarization.Keep.KeepLastMessages = 4;
            o.Summarization.TrimTokensToSummarize = 1;
            // 配置 EffectiveProvider 的 ContextWindowSize = 32_000
            o.Providers[providerName] = new ProviderOptions { ContextWindowSize = providerWindow };
        }, mockFactory.Object);

        var context = CreateContext(messages: messages);
        // EffectiveProvider 决定 ResolveContextWindow 使用哪个 provider 的上下文窗口
        context.EffectiveProvider = providerName;

        List<ChatMessage>? capturedMessages = null;
        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            capturedMessages = [.. ctx.Messages];
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        // 摘要应被触发 — 因为 provider 的上下文窗口（32K）比全局默认（128K）小得多
        capturedMessages.ShouldNotBeNull();
        capturedMessages!.ShouldContain(m => m.Text != null && m.Text.Contains("[Conversation summary:"));
        mockChatClient.Verify(c => c.GetResponseAsync(
            It.IsAny<IList<ChatMessage>>(),
            It.IsAny<ChatOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProviderContextWindow_LargeGlobalDefault_NoSummarization()
    {
        // Same messages (~30K tokens) but no provider override → uses global 128K window.
        // 30K/128K ≈ 23% < 80% threshold → should NOT trigger summarization.
        var messages = Enumerable.Range(0, 80)
            .Select(i => new ChatMessage(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, new string('x', 1500)))
            .ToList();
        var originalCount = messages.Count;

        var middleware = CreateMiddleware(o =>
        {
            o.Summarization.Enabled = true;
            o.Summarization.Trigger.Type = SummarizationTriggerType.Fraction;
            o.Summarization.Trigger.FractionThreshold = 0.8;
            o.Summarization.ModelContextWindow = 128_000;
        });

        var context = CreateContext(messages: messages);
        // EffectiveProvider not set → ResolveContextWindow returns ModelContextWindow (128K)

        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            ctx.Messages.Count.ShouldBe(originalCount); // 不触发摘要
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });
    }

    #endregion

    #region Custom model name

    [Fact]
    public async Task CustomModelName_PassedToChatOptions()
    {
        ChatOptions? capturedOptions = null;
        var mockChatClient = new Mock<IChatClient>();
        mockChatClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Summary")));

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(x => x.GetChatClient("test", "gpt-4o-mini"))
            .Returns(mockChatClient.Object);

        var middleware = CreateMiddleware(o =>
        {
            o.Summarization.Enabled = true;
            o.Summarization.Trigger.Type = SummarizationTriggerType.Messages;
            o.Summarization.Trigger.MessageThreshold = 5;
            o.Summarization.Keep.KeepLastMessages = 2;
            o.Summarization.TrimTokensToSummarize = 10;
            o.Summarization.ModelName = "gpt-4o-mini";
        }, mockFactory.Object);

        var messages = CreateMessages(20);
        var context = CreateContext(messages: messages);

        await middleware.InvokeAsync(context, (ctx, ct) =>
            Task.FromResult(new AgentRunResult { Response = "ok" }));

        capturedOptions.ShouldNotBeNull();
        capturedOptions!.ModelId.ShouldBe("gpt-4o-mini");
        mockFactory.Verify(x => x.GetChatClient("test", "gpt-4o-mini"), Times.Once);
    }

    #endregion
}

/// <summary>
/// 测试用 IOptionsMonitor 实现
/// </summary>
file sealed class SummarizationTestOptionsMonitor<T> : IOptionsMonitor<T>
{
    public T CurrentValue { get; }

    public SummarizationTestOptionsMonitor(T value)
    {
        CurrentValue = value;
    }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
