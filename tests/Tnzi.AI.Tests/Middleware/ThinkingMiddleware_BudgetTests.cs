
namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// ThinkingMiddleware 自动预算分配测试
/// </summary>
public class ThinkingMiddleware_BudgetTests
{
    private const string TestProvider = "anthropic";
    private const string ReasoningModel = "o4-mini";

    #region Helper

    private static ThinkingMiddleware CreateMiddleware(ProviderOptions? providerOptions = null)
    {
        var provider = providerOptions ?? new ProviderOptions
        {
            Enabled = true,
            MaxTokens = 16000,
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.High }
        };
        var aiOptions = new AIOptions
        {
            Providers = { [TestProvider] = provider }
        };
        var optionsMonitor = new TestBudgetOptionsMonitor<AIOptions>(aiOptions);
        var logger = Mock.Of<ILogger<ThinkingMiddleware>>();
        return new ThinkingMiddleware(optionsMonitor, logger);
    }

    private static AiMiddlewareContext CreateContext(string model = ReasoningModel)
    {
        return new AiMiddlewareContext
        {
            Request = new AgentRunRequest { UserMessage = "Hello" },
            Agent = AgentResolution.Success(
                agent: null!,
                provider: TestProvider,
                model: model,
                agentId: null),
            ServiceProvider = new Mock<IServiceProvider>().Object
        };
    }

    #endregion

    #region 自动预算分配

    [Fact]
    public async Task NoBudgetSet_AutoAllocates80Percent()
    {
        // MaxTokens=16000, BudgetTokens=null → 自动分配 80% = 12800
        var middleware = CreateMiddleware(new ProviderOptions
        {
            Enabled = true,
            MaxTokens = 16000,
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.High, BudgetTokens = null }
        });
        var context = CreateContext();
        ThinkingRequestContext? capturedContext = null;

        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            capturedContext = ThinkingRequestPolicy.RequestContext.Value;
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        capturedContext.ShouldNotBeNull();
        capturedContext!.Thinking.BudgetTokens.ShouldBe(12800);
    }

    [Fact]
    public async Task ExplicitBudget_UsesExplicit()
    {
        // MaxTokens=16000, BudgetTokens=5000 → 保持 5000
        var middleware = CreateMiddleware(new ProviderOptions
        {
            Enabled = true,
            MaxTokens = 16000,
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.High, BudgetTokens = 5000 }
        });
        var context = CreateContext();
        ThinkingRequestContext? capturedContext = null;

        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            capturedContext = ThinkingRequestPolicy.RequestContext.Value;
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        capturedContext.ShouldNotBeNull();
        capturedContext!.Thinking.BudgetTokens.ShouldBe(5000);
    }

    [Fact]
    public async Task NoMaxTokens_NoAutoAllocation()
    {
        // MaxTokens=null, BudgetTokens=null → BudgetTokens 保持 null
        var middleware = CreateMiddleware(new ProviderOptions
        {
            Enabled = true,
            MaxTokens = null,
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.High, BudgetTokens = null }
        });
        var context = CreateContext();
        ThinkingRequestContext? capturedContext = null;

        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            capturedContext = ThinkingRequestPolicy.RequestContext.Value;
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        capturedContext.ShouldNotBeNull();
        capturedContext!.Thinking.BudgetTokens.ShouldBeNull();
    }

    #endregion

    #region AsyncLocal 清理

    [Fact]
    public async Task CleanupAfterExecution()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext();

        await middleware.InvokeAsync(context, (ctx, ct) =>
            Task.FromResult(new AgentRunResult { Response = "ok" }));

        // finally 块应清理 AsyncLocal
        ThinkingRequestPolicy.RequestContext.Value.ShouldBeNull();
    }

    #endregion

    #region AlwaysOn 模型不自动分配

    [Fact]
    public async Task AlwaysOnModel_NoAutoAllocation()
    {
        // DeepSeek always-on 模型，即使有 MaxTokens 也不自动分配 budget
        const string deepSeekProvider = "DeepSeek";
        const string deepSeekModel = "deepseek-reasoner";

        var aiOptions = new AIOptions
        {
            Providers =
            {
                [deepSeekProvider] = new ProviderOptions
                {
                    Enabled = true,
                    MaxTokens = 16000,
                    Thinking = new ThinkingOptions { Effort = ReasoningEffort.High, BudgetTokens = null }
                }
            }
        };
        var optionsMonitor = new TestBudgetOptionsMonitor<AIOptions>(aiOptions);
        var middleware = new ThinkingMiddleware(optionsMonitor, Mock.Of<ILogger<ThinkingMiddleware>>());

        var context = new AiMiddlewareContext
        {
            Request = new AgentRunRequest { UserMessage = "Hello" },
            Agent = AgentResolution.Success(
                agent: null!,
                provider: deepSeekProvider,
                model: deepSeekModel,
                agentId: null),
            ServiceProvider = new Mock<IServiceProvider>().Object
        };

        ThinkingRequestContext? capturedContext = null;

        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            capturedContext = ThinkingRequestPolicy.RequestContext.Value;
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        capturedContext.ShouldNotBeNull();
        // AlwaysOn 模型不应自动分配 budget
        capturedContext!.Thinking.BudgetTokens.ShouldBeNull();
    }

    #endregion
}

/// <summary>
/// 测试用 IOptionsMonitor 实现
/// </summary>
file sealed class TestBudgetOptionsMonitor<T> : IOptionsMonitor<T>
{
    public T CurrentValue { get; }

    public TestBudgetOptionsMonitor(T value)
    {
        CurrentValue = value;
    }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
