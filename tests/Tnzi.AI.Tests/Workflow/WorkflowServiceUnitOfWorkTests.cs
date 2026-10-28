using Tnzi.AspNetCore.Mvc.Filters;

namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// Workflow execution inside an ambient unit of work (what <c>EnableGlobalUnitOfWork</c> gives
/// every request that does not opt out, and what an in-process caller may already be in).
/// </summary>
/// <remarks>
/// <para>
/// Inside a transaction the repositories defer their writes, while everything that follows the
/// execution insert reads the row back with a database query. The checkpoint store therefore
/// found nothing and inserted the same ExecutionId again after every DAG layer, so the commit
/// failed on the unique index after every LLM call had been paid for; Sequential runs committed
/// with the row still Running, for the watchdog to mark timed out.
/// </para>
/// <para>
/// These cases need the real EF repository and a real UnitOfWorkManager: a mocked repository
/// neither defers nor enforces the index.
/// </para>
/// </remarks>
public class WorkflowServiceUnitOfWorkTests : WorkflowServiceSqliteTestBase
{
    private const string MeteredType = "uow-metered-test";

    private IUnitOfWorkManager Manager => ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var entityManager = new Mock<IEntityManager>();
        entityManager.Setup(m => m.GetAllDbContextTypes()).Returns([typeof(WorkflowServiceDbContext)]);
        services.AddSingleton(_ => entityManager.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
    }

    protected override void ConfigureWorkflowNodes(IServiceCollection services)
    {
        services.AddScoped<IWorkflowNode, MeteredNode>();
        services.AddScoped<IWorkflowNode, ApprovalNode>();
    }

    private const string TwoLayers = """
        [
          { "stepId": "a", "configuration": { "nodeType": "uow-metered-test" } },
          { "stepId": "b", "dependsOn": ["a"], "configuration": { "nodeType": "uow-metered-test" } }
        ]
        """;

    [Fact]
    public async Task DagRun_InsideATransaction_CommitsOneCompletedExecution()
    {
        var definition = await InsertDefinitionAsync(TwoLayers, WorkflowExecutionMode.Dag);

        Manager.EnableTransaction();
        var result = await CreateService().RunAsync(definition.Id, "start");
        result.Succeeded.ShouldBeTrue(result.Message);
        await Manager.CommitTransactionAsync();

        await AssertSingleCompletedExecutionAsync(result.Data!.ExecutionId!);
    }

    [Theory]
    [InlineData(WorkflowExecutionMode.Sequential)]
    [InlineData(WorkflowExecutionMode.Parallel)]
    public async Task NonDagRun_InsideATransaction_RecordsTheTerminalStatus(WorkflowExecutionMode mode)
    {
        var definition = await InsertDefinitionAsync(TwoLayers, mode);

        Manager.EnableTransaction();
        var result = await CreateService().RunAsync(definition.Id, "start");
        result.Succeeded.ShouldBeTrue(result.Message);
        await Manager.CommitTransactionAsync();

        await AssertSingleCompletedExecutionAsync(result.Data!.ExecutionId!);
    }

    [Fact]
    public async Task ApproveThenResume_EachInsideATransaction_CompletesTheSameExecution()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "draft", "configuration": { "nodeType": "uow-metered-test" } },
              { "stepId": "gate", "dependsOn": ["draft"], "configuration": { "nodeType": "approval" } },
              { "stepId": "publish", "dependsOn": ["gate"], "configuration": { "nodeType": "uow-metered-test" } }
            ]
            """);
        var service = CreateService();

        Manager.EnableTransaction();
        var run = await service.RunAsync(definition.Id, "start");
        await Manager.CommitTransactionAsync();
        run.Data!.Status.ShouldBe("AwaitingApproval");
        var executionId = run.Data.ExecutionId!;

        Manager.EnableTransaction();
        (await service.ApproveStepAsync(executionId, "gate")).Succeeded.ShouldBeTrue();
        await Manager.CommitTransactionAsync();

        Manager.EnableTransaction();
        var resumed = await service.ResumeAsync(executionId);
        resumed.Succeeded.ShouldBeTrue(resumed.Message);
        await Manager.CommitTransactionAsync();

        resumed.Data!.Status.ShouldBe("Completed");
        await AssertSingleCompletedExecutionAsync(executionId);
    }

    [Fact]
    public async Task CheckpointStore_InsideATransaction_ReadsBackWhatItJustSaved()
    {
        var store = ServiceProvider.GetRequiredService<IWorkflowCheckpointStore>();
        var executionId = Guid.NewGuid().ToString("N");

        Manager.EnableTransaction();
        await store.SaveCheckpointAsync(new WorkflowCheckpoint
        {
            ExecutionId = executionId,
            InitialInput = "start",
            Status = WorkflowExecutionStatus.Paused
        });
        (await store.GetCheckpointAsync(executionId)).ShouldNotBeNull("the next save looks the row up the same way; not finding it inserts a duplicate");

        await store.SaveCheckpointAsync(new WorkflowCheckpoint
        {
            ExecutionId = executionId,
            InitialInput = "start",
            Status = WorkflowExecutionStatus.Running
        });
        (await store.GetCheckpointAsync(executionId))!.Status.ShouldBe(WorkflowExecutionStatus.Running);
        await Manager.CommitTransactionAsync();

        DbContext.ChangeTracker.Clear();
        (await DbContext.Set<WorkflowExecution>().CountAsync(e => e.ExecutionId == executionId)).ShouldBe(1);
    }

    [Theory]
    [InlineData(nameof(DefaultWorkflowAdminController.Run))]
    [InlineData(nameof(DefaultWorkflowAdminController.RunStreaming))]
    [InlineData(nameof(DefaultWorkflowAdminController.ResumeExecution))]
    [InlineData(nameof(DefaultWorkflowAdminController.ResumeWithInput))]
    public void RunAndResumeEndpoints_StayOutOfTheRequestUnitOfWork(string action)
    {
        // A run takes minutes; its execution row, checkpoints and status must be visible to the
        // status, cancel, heartbeat and watchdog paths while it runs, not at the end of the request.
        var method = typeof(DefaultWorkflowAdminController).GetMethod(action)!;

        method.GetCustomAttributes(typeof(UnitOfWorkAttribute), inherit: true)
            .Cast<UnitOfWorkAttribute>()
            .ShouldContain(a => a.IsDisabled);
    }

    private async Task AssertSingleCompletedExecutionAsync(string executionId)
    {
        DbContext.ChangeTracker.Clear();
        var rows = await DbContext.Set<WorkflowExecution>().AsNoTracking()
            .Where(e => e.ExecutionId == executionId)
            .ToListAsync();

        rows.Count.ShouldBe(1);
        rows[0].Status.ShouldBe(WorkflowExecutionStatus.Completed);
        rows[0].CompletedTime.ShouldNotBeNull();
    }

    private sealed class MeteredNode : IWorkflowNode
    {
        public string NodeType => MeteredType;

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkflowNodeResult
            {
                Output = $"{context.Step.StepId}-out",
                IsSuccess = true,
                Usage = new TokenUsageDto { InputTokens = 10, OutputTokens = 5, TotalTokens = 15 }
            });
    }
}
