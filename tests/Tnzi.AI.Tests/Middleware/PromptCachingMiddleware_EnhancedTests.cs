namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// PromptCachingMiddleware 增强功能测试 - 3-tier caching
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

    /// <summary>断言经 SDK 键写在内容块上的断点（<c>AIContent.AdditionalProperties["anthropic:cache_control"]</c>）。</summary>
    private static bool HasCacheBreakpoint(ChatMessage message)
        => message.Contents.Any(c => c.AdditionalProperties?.ContainsKey("anthropic:cache_control") == true);

    private static bool HasToolCacheBreakpoint(AITool tool)
        => tool.AdditionalProperties.TryGetValue("CacheControl", out var v) && v is Anthropic.Models.Messages.CacheControlEphemeral;

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
        HasCacheBreakpoint(userMsg1).ShouldBeFalse();
        HasCacheBreakpoint(userMsg2).ShouldBeTrue();
        HasCacheBreakpoint(userMsg3).ShouldBeTrue();

        // Assistant messages should not have cache_control from this tier
        HasCacheBreakpoint(assistantMsg1).ShouldBeFalse();
        HasCacheBreakpoint(assistantMsg2).ShouldBeFalse();
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
        HasCacheBreakpoint(userMsg1).ShouldBeTrue();
        HasCacheBreakpoint(userMsg2).ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // Tier 3: CacheToolDefinitions
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CacheToolDefinitions_MarksLastToolWithCacheControl()
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

        context.AdditionalTools.Count.ShouldBe(1);
        HasToolCacheBreakpoint(context.AdditionalTools[0]).ShouldBeTrue();
        // the wrapper must stay a callable function with the same name
        context.AdditionalTools[0].Name.ShouldBe("tool_a");
    }

    [Fact]
    public async Task CacheToolDefinitions_NoTools_MarksNothing()
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

        context.AdditionalTools.ShouldBeEmpty();
    }

    [Fact]
    public async Task CacheToolDefinitions_Disabled_MarksNothing()
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

        HasToolCacheBreakpoint(context.AdditionalTools[0]).ShouldBeFalse();
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
        HasCacheBreakpoint(systemMsg).ShouldBeTrue();

        // Tier 2a: CacheFirstNMessages=2 → 2nd message (assistantMsg1) has breakpoint
        HasCacheBreakpoint(assistantMsg1).ShouldBeTrue();

        // Tier 2b: CacheRecentUserMessages=1 → last user message (userMsg3) has breakpoint
        HasCacheBreakpoint(userMsg3).ShouldBeTrue();

        // Tier 3: last tool definition marked
        HasToolCacheBreakpoint(context.AdditionalTools[^1]).ShouldBeTrue();
    }
}
