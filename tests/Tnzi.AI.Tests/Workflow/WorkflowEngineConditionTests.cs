namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// WorkflowEngine 步骤条件门控（EvaluateCondition）测试 - fail-closed 语义：
/// 空条件 = 执行；非空时仅白名单 "true"/"1"/"yes"（大小写不敏感）判真；
/// 未解析模板（{{...}} 残留）与任意自由文本一律判假跳过节点。
/// </summary>
public class WorkflowEngineConditionTests
{
    // -----------------------------------------------------------------------
    // 空条件 = 无条件 → 执行
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NullCondition_ExecutesNode()
    {
        var result = await RunSingleGatedStepAsync(condition: null);

        result.HasFailure.ShouldBeFalse();
        var step = result.StepResults.Single(r => r.StepId == "gated");
        step.Skipped.ShouldBeFalse();
        step.Output.ShouldBe("gated-ran");
    }

    // -----------------------------------------------------------------------
    // 白名单判真（大小写不敏感）
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("True")]
    [InlineData("yes")]
    [InlineData("Yes")]
    [InlineData("1")]
    [InlineData("  true  ")]
    public async Task TruthyWhitelistCondition_ExecutesNode(string condition)
    {
        var result = await RunSingleGatedStepAsync(condition);

        var step = result.StepResults.Single(r => r.StepId == "gated");
        step.Skipped.ShouldBeFalse();
        step.Output.ShouldBe("gated-ran");
    }

    // -----------------------------------------------------------------------
    // 显式假值（旧白名单仍判假）
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("no")]
    [InlineData("skip")]
    public async Task FalsyCondition_SkipsNode(string condition)
    {
        var result = await RunSingleGatedStepAsync(condition);

        result.HasFailure.ShouldBeFalse();
        var step = result.StepResults.Single(r => r.StepId == "gated");
        step.Skipped.ShouldBeTrue();
        step.Output.ShouldBe(string.Empty);
    }

    // -----------------------------------------------------------------------
    // fail-closed：自由文本（如 LLM 输出）不再默认放行
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("The output looks good to me, proceed.")]
    [InlineData("approved")]
    [InlineData("maybe")]
    public async Task FreeTextCondition_SkipsNode_FailClosed(string condition)
    {
        var result = await RunSingleGatedStepAsync(condition);

        var step = result.StepResults.Single(r => r.StepId == "gated");
        step.Skipped.ShouldBeTrue();
    }

    // -----------------------------------------------------------------------
    // fail-closed：未解析模板（{{...}} 残留）判假并告警
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UnresolvedTemplateCondition_SkipsNode_AndLogsWarning()
    {
        var logger = new CapturingLogger();
        var result = await RunSingleGatedStepAsync("{{nonexistent}}", logger);

        var step = result.StepResults.Single(r => r.StepId == "gated");
        step.Skipped.ShouldBeTrue();
        logger.Entries.ShouldContain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("unresolved template"));
    }

    [Fact]
    public async Task FreeTextCondition_LogsWarning()
    {
        var logger = new CapturingLogger();
        await RunSingleGatedStepAsync("looks fine", logger);

        logger.Entries.ShouldContain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("fail-closed"));
    }

    // -----------------------------------------------------------------------
    // 模板已解析的条件：跟随上游输出判真/判假
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ResolvedTemplateCondition_TruthyUpstreamOutput_ExecutesNode()
    {
        var result = await RunTemplateGatedGraphAsync(upstreamOutput: "true");

        var step = result.StepResults.Single(r => r.StepId == "gated");
        step.Skipped.ShouldBeFalse();
        step.Output.ShouldBe("gated-ran");
    }

    [Fact]
    public async Task ResolvedTemplateCondition_FreeTextUpstreamOutput_SkipsNode()
    {
        var result = await RunTemplateGatedGraphAsync(upstreamOutput: "Probably fine, ship it.");

        var step = result.StepResults.Single(r => r.StepId == "gated");
        step.Skipped.ShouldBeTrue();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>单节点图：节点带条件门控</summary>
    private static async Task<WorkflowEngineResult> RunSingleGatedStepAsync(string? condition, ILogger<WorkflowEngine>? logger = null)
    {
        var services = BuildServiceProvider();
        var engine = new WorkflowEngine(logger ?? Mock.Of<ILogger<WorkflowEngine>>());

        var graph = new WorkflowGraph(
        [
            new WorkflowStepDto
            {
                StepId = "gated",
                Condition = condition,
                Configuration = new Dictionary<string, string> { ["nodeType"] = "cond-gated-test" }
            }
        ]);

        return await engine.ExecuteAsync(graph, "input", services);
    }

    /// <summary>两节点图：src 节点输出 upstreamOutput，gated 节点条件为 {{src}}</summary>
    private static async Task<WorkflowEngineResult> RunTemplateGatedGraphAsync(string upstreamOutput)
    {
        var services = BuildServiceProvider();
        var engine = new WorkflowEngine(Mock.Of<ILogger<WorkflowEngine>>());

        var graph = new WorkflowGraph(
        [
            new WorkflowStepDto
            {
                StepId = "src",
                Configuration = new Dictionary<string, string>
                {
                    ["nodeType"] = "cond-emit-test",
                    ["emit"] = upstreamOutput
                }
            },
            new WorkflowStepDto
            {
                StepId = "gated",
                DependsOn = ["src"],
                Condition = "{{src}}",
                Configuration = new Dictionary<string, string> { ["nodeType"] = "cond-gated-test" }
            }
        ]);

        return await engine.ExecuteAsync(graph, "input", services);
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IWorkflowNode, EmitNode>();
        services.AddScoped<IWorkflowNode, GatedNode>();
        services.AddScoped<WorkflowNodeExecutor>();
        return services.BuildServiceProvider();
    }

    // -----------------------------------------------------------------------
    // Test node implementations
    // -----------------------------------------------------------------------

    /// <summary>输出 Configuration["emit"] 指定文本的测试节点</summary>
    private sealed class EmitNode : IWorkflowNode
    {
        public string NodeType => "cond-emit-test";

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            var text = context.Step.Configuration?.GetValueOrDefault("emit") ?? "emitted";
            return Task.FromResult(new WorkflowNodeResult { Output = text });
        }
    }

    /// <summary>被条件门控的测试节点</summary>
    private sealed class GatedNode : IWorkflowNode
    {
        public string NodeType => "cond-gated-test";

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WorkflowNodeResult { Output = "gated-ran" });
        }
    }

    /// <summary>捕获日志条目的测试 Logger</summary>
    private sealed class CapturingLogger : ILogger<WorkflowEngine>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
