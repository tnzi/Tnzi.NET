namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// PromptCachingMiddleware 增强功能测试 — 3-tier caching + OAuth guard
/// </summary>
public class PromptCachingMiddleware_EnhancedTests
{
    private const string AnthropicProvider = "anthropic";

    #region Helpers

    private static PromptCachingMiddleware CreateMiddleware(PromptCachingOptions? cachingOptions = null)
    {
        var providerOptions = new ProviderOptions
        {
            Enabled = true,
            PromptCaching = cachingOptions ?? new PromptCachingOptions { Enabled = true }
        };
        var aiOptions = new AIOptions
        {
            Providers = { [AnthropicProvider] = providerOptions }
        };
        var optionsMonitor = Mock.Of<IOptionsMonitor<AIOptions>>(o => o.CurrentValue == aiOptions);
        var logger = Mock.Of<ILogger<PromptCachingMiddleware>>();
        return new PromptCachingMiddleware(optionsMonitor, logger);
    }

    private static AiMiddlewareContext CreateContext(List<ChatMessage>? messages = null)
    {
        return new AiMiddlewareContext
        {
            Request = new AgentRunRequest { UserMessage = "test" },
            Agent = new AgentResolution
            {
                Agent = null,
                Provider = AnthropicProvider,
                ExecutionMode = AgentExecutionMode.Single
            },
            Messages = messages ?? [],
            ServiceProvider = new ServiceCollection().BuildServiceProvider()
        };
    }

    private static Task<AgentRunResult> NextDelegate(AiMiddlewareContext ctx, CancellationToken ct)
        => Task.FromResult(new AgentRunResult { Response = "ok" });

    #endregion

    // -------------------------------------------------------------------------
    // Tier 2b: CacheRecentUserMessages
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CacheRecentUserMessages_SetsCacheControl()
    {
        var middleware = CreateMiddleware(new PromptCachingOptions
        {
            Enabled = true,
            CacheStaticDynamicBoundary = false,
            CacheSystemPrompt = false,
            CacheRecentUserMessages = 2
        });

        var userMsg1 = new ChatMessage(ChatRole.User, "First");
        var assistantMsg1 = new ChatMessage(ChatRole.Assistant, "Response 1");
        var userMsg2 = new ChatMessage(ChatRole.User, "Second");
        var assistantMsg2 = new ChatMessage(ChatRole.Assistant, "Response 2");
        var userMsg3 = new ChatMessage(ChatRole.User, "Third");

        var context = CreateContext([userMsg1, assistantMsg1, userMsg2, assistantMsg2, userMsg3]);

        await middleware.InvokeAsync(context, NextDelegate);

        // CacheRecentUserMessages=2: last 2 user messages (userMsg2, userMsg3) should have cache_control
        userMsg1.AdditionalProperties?.ContainsKey("cache_control").ShouldNotBe(true);
        userMsg2.AdditionalProperties.ShouldNotBeNull();
        userMsg2.AdditionalProperties!.ContainsKey("cache_control").ShouldBeTrue();
        userMsg3.AdditionalProperties.ShouldNotBeNull();
        userMsg3.AdditionalProperties!.ContainsKey("cache_control").ShouldBeTrue();

        // Assistant messages should not have cache_control from this tier
        assistantMsg1.AdditionalProperties?.ContainsKey("cache_control").ShouldNotBe(true);
        assistantMsg2.AdditionalProperties?.ContainsKey("cache_control").ShouldNotBe(true);
    }

    [Fact]
    public async Task CacheRecentUserMessages_LessThanN_CachesAll()
    {
        var middleware = CreateMiddleware(new PromptCachingOptions
        {
            Enabled = true,
            CacheStaticDynamicBoundary = false,
            CacheSystemPrompt = false,
            CacheRecentUserMessages = 5 // 请求 5 条但只有 2 条
        });

        var userMsg1 = new ChatMessage(ChatRole.User, "First");
        var userMsg2 = new ChatMessage(ChatRole.User, "Second");

        var context = CreateContext([userMsg1, userMsg2]);

        await middleware.InvokeAsync(context, NextDelegate);

        // 只有 2 条用户消息，全部应被缓存
        userMsg1.AdditionalProperties.ShouldNotBeNull();
        userMsg1.AdditionalProperties!.ContainsKey("cache_control").ShouldBeTrue();
        userMsg2.AdditionalProperties.ShouldNotBeNull();
        userMsg2.AdditionalProperties!.ContainsKey("cache_control").ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // Tier 3: CacheToolDefinitions
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CacheToolDefinitions_SetsCacheLastToolDefinitionProperty()
    {
        var middleware = CreateMiddleware(new PromptCachingOptions
        {
            Enabled = true,
            CacheStaticDynamicBoundary = false,
            CacheSystemPrompt = false,
            CacheToolDefinitions = true
        });

        var context = CreateContext([new ChatMessage(ChatRole.System, "system")]);
        context.AdditionalTools.Add(AIFunctionFactory.Create(() => "test", "tool_a", "Tool A"));

        await middleware.InvokeAsync(context, NextDelegate);

        context.Properties.ContainsKey("CacheLastToolDefinition").ShouldBeTrue();
        context.Properties["CacheLastToolDefinition"].ShouldBe(true);
    }

    [Fact]
    public async Task CacheToolDefinitions_NoTools_DoesNotSetProperty()
    {
        var middleware = CreateMiddleware(new PromptCachingOptions
        {
            Enabled = true,
            CacheStaticDynamicBoundary = false,
            CacheSystemPrompt = false,
            CacheToolDefinitions = true
        });

        var context = CreateContext();
        // No tools added

        await middleware.InvokeAsync(context, NextDelegate);

        context.Properties.ContainsKey("CacheLastToolDefinition").ShouldBeFalse();
    }

    [Fact]
    public async Task CacheToolDefinitions_Disabled_DoesNotSetProperty()
    {
        var middleware = CreateMiddleware(new PromptCachingOptions
        {
            Enabled = true,
            CacheStaticDynamicBoundary = false,
            CacheSystemPrompt = false,
            CacheToolDefinitions = false
        });

        var context = CreateContext();
        context.AdditionalTools.Add(AIFunctionFactory.Create(() => "test", "tool_b", "Tool B"));

        await middleware.InvokeAsync(context, NextDelegate);

        context.Properties.ContainsKey("CacheLastToolDefinition").ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // OAuth Guard: DisableOnOAuthToken
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DisableOnOAuthToken_SkipsCaching()
    {
        var middleware = CreateMiddleware(new PromptCachingOptions
        {
            Enabled = true,
            CacheSystemPrompt = true,
            DisableOnOAuthToken = true
        });

        var systemMsg = new ChatMessage(ChatRole.System, "System prompt");
        var context = CreateContext([systemMsg]);
        context.Properties["OAuthTokenDetected"] = true;

        await middleware.InvokeAsync(context, NextDelegate);

        // PromptCachingEnabled should NOT be set
        context.Properties.ContainsKey("PromptCachingEnabled").ShouldBeFalse();
        // System message should NOT have cache_control
        systemMsg.AdditionalProperties?.ContainsKey("cache_control").ShouldNotBe(true);
    }

    [Fact]
    public async Task DisableOnOAuthToken_Disabled_AllowsCaching()
    {
        var middleware = CreateMiddleware(new PromptCachingOptions
        {
            Enabled = true,
            CacheSystemPrompt = true,
            DisableOnOAuthToken = false // 明确禁用 OAuth guard
        });

        var systemMsg = new ChatMessage(ChatRole.System, "System prompt");
        var context = CreateContext([systemMsg]);
        context.Properties["OAuthTokenDetected"] = true;

        await middleware.InvokeAsync(context, NextDelegate);

        // DisableOnOAuthToken=false: caching should still work
        context.Properties["PromptCachingEnabled"].ShouldBe(true);
        systemMsg.AdditionalProperties.ShouldNotBeNull();
        systemMsg.AdditionalProperties!.ContainsKey("cache_control").ShouldBeTrue();
    }

    [Fact]
    public async Task DisableOnOAuthToken_NoOAuthToken_AllowsCaching()
    {
        var middleware = CreateMiddleware(new PromptCachingOptions
        {
            Enabled = true,
            CacheSystemPrompt = true,
            DisableOnOAuthToken = true
        });

        var systemMsg = new ChatMessage(ChatRole.System, "System prompt");
        var context = CreateContext([systemMsg]);
        // No OAuthTokenDetected property set

        await middleware.InvokeAsync(context, NextDelegate);

        // No OAuth token: caching should work normally
        context.Properties["PromptCachingEnabled"].ShouldBe(true);
        systemMsg.AdditionalProperties.ShouldNotBeNull();
        systemMsg.AdditionalProperties!.ContainsKey("cache_control").ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // Combined: 3-tier caching
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreeTierCaching_AllTiersApplied()
    {
        var middleware = CreateMiddleware(new PromptCachingOptions
        {
            Enabled = true,
            CacheStaticDynamicBoundary = false,
            CacheSystemPrompt = true,
            CacheFirstNMessages = 2,
            CacheRecentUserMessages = 1,
            CacheToolDefinitions = true
        });

        var systemMsg = new ChatMessage(ChatRole.System, "System prompt");
        var userMsg1 = new ChatMessage(ChatRole.User, "First");
        var assistantMsg1 = new ChatMessage(ChatRole.Assistant, "Response 1");
        var userMsg2 = new ChatMessage(ChatRole.User, "Second");
        var userMsg3 = new ChatMessage(ChatRole.User, "Third");

        var context = CreateContext([systemMsg, userMsg1, assistantMsg1, userMsg2, userMsg3]);
        context.AdditionalTools.Add(AIFunctionFactory.Create(() => "test", "tool_c", "Tool C"));

        await middleware.InvokeAsync(context, NextDelegate);

        // Tier 1: system message cached
        systemMsg.AdditionalProperties.ShouldNotBeNull();
        systemMsg.AdditionalProperties!.ContainsKey("cache_control").ShouldBeTrue();

        // Tier 2a: CacheFirstNMessages=2 → 2nd message (assistantMsg1) has breakpoint
        assistantMsg1.AdditionalProperties.ShouldNotBeNull();
        assistantMsg1.AdditionalProperties!.ContainsKey("cache_control").ShouldBeTrue();

        // Tier 2b: CacheRecentUserMessages=1 → last user message (userMsg3) has breakpoint
        userMsg3.AdditionalProperties.ShouldNotBeNull();
        userMsg3.AdditionalProperties!.ContainsKey("cache_control").ShouldBeTrue();

        // Tier 3: tool definitions marked
        context.Properties["CacheLastToolDefinition"].ShouldBe(true);
    }
}
