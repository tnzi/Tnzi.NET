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
        AgentExecutionMode mode = AgentExecutionMode.Single,
        string? effectiveProvider = null,
        List<ChatMessage>? messages = null)
    {
        var resolution = new AgentResolution
        {
            Agent = null,
            Provider = provider,
            ExecutionMode = mode
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
        int cacheFirstNMessages = 0)
    {
        var opts = new AIOptions();
        opts.Providers[providerName] = new ProviderOptions
        {
            Enabled = true,
            PromptCaching = new PromptCachingOptions
            {
                Enabled = enabled,
                CacheSystemPrompt = cacheSystemPrompt,
                CacheFirstNMessages = cacheFirstNMessages
            }
        };
        return opts;
    }

    #endregion

    // -------------------------------------------------------------------------
    // ExternalCli mode: skip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_ExternalCliMode_PassesThroughWithoutCaching()
    {
        var opts = CreateOptionsWithCaching(AnthropicProvider);
        var middleware = CreateMiddleware(o => o.Providers[AnthropicProvider] = opts.Providers[AnthropicProvider]);

        var systemMsg = new ChatMessage(ChatRole.System, "You are an assistant.");
        var context = CreateContext(mode: AgentExecutionMode.ExternalCli, messages: [systemMsg]);

        await middleware.InvokeAsync(context, (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "ok" }));

        // In ExternalCli mode the system message should NOT have cache_control applied
        systemMsg.AdditionalProperties.ShouldBeNull();
        context.Properties.ContainsKey("PromptCachingEnabled").ShouldBeFalse();
    }

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
        systemMsg.AdditionalProperties?.ContainsKey("cache_control").ShouldNotBe(true);
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
        systemMsg.AdditionalProperties.ShouldNotBeNull();
        systemMsg.AdditionalProperties!.ContainsKey("cache_control").ShouldBeTrue();
        var cacheControl = systemMsg.AdditionalProperties["cache_control"] as Dictionary<string, string>;
        cacheControl.ShouldNotBeNull();
        cacheControl!["type"].ShouldBe("ephemeral");
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
        systemMsg.AdditionalProperties?.ContainsKey("cache_control").ShouldNotBe(true);
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
        userMsg1.AdditionalProperties?.ContainsKey("cache_control").ShouldNotBe(true);
        assistantMsg1.AdditionalProperties.ShouldNotBeNull();
        assistantMsg1.AdditionalProperties!.ContainsKey("cache_control").ShouldBeTrue();
        var cc = assistantMsg1.AdditionalProperties["cache_control"] as Dictionary<string, string>;
        cc!["type"].ShouldBe("ephemeral");
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
        systemMsg.AdditionalProperties.ShouldNotBeNull();
        systemMsg.AdditionalProperties!.ContainsKey("cache_control").ShouldBeTrue();
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
        systemMsg.AdditionalProperties.ShouldNotBeNull();
        systemMsg.AdditionalProperties!.ContainsKey("cache_control").ShouldBeTrue();
        chunks.Count.ShouldBe(1);
    }

    [Fact]
    public async Task InvokeStreamingAsync_ExternalCliMode_PassesThroughWithoutCaching()
    {
        var opts = CreateOptionsWithCaching(AnthropicProvider);
        var middleware = CreateMiddleware(o => o.Providers[AnthropicProvider] = opts.Providers[AnthropicProvider]);

        var systemMsg = new ChatMessage(ChatRole.System, "System prompt");
        var context = CreateContext(mode: AgentExecutionMode.ExternalCli, messages: [systemMsg]);

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in middleware.InvokeStreamingAsync(context, (ctx, ct) => CreateChunkStream()))
        {
            chunks.Add(chunk);
        }

        // ExternalCli: cache_control should NOT be applied
        systemMsg.AdditionalProperties?.ContainsKey("cache_control").ShouldNotBe(true);
        context.Properties.ContainsKey("PromptCachingEnabled").ShouldBeFalse();
        chunks.Count.ShouldBe(1);
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
