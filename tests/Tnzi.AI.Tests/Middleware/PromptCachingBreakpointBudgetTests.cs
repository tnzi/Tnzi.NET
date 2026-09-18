namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// Anthropic 每个请求最多 4 个 cache_control 块。断点此前从未到达线路，所以
/// <c>CacheRecentUserMessages: 2</c> 一直无害；现在到了线路，工具 + 系统 + 前 N 条 + 2 条最近消息 = 5，
/// 该部署的每一次 Anthropic 请求都会被 API 以 400 拒绝。中间件必须按价值封顶：
/// 工具定义 &gt; 静态系统 &gt; 动态系统 &gt; 前 N 条历史 &gt; 最近用户消息（最新的优先保留）。
/// </summary>
public class PromptCachingBreakpointBudgetTests
{
    private const string AnthropicProvider = "anthropic";

    private static PromptCachingMiddleware CreateMiddleware(PromptCachingOptions cachingOptions)
    {
        var aiOptions = new AIOptions
        {
            Providers = { [AnthropicProvider] = new ProviderOptions { Enabled = true, PromptCaching = cachingOptions } }
        };
        var optionsMonitor = Mock.Of<IOptionsMonitor<AIOptions>>(o => o.CurrentValue == aiOptions);
        return new PromptCachingMiddleware(optionsMonitor, Mock.Of<ILogger<PromptCachingMiddleware>>());
    }

    private static AiMiddlewareContext CreateContext(List<ChatMessage> messages, bool withTool)
    {
        var context = new AiMiddlewareContext
        {
            Request = new AgentRunRequest { UserMessage = "test" },
            Agent = new AgentResolution { Agent = null, Provider = AnthropicProvider, ExecutionMode = AgentExecutionMode.Single },
            Messages = messages,
            ServiceProvider = new ServiceCollection().BuildServiceProvider()
        };
        if (withTool)
        {
            context.AdditionalTools.Add(AIFunctionFactory.Create(() => "test", "tool_a", "Tool A"));
        }
        return context;
    }

    private static Task<AgentRunResult> Next(AiMiddlewareContext ctx, CancellationToken ct)
        => Task.FromResult(new AgentRunResult { Response = "ok" });

    private static bool HasBreakpoint(ChatMessage message)
        => message.Contents.Any(c => c.AdditionalProperties?.ContainsKey("anthropic:cache_control") == true);

    private static bool HasToolBreakpoint(AITool tool)
        => tool.AdditionalProperties.TryGetValue("CacheControl", out var v) && v is Anthropic.Models.Messages.CacheControlEphemeral;

    /// <summary>线路上会出现的 cache_control 块总数：消息块 + 工具定义。</summary>
    private static int CountWireBreakpoints(AiMiddlewareContext context)
        => context.Messages.Sum(m => m.Contents.Count(c => c.AdditionalProperties?.ContainsKey("anthropic:cache_control") == true))
           + context.AdditionalTools.Count(HasToolBreakpoint);

    [Fact]
    public async Task StaticDynamic_NoDynamicMessage_HistoryFallbackWithTool_NeverExceedsFourBlocks()
    {
        // static/dynamic 策略在没有动态消息时回退到历史断点：工具 + 静态 + 前 N + 最近 N 会超出 4
        var middleware = CreateMiddleware(new PromptCachingOptions
        {
            Enabled = true,
            CacheStaticDynamicBoundary = true,
            CacheToolDefinitions = true,
            CacheFirstNMessages = 1,
            CacheRecentUserMessages = 3
        });
        var instructions = new ChatMessage(ChatRole.System, "You are an assistant.");
        var u1 = new ChatMessage(ChatRole.User, "one");
        var a1 = new ChatMessage(ChatRole.Assistant, "r1");
        var u2 = new ChatMessage(ChatRole.User, "two");
        var u3 = new ChatMessage(ChatRole.User, "three");
        var context = CreateContext([instructions, u1, a1, u2, u3], withTool: true);

        await middleware.InvokeAsync(context, Next);

        CountWireBreakpoints(context).ShouldBe(4);
        HasToolBreakpoint(context.AdditionalTools[0]).ShouldBeTrue();
        HasBreakpoint(instructions).ShouldBeTrue();
        HasBreakpoint(u1).ShouldBeTrue();   // CacheFirstNMessages = 1
        // 只剩一个名额：留给最新的那条用户消息
        HasBreakpoint(u3).ShouldBeTrue();
        HasBreakpoint(u2).ShouldBeFalse();
        context.Properties["PromptCachingBreakpoints"].ShouldBe(4);
    }

    [Fact]
    public async Task StaticDynamic_WithDynamicMessageAndTool_CountsEveryWireBlock()
    {
        var middleware = CreateMiddleware(new PromptCachingOptions
        {
            Enabled = true,
            CacheStaticDynamicBoundary = true,
            CacheToolDefinitions = true
        });
        var instructions = new ChatMessage(ChatRole.System, "You are an assistant.");
        var memory = new ChatMessage(ChatRole.System, "<memory>likes tea</memory>");
        var user = new ChatMessage(ChatRole.User, "hi");
        var context = CreateContext([instructions, memory, user], withTool: true);

        await middleware.InvokeAsync(context, Next);

        // 工具断点也在线路上，计数必须把它算进去，否则「3 个」的自述对不上真实请求
        CountWireBreakpoints(context).ShouldBe(3);
        context.Properties["PromptCachingBreakpoints"].ShouldBe(3);
    }

    [Fact]
    public async Task Legacy_AllTiersRequested_NeverExceedsFourBlocks()
    {
        var middleware = CreateMiddleware(new PromptCachingOptions
        {
            Enabled = true,
            CacheStaticDynamicBoundary = false,
            CacheSystemPrompt = true,
            CacheFirstNMessages = 1,
            CacheRecentUserMessages = 3,
            CacheToolDefinitions = true
        });
        var system = new ChatMessage(ChatRole.System, "System prompt");
        var u1 = new ChatMessage(ChatRole.User, "one");
        var a1 = new ChatMessage(ChatRole.Assistant, "r1");
        var u2 = new ChatMessage(ChatRole.User, "two");
        var u3 = new ChatMessage(ChatRole.User, "three");
        var context = CreateContext([system, u1, a1, u2, u3], withTool: true);

        await middleware.InvokeAsync(context, Next);

        CountWireBreakpoints(context).ShouldBe(4);
        HasToolBreakpoint(context.AdditionalTools[0]).ShouldBeTrue();
        HasBreakpoint(system).ShouldBeTrue();
        HasBreakpoint(u1).ShouldBeTrue();   // CacheFirstNMessages = 1
        HasBreakpoint(u3).ShouldBeTrue();   // the one recent slot left goes to the newest
        HasBreakpoint(u2).ShouldBeFalse();
    }

    [Fact]
    public async Task RecentUserMessagesAlone_KeepsTheNewestFour()
    {
        var middleware = CreateMiddleware(new PromptCachingOptions
        {
            Enabled = true,
            CacheStaticDynamicBoundary = false,
            CacheSystemPrompt = false,
            CacheToolDefinitions = false,
            CacheRecentUserMessages = 5
        });
        var users = Enumerable.Range(1, 5).Select(i => new ChatMessage(ChatRole.User, $"m{i}")).ToList();
        var context = CreateContext([.. users], withTool: false);

        await middleware.InvokeAsync(context, Next);

        CountWireBreakpoints(context).ShouldBe(4);
        HasBreakpoint(users[0]).ShouldBeFalse();
        users.Skip(1).ShouldAllBe(m => HasBreakpoint(m));
    }

    [Fact]
    public async Task SameBlockRequestedTwice_CountsOnce()
    {
        // 前 N 条与最近 N 条落在同一条消息上时是同一个块，不能记成两个名额
        var middleware = CreateMiddleware(new PromptCachingOptions
        {
            Enabled = true,
            CacheStaticDynamicBoundary = false,
            CacheSystemPrompt = false,
            CacheToolDefinitions = false,
            CacheFirstNMessages = 1,
            CacheRecentUserMessages = 1
        });
        var u1 = new ChatMessage(ChatRole.User, "only");
        var context = CreateContext([u1], withTool: false);

        await middleware.InvokeAsync(context, Next);

        CountWireBreakpoints(context).ShouldBe(1);
        context.Properties["PromptCachingBreakpoints"].ShouldBe(1);
    }
}
