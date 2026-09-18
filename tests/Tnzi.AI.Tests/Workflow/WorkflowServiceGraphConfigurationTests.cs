namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// 条件边与循环必须能经 Create / Update DTO 落到 <c>WorkflowDefinition.Configuration</c>，并被执行引擎读到。
/// </summary>
/// <remarks>
/// ★ 此前 <c>ConditionalEdges</c> / <c>Loops</c> 只住在实体的 Configuration 列里，而没有任何 DTO、端点或编辑器写它：
/// 经 <c>POST /admin/workflows</c> 建的每一条工作流里，RouterNode 付一次 LLM 分类调用、返回 <c>RouteTo</c>，
/// 引擎在 <c>GetConditionalEdge == null</c> 处直接返回，所有分支都沿静态 DependsOn 跑完。
/// 代码优先的 <c>WorkflowBuilder.BuildGraph()</c> 路径能到，持久化路径一条都到不了。
/// </remarks>
public class WorkflowServiceGraphConfigurationTests : WorkflowServiceSqliteTestBase
{
    private readonly NodeRecorder _recorder = new();

    protected override void ConfigureWorkflowNodes(IServiceCollection services)
    {
        services.AddSingleton(_recorder);
        services.AddScoped<IWorkflowNode, EchoNode>();
        services.AddScoped<IWorkflowNode, RouteToANode>();
        services.AddScoped<IWorkflowNode>(_ => new NamedNode(WorkflowNodeTypes.Router));
    }

    private static CreateWorkflowDefinitionDto RoutedWorkflow(WorkflowGraphConfigurationDto? configuration) => new()
    {
        Name = "routed",
        ExecutionMode = WorkflowExecutionMode.Dag,
        Steps =
        [
            new WorkflowStepDto { StepId = "classify", Configuration = new() { ["nodeType"] = "route-to-a" } },
            new WorkflowStepDto { StepId = "handleA", DependsOn = ["classify"], Configuration = new() { ["nodeType"] = "echo" } },
            new WorkflowStepDto { StepId = "handleB", DependsOn = ["classify"], Configuration = new() { ["nodeType"] = "echo" } }
        ],
        Configuration = configuration
    };

    private static WorkflowGraphConfigurationDto ClassifyEdge() => new()
    {
        ConditionalEdges =
        [
            new WorkflowConditionalEdgeDto
            {
                FromNodeId = "classify",
                Routes = new Dictionary<string, string> { ["a"] = "handleA", ["b"] = "handleB" }
            }
        ]
    };

    [Fact]
    public async Task CreateAsync_WithConditionalEdges_PersistsConfiguration_AndTheEngineRoutesByIt()
    {
        var service = CreateService();

        var created = await service.CreateAsync(RoutedWorkflow(ClassifyEdge()));
        created.Succeeded.ShouldBeTrue(created.Message);
        created.Data!.Configuration.ShouldNotBeNull();
        created.Data.Configuration!.ConditionalEdges.ShouldHaveSingleItem().Routes["a"].ShouldBe("handleA");

        var row = await DbContext.Set<WorkflowDefinition>().AsNoTracking().SingleAsync(d => d.Id == created.Data.Id);
        row.Configuration.ShouldNotBeNullOrWhiteSpace();

        var run = await service.RunAsync(created.Data.Id, "start");
        run.Succeeded.ShouldBeTrue(run.Message);
        run.Data!.StepResults.ShouldNotBeNull();
        run.Data.StepResults!.Single(s => s.StepId == "handleA").Skipped.ShouldBeFalse();
        run.Data.StepResults.ShouldNotContain(s => s.StepId == "handleB" && !s.Skipped, "the router chose 'a'; the other branch must not run");
        _recorder.Executions.ShouldContain("handleA");
        _recorder.Executions.ShouldNotContain("handleB");
    }

    [Fact]
    public async Task CreateAsync_WithoutConfiguration_BothBranchesRun_AsBefore()
    {
        var service = CreateService();
        var created = await service.CreateAsync(RoutedWorkflow(null));

        var run = await service.RunAsync(created.Data!.Id, "start");

        run.Succeeded.ShouldBeTrue(run.Message);
        _recorder.Executions.ShouldContain("handleA");
        _recorder.Executions.ShouldContain("handleB");
    }

    [Fact]
    public async Task UpdateAsync_WithConfiguration_ReplacesIt_AndEmptyConfigurationClearsIt()
    {
        var service = CreateService();
        var created = await service.CreateAsync(RoutedWorkflow(null));

        var updated = await service.UpdateAsync(created.Data!.Id, new UpdateWorkflowDefinitionDto { Configuration = ClassifyEdge() });
        updated.Succeeded.ShouldBeTrue(updated.Message);
        updated.Data!.Configuration!.ConditionalEdges.ShouldHaveSingleItem();

        var cleared = await service.UpdateAsync(created.Data.Id, new UpdateWorkflowDefinitionDto { Configuration = new WorkflowGraphConfigurationDto() });
        cleared.Succeeded.ShouldBeTrue(cleared.Message);
        cleared.Data!.Configuration.ShouldBeNull();
        var row = await DbContext.Set<WorkflowDefinition>().AsNoTracking().SingleAsync(d => d.Id == created.Data.Id);
        row.Configuration.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateAsync_WithoutConfiguration_LeavesItUntouched()
    {
        var service = CreateService();
        var created = await service.CreateAsync(RoutedWorkflow(ClassifyEdge()));

        var updated = await service.UpdateAsync(created.Data!.Id, new UpdateWorkflowDefinitionDto { Name = "renamed" });

        updated.Data!.Configuration!.ConditionalEdges.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ValidateAsync_EdgeReferencingUnknownStep_ReturnsError()
    {
        var service = CreateService();
        var created = await service.CreateAsync(RoutedWorkflow(new WorkflowGraphConfigurationDto
        {
            ConditionalEdges =
            [
                new WorkflowConditionalEdgeDto
                {
                    FromNodeId = "nope",
                    Routes = new Dictionary<string, string> { ["a"] = "handleA", ["b"] = "missing" },
                    DefaultTarget = "alsoMissing"
                }
            ],
            Loops = new Dictionary<string, WorkflowLoopDto> { ["l1"] = new() { NodeIds = ["handleA", "ghost"] } }
        }));

        var validation = await service.ValidateAsync(created.Data!.Id);

        validation.Succeeded.ShouldBeTrue();
        validation.Data!.IsValid.ShouldBeFalse();
        validation.Data.Errors.ShouldContain(e => e.Contains("'nope'"));
        validation.Data.Errors.ShouldContain(e => e.Contains("'missing'"));
        validation.Data.Errors.ShouldContain(e => e.Contains("'alsoMissing'"));
        validation.Data.Errors.ShouldContain(e => e.Contains("'ghost'"));
    }

    [Fact]
    public async Task ValidateAsync_RouterWithoutEdge_ReturnsWarning()
    {
        var service = CreateService();
        var created = await service.CreateAsync(new CreateWorkflowDefinitionDto
        {
            Name = "router-no-edge",
            ExecutionMode = WorkflowExecutionMode.Dag,
            Steps =
            [
                new WorkflowStepDto { StepId = "classify", Configuration = new() { ["nodeType"] = WorkflowNodeTypes.Router } },
                new WorkflowStepDto { StepId = "handleA", DependsOn = ["classify"], Configuration = new() { ["nodeType"] = "echo" } }
            ]
        });

        var validation = await service.ValidateAsync(created.Data!.Id);

        validation.Data!.IsValid.ShouldBeTrue();
        validation.Data.Warnings.ShouldContain(w => w.Contains("classify") && w.Contains("conditional edge"));
    }

    private sealed class NodeRecorder
    {
        public List<string> Executions { get; } = [];
    }

    private sealed class EchoNode(NodeRecorder recorder) : IWorkflowNode
    {
        public string NodeType => "echo";
        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            recorder.Executions.Add(context.Step.StepId!);
            return Task.FromResult(new WorkflowNodeResult { Output = $"ran-{context.Step.StepId}", IsSuccess = true });
        }
    }

    private sealed class RouteToANode(NodeRecorder recorder) : IWorkflowNode
    {
        public string NodeType => "route-to-a";
        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            recorder.Executions.Add(context.Step.StepId!);
            return Task.FromResult(new WorkflowNodeResult { Output = "a", IsSuccess = true, RouteTo = "a" });
        }
    }

    private sealed class NamedNode(string nodeType) : IWorkflowNode
    {
        public string NodeType => nodeType;
        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
