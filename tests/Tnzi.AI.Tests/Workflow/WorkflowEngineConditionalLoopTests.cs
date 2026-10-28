namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// WorkflowEngine 条件边 × 循环的交互。
///
/// 循环成员上的条件边决定的是「再来一轮」还是「离开循环」：
/// <list type="bullet">
/// <item>选中循环内的目标：循环外的备选分支只是<b>本轮</b>不走。循环重置进入下一轮时它们恢复可选，
/// 否则出口在第一轮选了 rework 之后就永远关上了，第二轮判 accept 也走不出去。</item>
/// <item>选中循环外的目标：离开循环。循环内的备选分支不再执行，循环也不再重置，
/// 否则选了出口照样 rework、照样一轮轮审下去直到 MaxIterations。</item>
/// </list>
/// </summary>
public class WorkflowEngineConditionalLoopTests
{
    // -----------------------------------------------------------------------
    // review → { accept: publish（循环外）, rework: rework（循环内）}，循环 [review, rework]
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReviewLoop_ReworkThenAccept_PublishRunsOnceAndTheLoopStops()
    {
        var recorder = new ExecutionRecorder();
        var result = await RunReviewLoopAsync(recorder, ["rework", "accept"]);

        result.HasFailure.ShouldBeFalse();
        recorder.Count("review").ShouldBe(2);
        recorder.Count("rework").ShouldBe(1, "choosing the exit ends the loop; the in-loop branch does not run");
        recorder.Count("publish").ShouldBe(1, "the exit not taken in the first round is still open in the second");
        result.FinalOutput.ShouldBe("ran-publish");
    }

    [Fact]
    public async Task ReviewLoop_AcceptFirst_ReworkNeverRunsAndTheLoopDoesNotIterate()
    {
        var recorder = new ExecutionRecorder();
        var result = await RunReviewLoopAsync(recorder, ["accept"]);

        result.HasFailure.ShouldBeFalse();
        recorder.Count("review").ShouldBe(1);
        recorder.Count("rework").ShouldBe(0, "the reviewer accepted; nothing is reworked");
        recorder.Count("publish").ShouldBe(1);
        result.FinalOutput.ShouldBe("ran-publish");
    }

    [Fact]
    public async Task ReviewLoop_ReworkUntilMaxIterations_TheExitIsNotTaken()
    {
        // 定义：迭代用尽时最后一次路由仍选了循环内分支，出口不走：审查者从未放行的内容
        // 不会因为轮数耗尽就被当作通过。执行以循环的最后输出结束。
        var recorder = new ExecutionRecorder();
        var result = await RunReviewLoopAsync(recorder, ["rework"], maxIterations: 3);

        result.HasFailure.ShouldBeFalse();
        recorder.Count("review").ShouldBe(3);
        recorder.Count("rework").ShouldBe(3);
        recorder.Count("publish").ShouldBe(0, "the reviewer never accepted");
        result.FinalOutput.ShouldBe("ran-rework");
    }

    // -----------------------------------------------------------------------
    // 路由节点就是循环的最后一个节点：work → check → { again: work, done: finish }，循环 [work, check]
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(new[] { "done" }, 1)]
    [InlineData(new[] { "again", "done" }, 2)]
    public async Task RouterIsTheLastLoopNode_ChoosingTheExit_EndsTheLoop(string[] verdicts, int expectedRounds)
    {
        var recorder = new ExecutionRecorder();
        var services = BuildServiceProvider(recorder, verdicts, reviewNodeType: "scripted-review");
        var nodes = new List<WorkflowStepDto>
        {
            new() { StepId = "work", Configuration = new() { ["nodeType"] = "echo-test" } },
            new() { StepId = "check", DependsOn = ["work"], Configuration = new() { ["nodeType"] = "scripted-review" } },
            new() { StepId = "finish", DependsOn = ["check"], Configuration = new() { ["nodeType"] = "echo-test" } }
        };
        var edges = new List<ConditionalEdge>
        {
            new()
            {
                FromNodeId = "check",
                Routes = new Dictionary<string, string> { ["again"] = "work", ["done"] = "finish" },
                DefaultTarget = "finish"
            }
        };
        var loops = new Dictionary<string, LoopDefinition>
        {
            ["L"] = new LoopDefinition { NodeIds = ["work", "check"], MaxIterations = 5 }
        };

        var result = await new WorkflowEngine(Mock.Of<ILogger<WorkflowEngine>>())
            .ExecuteAsync(new WorkflowGraph(nodes, edges, loops), "input", services);

        result.HasFailure.ShouldBeFalse();
        recorder.Count("work").ShouldBe(expectedRounds, "a loop that was left is not reset for another round");
        recorder.Count("check").ShouldBe(expectedRounds);
        recorder.Count("finish").ShouldBe(1);
    }

    // -----------------------------------------------------------------------
    // check（循环成员）→ { go-finish: finish（循环外）, go-work: work（循环内）}，循环 [check, work]
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ConditionalEdge_ExitSelected_InLoopAlternateDoesNotRun()
    {
        var result = await RunCheckWorkGraphAsync(checkEmits: "go-finish");

        result.StepResults.ShouldContain(r => r.StepId == "finish" && !r.Skipped);
        result.StepResults.ShouldNotContain(r => r.StepId == "work",
            "the router left the loop; the in-loop alternate is pruned like any other branch not taken");
        result.StepResults.Count(r => r.StepId == "check").ShouldBe(1, "a loop that was left does not iterate");
    }

    [Fact]
    public async Task ConditionalEdge_InLoopTargetSelected_RunsTheLoopBody()
    {
        var result = await RunCheckWorkGraphAsync(checkEmits: "go-work");

        // work 输出 loop_done=true，循环单轮后结束；本轮没被选的出口不执行。
        result.StepResults.ShouldContain(r => r.StepId == "work" && !r.Skipped);
        result.StepResults.ShouldNotContain(r => r.StepId == "finish");
    }

    [Fact]
    public async Task ConditionalEdge_NonLoopAlternateBranch_StillPruned()
    {
        var result = await RunCheckWorkGraphAsync(checkEmits: "go-finish", includeNonLoopBranch: true);

        // dead 是循环外的非目标分支 → 被裁剪，从未执行 → 不出现在 StepResults
        result.StepResults.ShouldNotContain(r => r.StepId == "dead",
            "non-loop alternate branch must still be pruned (force-completed, never executed)");
        result.StepResults.ShouldContain(r => r.StepId == "finish" && !r.Skipped);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static async Task<WorkflowEngineResult> RunReviewLoopAsync(ExecutionRecorder recorder, string[] verdicts, int maxIterations = 3)
    {
        var services = BuildServiceProvider(recorder, verdicts, reviewNodeType: "scripted-review");
        var nodes = new List<WorkflowStepDto>
        {
            new() { StepId = "worker", Configuration = new() { ["nodeType"] = "echo-test" } },
            new() { StepId = "review", DependsOn = ["worker"], Configuration = new() { ["nodeType"] = "scripted-review" } },
            new() { StepId = "rework", DependsOn = ["review"], Configuration = new() { ["nodeType"] = "echo-test" } },
            new() { StepId = "publish", DependsOn = ["review"], Configuration = new() { ["nodeType"] = "echo-test" } }
        };
        var edges = new List<ConditionalEdge>
        {
            new()
            {
                FromNodeId = "review",
                Routes = new Dictionary<string, string> { ["accept"] = "publish", ["rework"] = "rework" },
                DefaultTarget = "publish"
            }
        };
        var loops = new Dictionary<string, LoopDefinition>
        {
            ["review-loop"] = new LoopDefinition { NodeIds = ["review", "rework"], MaxIterations = maxIterations }
        };

        return await new WorkflowEngine(Mock.Of<ILogger<WorkflowEngine>>())
            .ExecuteAsync(new WorkflowGraph(nodes, edges, loops), "input", services);
    }

    /// <summary>
    /// 构建图：
    /// - check（循环成员，入口）：条件边路由 { "go-finish"→finish, "go-work"→work }，默认 finish
    /// - work（循环成员，循环体最后一个节点）：DependsOn check，输出 loop_done=true 让循环单次终止
    /// - finish（循环外）：DependsOn check
    /// - dead（可选，循环外备选支）：DependsOn check，Routes 中映射但非选中目标
    /// 循环 L = [check, work]。
    /// </summary>
    private static async Task<WorkflowEngineResult> RunCheckWorkGraphAsync(string checkEmits, bool includeNonLoopBranch = false)
    {
        var services = BuildServiceProvider(new ExecutionRecorder(), [], reviewNodeType: "scripted-review");

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

        return await new WorkflowEngine(Mock.Of<ILogger<WorkflowEngine>>()).ExecuteAsync(graph, "input", services);
    }

    private static IServiceProvider BuildServiceProvider(ExecutionRecorder recorder, string[] verdicts, string reviewNodeType)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(recorder);
        services.AddSingleton(new VerdictScript(verdicts));
        services.AddScoped<IWorkflowNode>(sp => new EchoTestNode(sp.GetRequiredService<ExecutionRecorder>(), "echo-test"));

        services.AddScoped<IWorkflowNode>(sp => new ScriptedReviewNode(
            sp.GetRequiredService<ExecutionRecorder>(), sp.GetRequiredService<VerdictScript>(), reviewNodeType));
        services.AddScoped<IWorkflowNode, LoopDoneEmitNode>();
        services.AddScoped<WorkflowNodeExecutor>();
        return services.BuildServiceProvider();
    }

    private sealed class ExecutionRecorder
    {
        private readonly List<string> _executions = [];

        public void Record(string stepId)
        {
            lock (_executions) _executions.Add(stepId);
        }

        public int Count(string stepId)
        {
            lock (_executions) return _executions.Count(id => id == stepId);
        }
    }

    /// <summary>审查结论脚本：第 n 次审查给第 n 个结论，用完后一直给最后一个。</summary>
    private sealed class VerdictScript(string[] verdicts)
    {
        private int _calls;

        public string Next()
        {
            var index = Interlocked.Increment(ref _calls) - 1;
            return verdicts.Length == 0 ? "accept" : verdicts[Math.Min(index, verdicts.Length - 1)];
        }
    }

    /// <summary>回显节点：有 Configuration["emit"] 时输出该文本，否则输出 ran-{StepId}。</summary>
    private sealed class EchoTestNode(ExecutionRecorder recorder, string nodeType) : IWorkflowNode
    {
        public string NodeType => nodeType;

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            recorder.Record(context.Step.StepId!);
            var emit = context.Step.Configuration?.GetValueOrDefault("emit") ?? $"ran-{context.Step.StepId}";
            return Task.FromResult(new WorkflowNodeResult { Output = emit, IsSuccess = true });
        }
    }

    /// <summary>审查节点替身：像 ReviewNode 一样把结论作为路由键（RouteTo）交给条件边。</summary>
    private sealed class ScriptedReviewNode(ExecutionRecorder recorder, VerdictScript script, string nodeType) : IWorkflowNode
    {
        public string NodeType => nodeType;

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            recorder.Record(context.Step.StepId!);
            var verdict = script.Next();
            return Task.FromResult(new WorkflowNodeResult { Output = $"verdict: {verdict}", IsSuccess = true, RouteTo = verdict });
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
