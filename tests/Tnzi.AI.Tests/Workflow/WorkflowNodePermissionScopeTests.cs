namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// 按工作流节点（<c>WorkflowNodeName</c>）与「仅工作流」（<c>IsWorkflowOnly</c>）限定的权限规则必须在运行时真的生效。
/// </summary>
/// <remarks>
/// <para>
/// 评估器要求规则的节点名与上下文的节点名相等才命中，而此前没有任何代码在节点执行期间提供节点名：
/// 「在 publish-step 节点里禁止 bash」这条规则永不命中，bash 照常执行；管理端的模拟评估却显示 Deny。
/// 直接执行工作流（管理端 / <c>IWorkflowService.RunAsync</c>）时连运行请求都没有，<c>IsWorkflowOnly</c> 规则同样失效。
/// </para>
/// <para>
/// 这里用真实的 <see cref="WorkflowEngine"/> + <see cref="WorkflowNodeExecutor"/> 跑图，节点里调用的是
/// 真实的 <see cref="ApprovalToolWrapper"/>（AgentExecutor 给工具套的就是它），只把 LLM 换成直接调用工具。
/// </para>
/// </remarks>
public class WorkflowNodePermissionScopeTests
{
    private const string ProbeNodeType = "permission-probe-test";

    [Fact]
    public async Task NodeScopedDeny_BlocksTheToolInThatNode_AndNotInAParallelSibling()
    {
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "bash",
                WorkflowNodeName = "publish-step",
                Behavior = PermissionBehavior.Deny,
                Reason = "No shell while publishing"
            }
        ]);

        // 两个节点没有依赖，在同一层里并发执行：各自只能看到自己的节点名
        var result = await RunAsync(evaluator, Step("publish-step"), Step("build-step"));

        Output(result, "publish-step").ShouldBe("Tool call rejected: No shell while publishing");
        Output(result, "build-step").ShouldBe("ran");
    }

    [Fact]
    public async Task WorkflowOnlyDeny_AppliesWhenTheWorkflowRunsDirectly()
    {
        // 管理端直接执行工作流：没有运行请求，此前 IsWorkflowRun 恒为 false
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "bash",
                IsWorkflowOnly = true,
                Behavior = PermissionBehavior.Deny,
                Reason = "No shell in workflows"
            }
        ]);

        var result = await RunAsync(evaluator, Step("any-step"));

        Output(result, "any-step").ShouldBe("Tool call rejected: No shell in workflows");
    }

    [Fact]
    public async Task OutsideAnyNode_TheToolIsNotTreatedAsAWorkflowCall()
    {
        var evaluator = NodeScopedAllowOverGlobalDeny();
        var wrapper = CreateWrapper(evaluator, new AgentExecutionContextAccessor());

        var result = await InvokeAsync(wrapper);

        result.ShouldBe("Tool call rejected: Shell is off");
    }

    [Fact]
    public async Task RequestMetadata_CannotClaimAWorkflowNode()
    {
        // 能往运行请求元数据里写 node_name 的调用方（例如管理端 spawn 的 metadata）不能借此让按节点放行的规则对自己生效
        var evaluator = NodeScopedAllowOverGlobalDeny();
        var accessor = new AgentExecutionContextAccessor
        {
            CurrentRequest = new AgentRunRequest
            {
                Metadata = new Dictionary<string, object>
                {
                    ["node_name"] = "build-step",
                    ["workflow_execution_id"] = "forged"
                }
            }
        };

        var result = await InvokeAsync(CreateWrapper(evaluator, accessor));

        result.ShouldBe("Tool call rejected: Shell is off");
    }

    [Fact]
    public async Task InsideANode_TheNodeScopedAllowApplies()
    {
        var result = await RunAsync(NodeScopedAllowOverGlobalDeny(), Step("build-step"));

        Output(result, "build-step").ShouldBe("ran");
    }

    [Fact]
    public async Task ARunStartedFromInsideANode_CannotEscapeTheNodesDeny()
    {
        // 节点里的 agent 把活委派给子 Agent（agent-as-tool / spawn_agent 的后台运行沿执行流起步、换了运行请求）：
        // 子运行的工具调用仍属于这个节点，按节点限定的拒绝不能靠委派绕过去
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "bash",
                WorkflowNodeName = "publish-step",
                Behavior = PermissionBehavior.Deny,
                Reason = "No shell while publishing"
            }
        ]);
        var accessor = new AgentExecutionContextAccessor { CurrentRequest = new AgentRunRequest() };

        using (WorkflowNodeScope.Enter(Guid.NewGuid(), "exec-1", "publish-step"))
        {
            var child = Task.Run(async () =>
            {
                accessor.CurrentRequest = new AgentRunRequest { ParentRunId = Guid.NewGuid() };
                return await InvokeAsync(CreateWrapper(evaluator, accessor));
            });

            (await child).ShouldBe("Tool call rejected: No shell while publishing");
        }
    }

    [Fact]
    public void Scope_IsRestoredWhenTheNodeEnds()
    {
        WorkflowNodeScope.Current.ShouldBeNull();

        using (WorkflowNodeScope.Enter(null, "exec-1", "outer"))
        {
            using (WorkflowNodeScope.Enter(null, "exec-1", "inner"))
            {
                WorkflowNodeScope.Current!.NodeName.ShouldBe("inner");
            }

            WorkflowNodeScope.Current!.NodeName.ShouldBe("outer");
        }

        WorkflowNodeScope.Current.ShouldBeNull();
    }

    [Fact]
    public async Task Engine_LeavesNoNodeScopeBehindAfterTheRun()
    {
        await RunAsync(NodeScopedAllowOverGlobalDeny(), Step("build-step"));

        WorkflowNodeScope.Current.ShouldBeNull();
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>全局禁 bash，只在 build-step 节点里放行（优先级更高）。</summary>
    private static ToolPermissionEvaluator NodeScopedAllowOverGlobalDeny() => new(
    [
        new ToolPermissionRule
        {
            ToolPattern = "bash",
            Behavior = PermissionBehavior.Deny,
            Reason = "Shell is off",
            Priority = 0
        },
        new ToolPermissionRule
        {
            ToolPattern = "bash",
            WorkflowNodeName = "build-step",
            Behavior = PermissionBehavior.Allow,
            Priority = 10
        }
    ]);

    private static ApprovalToolWrapper CreateWrapper(IToolPermissionEvaluator evaluator, IAgentExecutionContextAccessor accessor)
        => new(
            AIFunctionFactory.Create(() => "ran", "bash", "Run a shell command"),
            approvalHandler: null,
            new ToolApprovalOptions(),
            evaluator,
            executionContextAccessor: accessor,
            toolGroup: "sandbox");

    /// <summary>被包装的工具返回值经 JSON 编组（JsonElement），拒绝时是包装器自己的字符串。</summary>
    private static async Task<string?> InvokeAsync(ApprovalToolWrapper wrapper, CancellationToken cancellationToken = default)
    {
        var result = await wrapper.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()), cancellationToken);
        return result switch
        {
            JsonElement json => json.GetString(),
            string value => value,
            _ => result?.ToString()
        };
    }

    private static WorkflowStepDto Step(string stepId) => new()
    {
        StepId = stepId,
        Configuration = new Dictionary<string, string> { ["nodeType"] = ProbeNodeType }
    };

    private static string? Output(WorkflowEngineResult result, string stepId)
        => result.StepResults.Single(r => r.StepId == stepId).Output;

    private static async Task<WorkflowEngineResult> RunAsync(IToolPermissionEvaluator evaluator, params WorkflowStepDto[] steps)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(evaluator);
        services.AddSingleton<IAgentExecutionContextAccessor, AgentExecutionContextAccessor>();
        services.AddScoped<IWorkflowNode, ToolCallingProbeNode>();
        services.AddScoped<WorkflowNodeExecutor>();
        var provider = services.BuildServiceProvider();

        var engine = new WorkflowEngine(Mock.Of<ILogger<WorkflowEngine>>());
        var result = await engine.ExecuteAsync(new WorkflowGraph(steps), "input", provider,
            new WorkflowExecutionOptions { WorkflowDefinitionId = Guid.NewGuid() });

        result.HasFailure.ShouldBeFalse();
        return result;
    }

    /// <summary>节点里的 agent 调一次 bash：与 AgentExecutor 一样经 ApprovalToolWrapper 走权限评估。</summary>
    private sealed class ToolCallingProbeNode(IToolPermissionEvaluator evaluator, IAgentExecutionContextAccessor accessor) : IWorkflowNode
    {
        public string NodeType => ProbeNodeType;

        public async Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            // 让两个并发节点在工具调用前交错，暴露共享状态被互相覆盖的问题
            await Task.Yield();
            var output = await InvokeAsync(CreateWrapper(evaluator, accessor), cancellationToken);
            return new WorkflowNodeResult { Output = output ?? string.Empty, IsSuccess = true };
        }
    }
}
