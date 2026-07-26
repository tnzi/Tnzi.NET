namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// WorkflowService.GetExecutionsAsync 测试 - 验证分页查询返回真实的
/// CompletedStepCount / AwaitingApprovalCount（由 JSON 列反序列化得出，
/// 而非旧实现的 JSON 字符串字符数冒充）。
/// </summary>
public class WorkflowServiceExecutionQueryTests
{
    private readonly Mock<IRepository<WorkflowDefinition, Guid>> _definitionRepository = new();
    private readonly Mock<IRepository<WorkflowExecution, Guid>> _executionRepository = new();
    private readonly Mock<IRepository<WorkflowDefinitionVersion, Guid>> _versionRepository = new();
    private readonly Mock<IRepository<AgentRun, Guid>> _runRepository = new();
    private readonly Mock<IRepository<Agent, Guid>> _agentRepository = new();
    private readonly IServiceProvider _serviceProvider;

    public WorkflowServiceExecutionQueryTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task GetExecutionsAsync_ReturnsRealStepCounts_FromJsonColumns()
    {
        var executions = new List<WorkflowExecution>
        {
            MakeExecution(
                executionId: "exec-counts",
                completedSteps: """["step-a","step-b","step-c"]""",
                stepsAwaitingApproval: """["step-x","step-y"]"""),
            MakeExecution(
                executionId: "exec-empty",
                completedSteps: "[]",
                stepsAwaitingApproval: "[]")
        };
        SetupQueryable(executions);

        var result = await CreateService().GetExecutionsAsync(new WorkflowExecutionQueryDto());

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldNotBeNull();
        result.Data.TotalCount.ShouldBe(2);

        var withSteps = result.Data.Items.Single(i => i.ExecutionId == "exec-counts");
        withSteps.CompletedStepCount.ShouldBe(3);
        withSteps.AwaitingApprovalCount.ShouldBe(2);

        var empty = result.Data.Items.Single(i => i.ExecutionId == "exec-empty");
        empty.CompletedStepCount.ShouldBe(0);
        empty.AwaitingApprovalCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetExecutionsAsync_MalformedJson_FallsBackToZero()
    {
        var executions = new List<WorkflowExecution>
        {
            MakeExecution(
                executionId: "exec-bad-json",
                completedSteps: "{not valid json",
                stepsAwaitingApproval: "also not json")
        };
        SetupQueryable(executions);

        var result = await CreateService().GetExecutionsAsync(new WorkflowExecutionQueryDto());

        result.Succeeded.ShouldBeTrue(result.Message);
        var item = result.Data!.Items.Single();
        item.CompletedStepCount.ShouldBe(0);
        item.AwaitingApprovalCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetExecutionsAsync_FilterByStatus_AppliesFilterAndKeepsPagingMetadata()
    {
        var executions = new List<WorkflowExecution>
        {
            MakeExecution("exec-running", "[]", "[]", WorkflowExecutionStatus.Running),
            MakeExecution("exec-completed", """["a"]""", "[]", WorkflowExecutionStatus.Completed)
        };
        SetupQueryable(executions);

        var result = await CreateService().GetExecutionsAsync(new WorkflowExecutionQueryDto
        {
            Status = WorkflowExecutionStatus.Completed
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.TotalCount.ShouldBe(1);
        var item = result.Data.Items.Single();
        item.ExecutionId.ShouldBe("exec-completed");
        item.CompletedStepCount.ShouldBe(1);
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

    private void SetupQueryable(List<WorkflowExecution> data)
    {
        var mock = data.BuildMock();
        _executionRepository.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mock);
        _executionRepository.Setup(r => r.AsQueryable()).Returns(mock);
    }

    private static WorkflowExecution MakeExecution(
        string executionId,
        string completedSteps,
        string stepsAwaitingApproval,
        WorkflowExecutionStatus status = WorkflowExecutionStatus.Running) => new()
    {
        Id = Guid.NewGuid(),
        ExecutionId = executionId,
        WorkflowDefinitionId = Guid.NewGuid(),
        Status = status,
        CompletedSteps = completedSteps,
        StepsAwaitingApproval = stepsAwaitingApproval,
        StartedAt = DateTime.UtcNow.AddMinutes(-5),
        CreationTime = DateTime.UtcNow.AddMinutes(-5),
        UpdatedTime = DateTime.UtcNow
    };
}
