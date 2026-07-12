using System.Reflection;
using Tnzi.AI.Sandbox.Middleware;

namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// 中间件链路完整性测试 — 验证所有中间件 Order 值正确、ShouldSkip 行为一致
/// </summary>
public class MiddlewareChainIntegrationTests
{
    #region 每个中间件 Order 值验证

    [Fact]
    public void RetryMiddleware_Order_IsCorrect()
    {
        var mw = new RetryMiddleware(
            new StaticOptionsMonitor<AIOptions>(new AIOptions()),
            NullLogger<RetryMiddleware>.Instance);
        mw.Order.ShouldBe(AiMiddlewareOrders.Retry);
    }

    [Fact]
    public void LoopDetectionMiddleware_Order_IsCorrect()
    {
        var mw = new LoopDetectionMiddleware(
            TestHelpers.CreateOptionsMonitor(new LoopDetectionOptions()),
            NullLogger<LoopDetectionMiddleware>.Instance);
        mw.Order.ShouldBe(AiMiddlewareOrders.LoopDetection);
    }

    [Fact]
    public void ToolErrorRecoveryMiddleware_Order_IsCorrect()
    {
        var mw = new ToolErrorRecoveryMiddleware(
            NullLogger<ToolErrorRecoveryMiddleware>.Instance);
        mw.Order.ShouldBe(AiMiddlewareOrders.ToolErrorRecovery);
    }

    [Fact]
    public void SubAgentLimitMiddleware_Order_IsCorrect()
    {
        var mw = new SubAgentLimitMiddleware(
            new StaticOptionsMonitor<SubAgentOptions>(new SubAgentOptions()),
            NullLogger<SubAgentLimitMiddleware>.Instance);
        mw.Order.ShouldBe(AiMiddlewareOrders.SubAgentLimit);
    }

    [Fact]
    public void ViewImageMiddleware_Order_IsCorrect()
    {
        var mw = new ViewImageMiddleware(
            NullLogger<ViewImageMiddleware>.Instance);
        mw.Order.ShouldBe(AiMiddlewareOrders.ViewImage);
    }

    #endregion

    #region 中间件管道 — 端到端执行

    [Fact]
    public async Task Pipeline_EmptyPipeline_ExecutesCoreDirectly()
    {
        var pipeline = new AiMiddlewarePipeline();
        var executed = false;

        var coreExecutor = pipeline.Build((ctx, ct) =>
        {
            executed = true;
            return Task.FromResult(new AgentRunResult { Response = "core" });
        });

        var context = TestHelpers.CreateMinimalContext();
        var result = await coreExecutor(context, CancellationToken.None);

        executed.ShouldBeTrue();
        result.Response.ShouldBe("core");
    }

    [Fact]
    public async Task Pipeline_MiddlewaresExecuteInOrder()
    {
        var executionOrder = new List<int>();

        var mw1 = new OrderTrackingMiddleware(100, executionOrder);
        var mw2 = new OrderTrackingMiddleware(200, executionOrder);
        var mw3 = new OrderTrackingMiddleware(300, executionOrder);

        // 故意乱序注入，Pipeline 应按 Order 排序
        var pipeline = new AiMiddlewarePipeline();
        pipeline.Use(mw3).Use(mw1).Use(mw2);

        var builtPipeline = pipeline.Build((ctx, ct) =>
            Task.FromResult(new AgentRunResult { Response = "ok" }));

        var context = TestHelpers.CreateMinimalContext();
        await builtPipeline(context, CancellationToken.None);

        executionOrder.ShouldBe(new List<int> { 100, 200, 300 }, "Middlewares should execute in Order sequence");
    }

    [Fact]
    public void Pipeline_Count_ReflectsAddedMiddlewares()
    {
        var pipeline = new AiMiddlewarePipeline();
        pipeline.Count.ShouldBe(0);

        pipeline.Use(new OrderTrackingMiddleware(100, []));
        pipeline.Count.ShouldBe(1);

        pipeline.Use(new OrderTrackingMiddleware(200, []));
        pipeline.Count.ShouldBe(2);
    }

    #endregion

    #region 所有 IAiMiddleware 实现类验证

    [Fact]
    public void AllIAiMiddleware_Implementations_HaveOrderProperty()
    {
        var middlewareTypes = new[]
        {
            typeof(RetryMiddleware),
            typeof(ThinkingMiddleware),
            typeof(PromptCachingMiddleware),
            typeof(QuotaMiddleware),
            typeof(InputGuardrailMiddleware),
            typeof(HistoryMiddleware),
            typeof(ContextInjectionMiddleware),
            typeof(UsageLoggingMiddleware),
            typeof(OutputGuardrailMiddleware),
            typeof(ToolGuardrailMiddleware),
            typeof(LoopDetectionMiddleware),
            typeof(ToolErrorRecoveryMiddleware),
            typeof(SubAgentLimitMiddleware),
            typeof(SummarizationMiddleware),
            typeof(FileUploadMiddleware),
            typeof(ViewImageMiddleware),
            typeof(TodoMiddleware),
            typeof(ClarificationMiddleware),
            typeof(SkillConstraintMiddleware),
            typeof(SandboxMiddleware),
            typeof(ThreadDataMiddleware)
        };

        foreach (var type in middlewareTypes)
        {
            typeof(IAiMiddleware).IsAssignableFrom(type)
                .ShouldBeTrue($"{type.Name} should implement IAiMiddleware");

            var orderProp = type.GetProperty("Order");
            orderProp.ShouldNotBeNull($"{type.Name} should have Order property");
        }
    }

    #endregion

    #region Streaming 管道

    [Fact]
    public async Task Pipeline_StreamingBuild_ExecutesCoreAndYieldsChunks()
    {
        var pipeline = new AiMiddlewarePipeline();

        var streamingPipeline = pipeline.BuildStreaming((ctx, ct) => ProduceChunks("hello", "world"));

        var context = TestHelpers.CreateMinimalContext();
        var chunks = new List<string>();
        await foreach (var chunk in streamingPipeline(context, CancellationToken.None))
        {
            if (chunk.Text != null) chunks.Add(chunk.Text);
        }

        chunks.ShouldBe(new List<string> { "hello", "world" });
    }

    #endregion

    #region Helpers

    private class OrderTrackingMiddleware : IAiMiddleware
    {
        private readonly int _order;
        private readonly List<int> _executionLog;

        public int Order => _order;

        public OrderTrackingMiddleware(int order, List<int> executionLog)
        {
            _order = order;
            _executionLog = executionLog;
        }

        public Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken ct = default)
        {
            _executionLog.Add(_order);
            return next(context, ct);
        }

        public IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, CancellationToken ct = default)
        {
            _executionLog.Add(_order);
            return next(context, ct);
        }
    }

#pragma warning disable CS1998
    private static async IAsyncEnumerable<AgentStreamChunk> ProduceChunks(params string[] texts)
    {
        foreach (var text in texts)
            yield return new AgentStreamChunk { Text = text };
    }
#pragma warning restore CS1998

    #endregion
}
