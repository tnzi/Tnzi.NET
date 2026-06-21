namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// WorkflowService.RunAsync / RunStreamingAsync 执行驱动测试 — 覆盖两条新行为：
/// (1) HITL guard — 含 approval/interrupt 语义节点的非 DAG 工作流 fail-fast，不静默挂死；
/// (2) Observability — 所有执行模式（含 Sequential）都创建 WorkflowExecution 行，
///     使 GetExecutions / watchdog 可见。
/// </summary>
public class WorkflowServiceExecutionTests
{
    private readonly Mock<IRepository<WorkflowDefinition, Guid>> _definitionRepository = new();
    private readonly Mock<IRepository<WorkflowExecution, Guid>> _executionRepository = new();
    private readonly Mock<IRepository<WorkflowDefinitionVersion, Guid>> _versionRepository = new();
    private readonly Mock<IRepository<AgentRun, Guid>> _runRepository = new();
    private readonly Mock<IRepository<Agent, Guid>> _agentRepository = new();
    private readonly List<WorkflowExecution> _executions = [];
    private readonly IServiceProvider _serviceProvider;

    public WorkflowServiceExecutionTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // 注册一个最简 agent 节点 + 执行器，供真实 WorkflowEngine 驱动 Sequential 图。
        services.AddScoped<IWorkflowNode, EchoAgentNode>();
        services.AddScoped<WorkflowNodeExecutor>();
        _serviceProvider = services.BuildServiceProvider();

        // _executionRepository 由内存 List 支撑：Insert 追加、AsQueryable 投影、
        // FirstOrDefault/Update 按 ExecutionId 匹配。
        _executionRepository
            .Setup(r => r.InsertAsync(It.IsAny<WorkflowExecution>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowExecution, CancellationToken>((e, _) =>
            {
                if (e.Id == Guid.Empty) e.Id = Guid.NewGuid();
                _executions.Add(e);
            })
            .Returns(Task.CompletedTask);

        _executionRepository
            .Setup(r => r.UpdateAsync(It.IsAny<WorkflowExecution>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _executionRepository
            .Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<WorkflowExecution, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<WorkflowExecution, bool>> pred, CancellationToken _) =>
                _executions.AsQueryable().FirstOrDefault(pred));

        _executionRepository
            .Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(() => _executions.BuildMock());
        _executionRepository
            .Setup(r => r.AsQueryable())
            .Returns(() => _executions.BuildMock());
    }

    // -----------------------------------------------------------------------
    // (1) HITL guard
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(WorkflowExecutionMode.Sequential)]
    [InlineData(WorkflowExecutionMode.Parallel)]
    public async Task RunAsync_ApprovalNode_NonDagMode_FailsFast(WorkflowExecutionMode mode)
    {
        var def = MakeDefinition(mode, """
            [
              { "stepId": "a", "configuration": { "nodeType": "agent" } },
              { "stepId": "b", "configuration": { "nodeType": "approval" } }
            ]
            """);
        SetupDefinition(def);

        var result = await CreateService().RunAsync(def.Id, "hello");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldBe("HITL nodes require DAG execution mode");
        // fail-fast → no execution row created, engine never invoked
        _executions.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_RequiresApprovalFlag_NonDagMode_FailsFast()
    {
        var def = MakeDefinition(WorkflowExecutionMode.Sequential, """
            [
              { "stepId": "a", "requiresApproval": true, "configuration": { "nodeType": "agent" } }
            ]
            """);
        SetupDefinition(def);

        var result = await CreateService().RunAsync(def.Id, "hello");

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe("HITL nodes require DAG execution mode");
    }

    [Fact]
    public async Task RunAsync_DagMode_NotBlockedByHitlGuard()
    {
        // DAG mode is always allowed (checkpoint store wired for pause/resume); the HITL
        // guard short-circuits on DAG before inspecting steps, so even an approval-bearing
        // DAG workflow is never fail-fast rejected here.
        var def = MakeDefinition(WorkflowExecutionMode.Dag, """
            [
              { "stepId": "a", "configuration": { "nodeType": "agent" } }
            ]
            """);
        SetupDefinition(def);

        var result = await CreateService().RunAsync(def.Id, "hello");

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    [Fact]
    public async Task RunStreamingAsync_ApprovalNode_NonDagMode_Throws()
    {
        var def = MakeDefinition(WorkflowExecutionMode.Sequential, """
            [
              { "stepId": "b", "configuration": { "nodeType": "approval" } }
            ]
            """);
        SetupDefinition(def);

        var ex = await Should.ThrowAsync<Tnzi.Exceptions.BusinessException>(async () =>
        {
            await foreach (var _ in CreateService().RunStreamingAsync(def.Id, "hello")) { }
        });

        ex.Message.ShouldBe("HITL nodes require DAG execution mode");
    }

    // -----------------------------------------------------------------------
    // (2) Observability — Sequential/Parallel also create WorkflowExecution rows
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_SequentialMode_CreatesExecutionRow_QueryableAfterwards()
    {
        var def = MakeDefinition(WorkflowExecutionMode.Sequential, """
            [
              { "stepId": "a", "configuration": { "nodeType": "agent" } },
              { "stepId": "b", "configuration": { "nodeType": "agent" } }
            ]
            """);
        SetupDefinition(def);

        var service = CreateService();
        var runResult = await service.RunAsync(def.Id, "hello");
        runResult.Succeeded.ShouldBeTrue(runResult.Message);
        runResult.Data!.ExecutionId.ShouldNotBeNullOrWhiteSpace();

        // The execution must be visible via GetExecutions (watchdog / history observability)
        var listResult = await service.GetExecutionsAsync(new WorkflowExecutionQueryDto());
        listResult.Succeeded.ShouldBeTrue(listResult.Message);
        listResult.Data!.TotalCount.ShouldBe(1);

        var row = listResult.Data.Items.Single();
        row.ExecutionId.ShouldBe(runResult.Data.ExecutionId);
        row.WorkflowDefinitionId.ShouldBe(def.Id);
        // Terminal status recorded (Running → Completed), with start/duration tracked.
        row.Status.ShouldBe(WorkflowExecutionStatus.Completed);
        _executions.Single().StartedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task RunAsync_ParallelMode_CreatesExecutionRow()
    {
        var def = MakeDefinition(WorkflowExecutionMode.Parallel, """
            [ { "stepId": "a", "configuration": { "nodeType": "agent" } } ]
            """);
        SetupDefinition(def);

        var result = await CreateService().RunAsync(def.Id, "hello");

        result.Succeeded.ShouldBeTrue(result.Message);
        _executions.Count.ShouldBe(1);
        _executions.Single().Status.ShouldBe(WorkflowExecutionStatus.Completed);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private WorkflowService CreateService() => new(
        _definitionRepository.Object,
        _executionRepository.Object,
        _versionRepository.Object,
        _runRepository.Object,
        _agentRepository.Object,
        Mock.Of<IUsageLogService>(),
        Mock.Of<IQuotaService>(),
        Mock.Of<IWorkflowCheckpointStore>(),
        new WorkflowEngine(Mock.Of<ILogger<WorkflowEngine>>()),
        _serviceProvider);

    private void SetupDefinition(WorkflowDefinition def)
    {
        _definitionRepository
            .Setup(r => r.GetAsync(def.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(def);
    }

    private static WorkflowDefinition MakeDefinition(WorkflowExecutionMode mode, string stepsJson) => new()
    {
        Id = Guid.NewGuid(),
        Name = "test-wf",
        ExecutionMode = mode,
        IsEnabled = true,
        Steps = stepsJson
    };

    /// <summary>最简 agent 节点：回显输入，供 WorkflowEngine 驱动 Sequential 图完成。</summary>
    private sealed class EchoAgentNode : IWorkflowNode
    {
        public string NodeType => WorkflowNodeTypes.Agent;

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkflowNodeResult { Output = "ok", IsSuccess = true });
    }
}
