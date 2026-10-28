namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// 条件节点的路由表达式写在 <see cref="WorkflowStepDto.Condition"/> 上时（可视化编辑器只给这一个字段），
/// 引擎不能再把它当 fail-closed 门控：此前 <c>accept</c> / <c>{{review}}</c> 一律判假，节点被跳过、没有 RouteTo，
/// 条件边不生效，下游所有分支都跑。
/// </summary>
public class WorkflowEngineConditionalNodeTests
{
    [Fact]
    public async Task ConditionalNode_ExpressionOnStepCondition_RunsAndRoutes()
    {
        var result = await RunAsync(new WorkflowStepDto
        {
            StepId = "decide",
            DependsOn = ["draft"],
            Condition = "accept",
            Configuration = new Dictionary<string, string> { ["nodeType"] = "conditional" }
        });

        result.StepResults.ShouldContain(r => r.StepId == "decide" && !r.Skipped, "the expression is the node's input, not a gate");
        result.StepResults.ShouldContain(r => r.StepId == "publish" && !r.Skipped);
        result.StepResults.ShouldNotContain(r => r.StepId == "revise", "the conditional edge prunes the branch it did not choose");
    }

    [Fact]
    public async Task ConditionalNode_TemplateOnStepCondition_RoutesByTheUpstreamOutput()
    {
        var result = await RunAsync(new WorkflowStepDto
        {
            StepId = "decide",
            DependsOn = ["draft"],
            Condition = "{{draft}}",
            Configuration = new Dictionary<string, string> { ["nodeType"] = "conditional" }
        }, draftEmits: "reject");

        result.StepResults.ShouldContain(r => r.StepId == "revise" && !r.Skipped);
        result.StepResults.ShouldNotContain(r => r.StepId == "publish");
    }

    [Fact]
    public async Task ConditionalNode_WithConfiguredExpression_StillTreatsStepConditionAsAGate()
    {
        // 配置键里另有表达式时，Condition 字段回到它对所有节点的含义：执行门控（fail-closed）。
        var result = await RunAsync(new WorkflowStepDto
        {
            StepId = "decide",
            DependsOn = ["draft"],
            Condition = "not-a-boolean",
            Configuration = new Dictionary<string, string>
            {
                ["nodeType"] = "conditional",
                ["condition"] = "accept"
            }
        });

        result.StepResults.ShouldContain(r => r.StepId == "decide" && r.Skipped);
    }

    [Fact]
    public async Task OtherNodes_StepCondition_RemainsAFailClosedGate()
    {
        var result = await RunAsync(new WorkflowStepDto
        {
            StepId = "decide",
            DependsOn = ["draft"],
            Condition = "accept",
            Configuration = new Dictionary<string, string> { ["nodeType"] = "echo-test" }
        });

        result.StepResults.ShouldContain(r => r.StepId == "decide" && r.Skipped);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>draft → decide → {accept: publish, reject: revise}</summary>
    private static async Task<WorkflowEngineResult> RunAsync(WorkflowStepDto decide, string draftEmits = "accept")
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IWorkflowNode, EchoTestNode>();
        services.AddScoped<IWorkflowNode, ConditionalNode>();
        services.AddScoped<WorkflowNodeExecutor>();
        var provider = services.BuildServiceProvider();

        var nodes = new List<WorkflowStepDto>
        {
            new()
            {
                StepId = "draft",
                Configuration = new Dictionary<string, string> { ["nodeType"] = "echo-test", ["emit"] = draftEmits }
            },
            decide,
            new()
            {
                StepId = "publish",
                DependsOn = ["decide"],
                Configuration = new Dictionary<string, string> { ["nodeType"] = "echo-test" }
            },
            new()
            {
                StepId = "revise",
                DependsOn = ["decide"],
                Configuration = new Dictionary<string, string> { ["nodeType"] = "echo-test" }
            }
        };

        var edges = new List<ConditionalEdge>
        {
            new()
            {
                FromNodeId = "decide",
                Routes = new Dictionary<string, string> { ["accept"] = "publish", ["reject"] = "revise" }
            }
        };

        var engine = new WorkflowEngine(Mock.Of<ILogger<WorkflowEngine>>());
        return await engine.ExecuteAsync(new WorkflowGraph(nodes, edges), "input", provider);
    }

    /// <summary>有 Configuration["emit"] 时输出该文本，否则输出 ran-{StepId}。</summary>
    private sealed class EchoTestNode : IWorkflowNode
    {
        public string NodeType => "echo-test";

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            var emit = context.Step.Configuration?.GetValueOrDefault("emit") ?? $"ran-{context.Step.StepId}";
            return Task.FromResult(new WorkflowNodeResult { Output = emit, IsSuccess = true });
        }
    }
}
