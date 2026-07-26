namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// WorkflowEngine 条件边 × 循环回边交互测试 - 验证条件边的「跳过非目标分支」逻辑
/// 显式排除「当前激活循环内的节点」，避免误把循环体节点标记为完成而破坏循环迭代。
///
/// 场景：循环成员节点 <c>check</c> 持有条件边，一支路由出循环（<c>finish</c>，循环外），
/// 备选支 <c>work</c> 在循环内。旧实现会把备选支 <c>work</c> 误标完成（Skipped），
/// 破坏循环；修复后 <c>work</c> 作为激活循环成员不被裁剪。
/// </summary>
public class WorkflowEngineConditionalLoopTests
{
    [Fact]
    public async Task ConditionalEdge_AlternateBranchInsideActiveLoop_NotSkipped()
    {
        var result = await RunGraphAsync(checkEmits: "go-finish");

        // 选中目标分支（循环外）正常执行
        var finish = result.StepResults.Where(r => r.StepId == "finish").ToList();
        finish.ShouldNotBeEmpty();
        finish.ShouldAllBe(r => !r.Skipped);

        // 备选分支 work 是激活循环成员 → 不被误标 Skipped，仍然执行
        var work = result.StepResults.Where(r => r.StepId == "work").ToList();
        work.ShouldNotBeEmpty("loop-member alternate branch must not be pruned by conditional edge");
        work.ShouldAllBe(r => !r.Skipped, "loop-member node must not be force-completed by branch pruning");
    }

    [Fact]
    public async Task ConditionalEdge_NonLoopAlternateBranch_StillPruned()
    {
        // 控制组：备选分支不在循环内时，原有「跳过非目标分支」行为保持不变 —— 该分支被
        // 强制标记完成（force-completed），从不进入就绪队列执行，故不会出现在步骤结果中。
        var result = await RunGraphAsync(checkEmits: "go-finish", includeNonLoopBranch: true);

        // dead 是循环外的非目标分支 → 被裁剪，从未执行 → 不出现在 StepResults
        result.StepResults.ShouldNotContain(r => r.StepId == "dead",
            "non-loop alternate branch must still be pruned (force-completed, never executed)");

        // 对照：循环内备选分支 work 仍然执行
        result.StepResults.ShouldContain(r => r.StepId == "work" && !r.Skipped);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// 构建图：
    /// - check（循环成员，入口）：条件边路由 { "go-finish"→finish, "go-work"→work }，默认 finish
    /// - work（循环成员，循环体最后一个节点）：DependsOn check，输出 loop_done=true 让循环单次终止
    /// - finish（循环外）：DependsOn check
    /// - dead（可选，循环外备选支）：DependsOn check，Routes 中映射但非选中目标
    /// 循环 L = [check, work]。HandleLoop 检查循环最后一个节点（work）的 loop_done 元数据。
    /// </summary>
    private static async Task<WorkflowEngineResult> RunGraphAsync(string checkEmits, bool includeNonLoopBranch = false)
    {
        var services = BuildServiceProvider();
        var engine = new WorkflowEngine(Mock.Of<ILogger<WorkflowEngine>>());

        var nodes = new List<WorkflowStepDto>
        {
            new()
            {
                StepId = "check",
                Configuration = new Dictionary<string, string>
                {
                    ["nodeType"] = "echo-test",
                    ["emit"] = checkEmits
                }
            },
            new()
            {
                StepId = "work",
                DependsOn = ["check"],
                // 循环体最后一个节点设置 loop_done=true，让循环单次迭代后终止（确定性）
                Configuration = new Dictionary<string, string> { ["nodeType"] = "loop-done-emit" }
            },
            new()
            {
                StepId = "finish",
                DependsOn = ["check"],
                Configuration = new Dictionary<string, string> { ["nodeType"] = "echo-test" }
            }
        };

        var routes = new Dictionary<string, string>
        {
            ["go-finish"] = "finish",
            ["go-work"] = "work"
        };

        if (includeNonLoopBranch)
        {
            nodes.Add(new WorkflowStepDto
            {
                StepId = "dead",
                DependsOn = ["check"],
                Configuration = new Dictionary<string, string> { ["nodeType"] = "echo-test" }
            });
            routes["go-dead"] = "dead";
        }

        var edges = new List<ConditionalEdge>
        {
            new()
            {
                FromNodeId = "check",
                Routes = routes,
                DefaultTarget = "finish",
                ConditionType = EdgeConditionType.OutputContains
            }
        };

        var loops = new Dictionary<string, LoopDefinition>
        {
            ["L"] = new LoopDefinition { NodeIds = ["check", "work"], MaxIterations = 3 }
        };

        var graph = new WorkflowGraph(nodes, edges, loops);

        return await engine.ExecuteAsync(graph, "input", services);
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IWorkflowNode, EchoTestNode>();
        services.AddScoped<IWorkflowNode, LoopDoneEmitNode>();
        services.AddScoped<WorkflowNodeExecutor>();
        return services.BuildServiceProvider();
    }

    /// <summary>回显节点：有 Configuration["emit"] 时输出该文本，否则输出 ran-{StepId}。</summary>
    private sealed class EchoTestNode : IWorkflowNode
    {
        public string NodeType => "echo-test";

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            var emit = context.Step.Configuration?.GetValueOrDefault("emit") ?? $"ran-{context.Step.StepId}";
            return Task.FromResult(new WorkflowNodeResult { Output = emit, IsSuccess = true });
        }
    }

    /// <summary>循环终止节点：输出携带 loop_done=true 元数据，让循环单次迭代后终止。</summary>
    private sealed class LoopDoneEmitNode : IWorkflowNode
    {
        public string NodeType => "loop-done-emit";

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkflowNodeResult
            {
                Output = new WorkflowStepOutput
                {
                    Text = $"ran-{context.Step.StepId}",
                    Metadata = new Dictionary<string, string> { ["loop_done"] = "true" }
                },
                IsSuccess = true
            });
    }
}
