
namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// ThinkingMiddleware 单元测试
/// </summary>
public class ThinkingMiddlewareTests
{
    private const string TestProvider = "OpenAI";
    private const string ReasoningModel = "o4-mini";
    private const string NonReasoningModel = "gpt-4o";

    #region Helper

    private static ThinkingMiddleware CreateMiddleware(Action<AIOptions>? configure = null)
    {
        var options = new AIOptions();
        configure?.Invoke(options);
        var monitor = Microsoft.Extensions.Options.Options.Create(options);
        var optionsMonitor = new TestOptionsMonitor<AIOptions>(options);
        return new ThinkingMiddleware(optionsMonitor, Mock.Of<ILogger<ThinkingMiddleware>>());
    }

    private static AiMiddlewareContext CreateContext(
        string provider = TestProvider,
        string model = ReasoningModel,
        ReasoningEffort? requestEffort = null)
    {
        return new AiMiddlewareContext
        {
            Request = new AgentRunRequest
            {
                UserMessage = "Hello",
                ReasoningEffort = requestEffort
            },
            Agent = AgentResolution.Success(
                agent: null!,
                provider: provider,
                model: model,
                agentId: null),
            ServiceProvider = new Mock<IServiceProvider>().Object
        };
    }

    private static AIOptions CreateOptionsWithThinking(
        string providerName,
        ReasoningEffort effort,
        int? budgetTokens = null)
    {
        var opts = new AIOptions();
        opts.Providers[providerName] = new ProviderOptions
        {
            Enabled = true,
            Thinking = new ThinkingOptions { Effort = effort, BudgetTokens = budgetTokens }
        };
        return opts;
    }

    #endregion

    #region Effort=None: 显式写下「关」（留空在下游等于「不经过管线」，装饰器会按提供商配置打开）

    [Fact]
    public async Task EffortNone_NoProviderConfig_ThinkingExplicitlyOff()
    {
        var middleware = CreateMiddleware(); // No provider config
        var context = CreateContext();
        var nextCalled = false;

        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            nextCalled = true;
            ThinkingRequestPolicy.RequestContext.Value.ShouldNotBeNull().Thinking.Effort.ShouldBe(ReasoningEffort.None);
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        nextCalled.ShouldBeTrue();
        ThinkingRequestPolicy.RequestContext.Value.ShouldBeNull();
    }

    [Fact]
    public async Task EffortNone_ProviderConfigIsNone_ThinkingExplicitlyOff()
    {
        var opts = CreateOptionsWithThinking(TestProvider, ReasoningEffort.None);
        var middleware = CreateMiddleware(o =>
        {
            o.Providers[TestProvider] = opts.Providers[TestProvider];
        });
        var context = CreateContext();

        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            ThinkingRequestPolicy.RequestContext.Value.ShouldNotBeNull().Thinking.Effort.ShouldBe(ReasoningEffort.None);
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        ThinkingRequestPolicy.RequestContext.Value.ShouldBeNull();
    }

    #endregion

    #region Effort>None + 支持推理模型: AsyncLocal 被设置

    [Fact]
    public async Task EffortHigh_ReasoningModel_AsyncLocalSet()
    {
        var opts = CreateOptionsWithThinking(TestProvider, ReasoningEffort.High);
        var middleware = CreateMiddleware(o =>
        {
            o.Providers[TestProvider] = opts.Providers[TestProvider];
        });
        var context = CreateContext(model: ReasoningModel);

        ThinkingRequestContext? capturedContext = null;

        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            capturedContext = ThinkingRequestPolicy.RequestContext.Value;
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        capturedContext.ShouldNotBeNull();
        capturedContext!.Thinking.Effort.ShouldBe(ReasoningEffort.High);
        capturedContext.ProviderName.ShouldBe(TestProvider);
    }

    [Fact]
    public async Task EffortHigh_ReasoningModel_BudgetTokensPropagated()
    {
        var opts = CreateOptionsWithThinking(TestProvider, ReasoningEffort.High, budgetTokens: 4096);
        var middleware = CreateMiddleware(o =>
        {
            o.Providers[TestProvider] = opts.Providers[TestProvider];
        });
        var context = CreateContext(model: ReasoningModel);

        ThinkingRequestContext? capturedContext = null;

        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            capturedContext = ThinkingRequestPolicy.RequestContext.Value;
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        capturedContext.ShouldNotBeNull();
        capturedContext!.Thinking.BudgetTokens.ShouldBe(4096);
    }

    #endregion

    #region 不支持推理模型: 静默跳过

    [Fact]
    public async Task EffortHigh_NonReasoningModel_ThinkingExplicitlyOff()
    {
        var opts = CreateOptionsWithThinking(TestProvider, ReasoningEffort.High);
        var middleware = CreateMiddleware(o =>
        {
            o.Providers[TestProvider] = opts.Providers[TestProvider];
        });
        var context = CreateContext(model: NonReasoningModel); // gpt-4o 不支持推理

        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            ThinkingRequestPolicy.RequestContext.Value.ShouldNotBeNull().Thinking.Effort.ShouldBe(ReasoningEffort.None);
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });
    }

    /// <summary>
    /// 能力表按名字认模型，新模型没收录时配置的推理被跳过：此前只有一条 Debug，运维看不见。
    /// 每个提供商 + 模型提示一次 Information，之后回到 Debug。
    /// </summary>
    [Fact]
    public async Task EffortHigh_ModelNotInTheCapabilityTable_LogsInformationOncePerModel()
    {
        // 进程级记录：用本测试独有的提供商名，避免与其它用例共享状态
        var provider = "skip-log-" + Guid.NewGuid().ToString("N");
        var opts = CreateOptionsWithThinking(provider, ReasoningEffort.High);
        var logger = new Mock<ILogger<ThinkingMiddleware>>();
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        var middleware = new ThinkingMiddleware(new TestOptionsMonitor<AIOptions>(opts), logger.Object);

        for (var i = 0; i < 3; i++)
        {
            await middleware.InvokeAsync(CreateContext(provider: provider, model: "brand-new-model"),
                (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "ok" }));
        }

        logger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v != null && v.ToString()!.Contains("brand-new-model")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    #endregion

    #region Request override 优先

    [Fact]
    public async Task RequestReasoningEffort_OverridesProviderConfig()
    {
        // Provider 配置 Low，但 request override High
        var opts = CreateOptionsWithThinking(TestProvider, ReasoningEffort.Low);
        var middleware = CreateMiddleware(o =>
        {
            o.Providers[TestProvider] = opts.Providers[TestProvider];
        });
        var context = CreateContext(model: ReasoningModel, requestEffort: ReasoningEffort.High);

        ThinkingRequestContext? capturedContext = null;

        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            capturedContext = ThinkingRequestPolicy.RequestContext.Value;
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        capturedContext.ShouldNotBeNull();
        capturedContext!.Thinking.Effort.ShouldBe(ReasoningEffort.High);
    }

    [Fact]
    public async Task RequestReasoningEffort_None_OverridesProviderConfigHigh()
    {
        // Provider 配置 High，但 request override None → 跳过
        var opts = CreateOptionsWithThinking(TestProvider, ReasoningEffort.High);
        var middleware = CreateMiddleware(o =>
        {
            o.Providers[TestProvider] = opts.Providers[TestProvider];
        });
        var context = CreateContext(model: ReasoningModel, requestEffort: ReasoningEffort.None);

        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            ThinkingRequestPolicy.RequestContext.Value.ShouldNotBeNull().Thinking.Effort.ShouldBe(ReasoningEffort.None);
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });
    }

    #endregion

    #region AsyncLocal 在执行后被清理

    [Fact]
    public async Task AsyncLocal_ClearedAfterInvoke()
    {
        var opts = CreateOptionsWithThinking(TestProvider, ReasoningEffort.High);
        var middleware = CreateMiddleware(o =>
        {
            o.Providers[TestProvider] = opts.Providers[TestProvider];
        });
        var context = CreateContext(model: ReasoningModel);

        await middleware.InvokeAsync(context, (ctx, ct) =>
            Task.FromResult(new AgentRunResult { Response = "ok" }));

        // finally 块应清理 AsyncLocal
        ThinkingRequestPolicy.RequestContext.Value.ShouldBeNull();
    }

    [Fact]
    public async Task AsyncLocal_ClearedAfterException()
    {
        var opts = CreateOptionsWithThinking(TestProvider, ReasoningEffort.High);
        var middleware = CreateMiddleware(o =>
        {
            o.Providers[TestProvider] = opts.Providers[TestProvider];
        });
        var context = CreateContext(model: ReasoningModel);

        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await middleware.InvokeAsync(context, (ctx, ct) =>
                throw new InvalidOperationException("Simulated error"));
        });

        // 即使异常，finally 块也应清理
        ThinkingRequestPolicy.RequestContext.Value.ShouldBeNull();
    }

    #endregion

    #region AlwaysOn 模型 (DeepSeek): 仍然设置 AsyncLocal

    [Fact]
    public async Task AlwaysOnModel_DeepSeek_AsyncLocalSet()
    {
        const string deepSeekProvider = "DeepSeek";
        const string deepSeekModel = "deepseek-reasoner";

        var opts = new AIOptions();
        opts.Providers[deepSeekProvider] = new ProviderOptions
        {
            Enabled = true,
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.High }
        };

        var middleware = CreateMiddleware(o =>
        {
            o.Providers[deepSeekProvider] = opts.Providers[deepSeekProvider];
        });

        var context = CreateContext(provider: deepSeekProvider, model: deepSeekModel);

        ThinkingRequestContext? capturedContext = null;

        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            capturedContext = ThinkingRequestPolicy.RequestContext.Value;
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        // DeepSeek always-on 也应该设置 AsyncLocal（ThinkingRequestPolicy 内部处理 DeepSeek 跳过）
        capturedContext.ShouldNotBeNull();
    }

    #endregion
}

/// <summary>
/// 测试用 IOptionsMonitor 实现
/// </summary>
file sealed class TestOptionsMonitor<T> : Microsoft.Extensions.Options.IOptionsMonitor<T>
{
    public T CurrentValue { get; }

    public TestOptionsMonitor(T value)
    {
        CurrentValue = value;
    }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
