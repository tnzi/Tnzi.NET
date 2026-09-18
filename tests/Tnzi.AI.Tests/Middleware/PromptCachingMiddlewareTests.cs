namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// PromptCachingMiddleware 单元测试
/// </summary>
public class PromptCachingMiddlewareTests
{
    private const string AnthropicProvider = "anthropic";
    private const string OpenAIProvider = "OpenAI";

    #region Helpers

    private static PromptCachingMiddleware CreateMiddleware(Action<AIOptions>? configure = null)
    {
        var options = new AIOptions();
        configure?.Invoke(options);
        var monitor = new TestOptionsMonitor<AIOptions>(options);
        return new PromptCachingMiddleware(monitor, Mock.Of<ILogger<PromptCachingMiddleware>>());
    }

    private static AiMiddlewareContext CreateContext(
        string provider = AnthropicProvider,
        string? effectiveProvider = null,
        List<ChatMessage>? messages = null)
    {
        var resolution = new AgentResolution
        {
            Agent = null,
            Provider = provider
        };
        return new AiMiddlewareContext
        {
            Request = new AgentRunRequest { UserMessage = "Hello" },
            Agent = resolution,
            Messages = messages ?? [],
            EffectiveProvider = effectiveProvider,
            ServiceProvider = new Mock<IServiceProvider>().Object
        };
    }

    private static AIOptions CreateOptionsWithCaching(
        string providerName,
        bool enabled = true,
        bool cacheSystemPrompt = true,
        int cacheFirstNMessages = 0,
        bool cacheStaticDynamicBoundary = false)
    {
        var opts = new AIOptions();
        opts.Providers[providerName] = new ProviderOptions
        {
            Enabled = true,
            PromptCaching = new PromptCachingOptions
            {
                Enabled = enabled,
                CacheStaticDynamicBoundary = cacheStaticDynamicBoundary,
                CacheSystemPrompt = cacheSystemPrompt,
                CacheFirstNMessages = cacheFirstNMessages
            }
        };
        return opts;
    }


    /// <summary>断言经 SDK 键写在内容块上的断点（<c>AIContent.AdditionalProperties["anthropic:cache_control"]</c>）。</summary>
    private static bool HasCacheBreakpoint(ChatMessage message)
        => message.Contents.Any(c => c.AdditionalProperties?.ContainsKey("anthropic:cache_control") == true);

    #endregion

    // -------------------------------------------------------------------------
    // No provider config: skip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_NoProviderConfig_PassesThroughWithoutCaching()
    {
        var middleware = CreateMiddleware(); // No provider config at all

        var systemMsg = new ChatMessage(ChatRole.System, "You are an assistant.");
        var context = CreateContext(provider: AnthropicProvider, messages: [systemMsg]);

        await middleware.InvokeAsync(context, (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "ok" }));

        systemMsg.AdditionalProperties.ShouldBeNull();
        context.Properties.ContainsKey("PromptCachingEnabled").ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // Caching disabled: skip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_CachingDisabled_PassesThroughWithoutCaching()
    {
        var opts = CreateOptionsWithCaching(AnthropicProvider, enabled: false);
        var middleware = CreateMiddleware(o => o.Providers[AnthropicProvider] = opts.Providers[AnthropicProvider]);

        var systemMsg = new ChatMessage(ChatRole.System, "You are an assistant.");
        var context = CreateContext(provider: AnthropicProvider, messages: [systemMsg]);

        await middleware.InvokeAsync(context, (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "ok" }));

        systemMsg.AdditionalProperties.ShouldBeNull();
        context.Properties.ContainsKey("PromptCachingEnabled").ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // OpenAI: marks PromptCachingEnabled but doesn't inject cache_control
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_OpenAIProvider_MarksEnabledButNoBreakpoints()
    {
        var opts = CreateOptionsWithCaching(OpenAIProvider);
        var middleware = CreateMiddleware(o => o.Providers[OpenAIProvider] = opts.Providers[OpenAIProvider]);

        var systemMsg = new ChatMessage(ChatRole.System, "You are an assistant.");
        var context = CreateContext(provider: OpenAIProvider, messages: [systemMsg]);

        await middleware.InvokeAsync(context, (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "ok" }));

        // OpenAI: marks context but does not inject cache_control on messages
        context.Properties["PromptCachingEnabled"].ShouldBe(true);
        // cache_control should NOT be set (Anthropic-specific)
        HasCacheBreakpoint(systemMsg).ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // Anthropic: applies cache_control to system message
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_AnthropicProvider_AppliesCacheControlToSystemMessage()
    {
        var opts = CreateOptionsWithCaching(AnthropicProvider, cacheSystemPrompt: true);
        var middleware = CreateMiddleware(o => o.Providers[AnthropicProvider] = opts.Providers[AnthropicProvider]);

        var systemMsg = new ChatMessage(ChatRole.System, "You are a helpful assistant.");
        var userMsg = new ChatMessage(ChatRole.User, "Hello!");
        var context = CreateContext(provider: AnthropicProvider, messages: [systemMsg, userMsg]);

        await middleware.InvokeAsync(context, (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "ok" }));

        context.Properties["PromptCachingEnabled"].ShouldBe(true);
        HasCacheBreakpoint(systemMsg).ShouldBeTrue();
    }

    [Fact]
    public async Task InvokeAsync_CachingDisabledSystemPrompt_NoBreakpointOnSystemMessage()
    {
        var opts = CreateOptionsWithCaching(AnthropicProvider, cacheSystemPrompt: false);
        var middleware = CreateMiddleware(o => o.Providers[AnthropicProvider] = opts.Providers[AnthropicProvider]);

        var systemMsg = new ChatMessage(ChatRole.System, "You are a helpful assistant.");
        var context = CreateContext(provider: AnthropicProvider, messages: [systemMsg]);

        await middleware.InvokeAsync(context, (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "ok" }));

        context.Properties["PromptCachingEnabled"].ShouldBe(true);
        HasCacheBreakpoint(systemMsg).ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // Anthropic: applies cache_control to history messages
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_AnthropicProvider_CacheFirstNMessages_AppliesBreakpointToNthMessage()
    {
        var opts = CreateOptionsWithCaching(AnthropicProvider, cacheSystemPrompt: false, cacheFirstNMessages: 2);
        var middleware = CreateMiddleware(o => o.Providers[AnthropicProvider] = opts.Providers[AnthropicProvider]);

        var userMsg1 = new ChatMessage(ChatRole.User, "First user message");
        var assistantMsg1 = new ChatMessage(ChatRole.Assistant, "First assistant response");
        var userMsg2 = new ChatMessage(ChatRole.User, "Second user message");
        var userMsg3 = new ChatMessage(ChatRole.User, "Third user message"); // beyond cache window
        var context = CreateContext(provider: AnthropicProvider, messages: [userMsg1, assistantMsg1, userMsg2, userMsg3]);

        await middleware.InvokeAsync(context, (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "ok" }));

        // CacheFirstNMessages=2: cache_control should be on the 2nd user/assistant message (index 1)
        HasCacheBreakpoint(userMsg1).ShouldBeFalse();
        HasCacheBreakpoint(assistantMsg1).ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // EffectiveProvider override
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_EffectiveProviderOverride_UsesEffectiveProvider()
    {
        // Agent.Provider = OpenAI, but EffectiveProvider = anthropic (skill override)
        var opts = CreateOptionsWithCaching(AnthropicProvider, cacheSystemPrompt: true);
        var middleware = CreateMiddleware(o => o.Providers[AnthropicProvider] = opts.Providers[AnthropicProvider]);

        var systemMsg = new ChatMessage(ChatRole.System, "System prompt");
        var context = CreateContext(provider: OpenAIProvider, effectiveProvider: AnthropicProvider, messages: [systemMsg]);

        await middleware.InvokeAsync(context, (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "ok" }));

        // Should use effectiveProvider = anthropic → cache_control applied
        HasCacheBreakpoint(systemMsg).ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // No messages: no-op
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_EmptyMessages_DoesNotThrow()
    {
        var opts = CreateOptionsWithCaching(AnthropicProvider);
        var middleware = CreateMiddleware(o => o.Providers[AnthropicProvider] = opts.Providers[AnthropicProvider]);

        var context = CreateContext(provider: AnthropicProvider, messages: []);

        var nextCalled = false;
        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            nextCalled = true;
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        nextCalled.ShouldBeTrue();
        context.Properties["PromptCachingEnabled"].ShouldBe(true);
    }

    // -------------------------------------------------------------------------
    // Streaming path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InvokeStreamingAsync_AnthropicProvider_AppliesCacheControlAndStreams()
    {
        var opts = CreateOptionsWithCaching(AnthropicProvider, cacheSystemPrompt: true);
        var middleware = CreateMiddleware(o => o.Providers[AnthropicProvider] = opts.Providers[AnthropicProvider]);

        var systemMsg = new ChatMessage(ChatRole.System, "You are a helpful assistant.");
        var context = CreateContext(provider: AnthropicProvider, messages: [systemMsg]);

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in middleware.InvokeStreamingAsync(context, (ctx, ct) => CreateChunkStream()))
        {
            chunks.Add(chunk);
        }

        context.Properties["PromptCachingEnabled"].ShouldBe(true);
        HasCacheBreakpoint(systemMsg).ShouldBeTrue();
        chunks.Count.ShouldBe(1);
    }

    // -------------------------------------------------------------------------
    // Task 19: Static/Dynamic boundary - two breakpoints
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PromptCaching_StaticDynamicBoundary_TwoBreakpoints()
    {
        var middleware = CreateMiddleware(o =>
        {
            o.Providers[AnthropicProvider] = new ProviderOptions
            {
                Enabled = true,
                PromptCaching = new PromptCachingOptions
                {
                    Enabled = true,
                    CacheStaticDynamicBoundary = true
                }
            };
        });

        // 模拟完整的消息列表（ContextInjection 已经执行后的状态）:
        // 动态: <user_profile>...</user_profile> (由 ContextInjectionMiddleware 注入)
        // 动态: <soul>...</soul> (由 ContextInjectionMiddleware 注入)
        // 动态: <memory>...</memory> (由 MemoryContextProvider 注入)
        // 静态: Agent Instructions (原始系统提示)
        // 对话: User message
        var userProfileMsg = new ChatMessage(ChatRole.System, "<user_profile>Name: Alice\nRole: Developer</user_profile>");
        var soulMsg = new ChatMessage(ChatRole.System, "<soul>You are a helpful assistant with a friendly personality.</soul>");
        var memoryMsg = new ChatMessage(ChatRole.System, "<memory>User prefers concise responses.</memory>");
        var instructionsMsg = new ChatMessage(ChatRole.System, "You are an AI coding assistant. Help users write clean code.");
        var userMsg = new ChatMessage(ChatRole.User, "Hello!");

        var context = CreateContext(provider: AnthropicProvider, messages:
            [userProfileMsg, soulMsg, memoryMsg, instructionsMsg, userMsg]);

        await middleware.InvokeAsync(context, (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "ok" }));

        // 验证 PromptCachingEnabled 标记
        context.Properties["PromptCachingEnabled"].ShouldBe(true);

        // Breakpoint 1 (static): Instructions 系统消息应有 cache_control
        HasCacheBreakpoint(instructionsMsg).ShouldBeTrue();

        // Breakpoint 2 (dynamic): 最后一条动态消息（memoryMsg）应有 cache_control
        HasCacheBreakpoint(memoryMsg).ShouldBeTrue();

        // 非最后一条动态消息不应有 cache_control（只标记最后一条）
        HasCacheBreakpoint(userProfileMsg).ShouldBeFalse();
        HasCacheBreakpoint(soulMsg).ShouldBeFalse();

        // User message 不应有 cache_control
        HasCacheBreakpoint(userMsg).ShouldBeFalse();

        // 验证断点计数
        context.Properties["PromptCachingBreakpoints"].ShouldBe(2);
    }

    [Fact]
    public async Task PromptCaching_StaticDynamicBoundary_OnlyStaticMessages_SingleBreakpoint()
    {
        var middleware = CreateMiddleware(o =>
        {
            o.Providers[AnthropicProvider] = new ProviderOptions
            {
                Enabled = true,
                PromptCaching = new PromptCachingOptions
                {
                    Enabled = true,
                    CacheStaticDynamicBoundary = true
                }
            };
        });

        // 没有动态上下文，只有 Instructions
        var instructionsMsg = new ChatMessage(ChatRole.System, "You are a helpful assistant.");
        var userMsg = new ChatMessage(ChatRole.User, "Hi!");

        var context = CreateContext(provider: AnthropicProvider, messages: [instructionsMsg, userMsg]);

        await middleware.InvokeAsync(context, (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "ok" }));

        // 只有静态断点
        HasCacheBreakpoint(instructionsMsg).ShouldBeTrue();

        context.Properties["PromptCachingBreakpoints"].ShouldBe(1);
    }

    [Fact]
    public async Task PromptCaching_StaticDynamicBoundary_OnlyDynamicMessages_SingleBreakpoint()
    {
        var middleware = CreateMiddleware(o =>
        {
            o.Providers[AnthropicProvider] = new ProviderOptions
            {
                Enabled = true,
                PromptCaching = new PromptCachingOptions
                {
                    Enabled = true,
                    CacheStaticDynamicBoundary = true
                }
            };
        });

        // 只有动态上下文，没有 Instructions
        var memoryMsg = new ChatMessage(ChatRole.System, "<memory>User likes dark mode.</memory>");
        var userMsg = new ChatMessage(ChatRole.User, "Hi!");

        var context = CreateContext(provider: AnthropicProvider, messages: [memoryMsg, userMsg]);

        await middleware.InvokeAsync(context, (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "ok" }));

        // 只有动态断点
        HasCacheBreakpoint(memoryMsg).ShouldBeTrue();

        context.Properties["PromptCachingBreakpoints"].ShouldBe(1);
    }

    private static async IAsyncEnumerable<AgentStreamChunk> CreateChunkStream()
    {
        yield return new AgentStreamChunk { Text = "streaming content" };
        await Task.CompletedTask;
    }
}

/// <summary>
/// 测试用 IOptionsMonitor 实现（与 ThinkingMiddlewareTests 共用相同模式，file-scoped 隔离）
/// </summary>
file sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
{
    public T CurrentValue { get; }

    public TestOptionsMonitor(T value) => CurrentValue = value;

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
