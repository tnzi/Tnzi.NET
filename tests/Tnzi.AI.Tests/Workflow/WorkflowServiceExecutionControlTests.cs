namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// Cancel and the resume claim against the real execution row, the real mailbox and the real
/// database checkpoint store, with and without an ambient transaction.
/// </summary>
/// <remarks>
/// <para>
/// Cancelling a running execution enqueued the cancel signal (the mailbox re-reads the row and
/// saves it), then wrote back the copy of the row read before the enqueue. Without a transaction
/// that copy carried the old concurrency stamp and the save failed with a 500, although the signal
/// was stored; inside a transaction both writes were deferred and the stale copy overwrote the
/// pending signal list, so the call answered 200 and the engine never saw the signal.
/// </para>
/// <para>
/// The resume claim checked the status of one read and swapped on the concurrency stamp of
/// another. A concurrent resume committing between the two reads left the second read Running
/// with a fresh stamp, and swapping on that stamp succeeded: both requests ran the engine.
/// </para>
/// </remarks>
public class WorkflowServiceExecutionControlTests : WorkflowServiceSqliteTestBase
{
    private readonly ExecutionCounter _counter = new();
    private Func<string, Task>? _afterCheckpointRead;

    private IUnitOfWorkManager Manager => ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        services.AddScoped<IWorkflowExecutionMailbox, WorkflowExecutionMailboxService>();

        var entityManager = new Mock<IEntityManager>();
        entityManager.Setup(m => m.GetAllDbContextTypes()).Returns([typeof(WorkflowServiceDbContext)]);
        services.AddSingleton(_ => entityManager.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();

        // Registered last, so it is the store the service resolves.
        services.AddScoped<DatabaseWorkflowCheckpointStore>();
        services.AddScoped<IWorkflowCheckpointStore>(sp => new RacingCheckpointStore(
            sp.GetRequiredService<DatabaseWorkflowCheckpointStore>(), () => _afterCheckpointRead));
    }

    protected override void ConfigureWorkflowNodes(IServiceCollection services)
    {
        services.AddSingleton(_counter);
        services.AddScoped<IWorkflowNode, CountingAgentNode>();
        services.AddScoped<IWorkflowNode, CountingHumanInputNode>();
        services.AddScoped<IWorkflowNode, ApprovalNode>();
    }

    // -----------------------------------------------------------------------
    // Cancel
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Cancel_RunningExecution_StoresTheCancelSignal()
    {
        var executionId = await InsertExecutionAsync(WorkflowExecutionStatus.Running);

        var result = await CreateService().CancelAsync(executionId, "stop");

        result.Succeeded.ShouldBeTrue(result.Message);
        await AssertCancelRequestedAsync(executionId);
    }

    [Fact]
    public async Task Cancel_RunningExecution_InsideATransaction_KeepsTheCancelSignal()
    {
        var executionId = await InsertExecutionAsync(WorkflowExecutionStatus.Running);

        Manager.EnableTransaction();
        var result = await CreateService().CancelAsync(executionId, "stop");
        result.Succeeded.ShouldBeTrue(result.Message);
        await Manager.CommitTransactionAsync();

        await AssertCancelRequestedAsync(executionId);
    }

    [Fact]
    public async Task Cancel_RunningExecutionWithAnotherSignalPending_StillSaysCancelRequested()
    {
        var executionId = await InsertExecutionAsync(WorkflowExecutionStatus.Running);
        var mailbox = ServiceProvider.GetRequiredService<IWorkflowExecutionMailbox>();
        await mailbox.EnqueueSignalAsync(executionId, new WorkflowExecutionSignal { Type = WorkflowExecutionSignalTypes.ResumeInput, StepId = "x" });

        var result = await CreateService().CancelAsync(executionId, "stop");

        result.Succeeded.ShouldBeTrue(result.Message);
        var row = await LoadExecutionAsync(executionId);
        row!.PendingSignalCount.ShouldBe(2);
        row.CurrentWaitReason.ShouldBe("cancel_requested", "a cancel outranks the reason an earlier signal left behind");
    }

    // -----------------------------------------------------------------------
    // Resume claim
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Resume_AnotherResumeClaimsTheRowBetweenTheReads_Returns409AndDoesNotRunTheEngine()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "gate", "configuration": { "nodeType": "approval" } },
              { "stepId": "publish", "dependsOn": ["gate"], "configuration": { "nodeType": "agent" } }
            ]
            """);
        var service = CreateService();
        var run = await service.RunAsync(definition.Id, "start");
        run.Data!.Status.ShouldBe("AwaitingApproval");
        var executionId = run.Data.ExecutionId!;
        (await service.ApproveStepAsync(executionId, "gate")).Succeeded.ShouldBeTrue();

        _afterCheckpointRead = ClaimTheRowAsTheWinnerAsync;
        var resumed = await service.ResumeAsync(executionId);

        resumed.Succeeded.ShouldBeFalse();
        resumed.Code.ShouldBe(409);
        _counter.Count("publish").ShouldBe(0, "only the request that claimed the row runs the engine");
    }

    [Fact]
    public async Task ResumeWithInput_AnotherResumeClaimsTheRowBetweenTheReads_Returns409AndDoesNotRunTheEngine()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "x", "configuration": { "nodeType": "counting_human_input" } },
              { "stepId": "b", "dependsOn": ["x"], "configuration": { "nodeType": "agent" } }
            ]
            """);
        var service = CreateService();
        var run = await service.RunAsync(definition.Id, "start");
        run.Data!.Status.ShouldBe("AwaitingInput");
        var executionId = run.Data.ExecutionId!;

        _afterCheckpointRead = ClaimTheRowAsTheWinnerAsync;
        var resumed = await service.ResumeWithInputAsync(executionId, "x", new Dictionary<string, object> { ["answer"] = "42" });

        resumed.Succeeded.ShouldBeFalse();
        resumed.Code.ShouldBe(409);
        _counter.Count("x").ShouldBe(0, "only the request that claimed the row runs the engine");
        _counter.Count("b").ShouldBe(0);
    }

    [Fact]
    public async Task Resume_NoConcurrentClaim_RunsTheEngine()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "gate", "configuration": { "nodeType": "approval" } },
              { "stepId": "publish", "dependsOn": ["gate"], "configuration": { "nodeType": "agent" } }
            ]
            """);
        var service = CreateService();
        var run = await service.RunAsync(definition.Id, "start");
        var executionId = run.Data!.ExecutionId!;
        (await service.ApproveStepAsync(executionId, "gate")).Succeeded.ShouldBeTrue();

        var resumed = await service.ResumeAsync(executionId);

        resumed.Succeeded.ShouldBeTrue(resumed.Message);
        resumed.Data!.Status.ShouldBe("Completed");
        _counter.Count("publish").ShouldBe(1);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// What the winning resume's claim leaves behind: the row is Running under a stamp the loser
    /// has not seen. Runs once, outside the change tracker, like a commit from another request.
    /// </summary>
    private async Task ClaimTheRowAsTheWinnerAsync(string executionId)
    {
        _afterCheckpointRead = null;
        await DbContext.Set<WorkflowExecution>()
            .Where(e => e.ExecutionId == executionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, WorkflowExecutionStatus.Running)
                .SetProperty(e => e.ConcurrencyStamp, "winner-stamp"));
    }

    private async Task<string> InsertExecutionAsync(WorkflowExecutionStatus status)
    {
        var definition = await InsertDefinitionAsync("""[ { "stepId": "a", "configuration": { "nodeType": "agent" } } ]""");
        var execution = new WorkflowExecution
        {
            ExecutionId = Guid.NewGuid().ToString("N"),
            WorkflowDefinitionId = definition.Id,
            InitialInput = "start",
            Status = status,
            StartedAt = DateTime.UtcNow
        };
        DbContext.Set<WorkflowExecution>().Add(execution);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return execution.ExecutionId;
    }

    private async Task AssertCancelRequestedAsync(string executionId)
    {
        DbContext.ChangeTracker.Clear();
        var row = await LoadExecutionAsync(executionId);
        row.ShouldNotBeNull();
        row!.PendingSignalCount.ShouldBe(1);
        var signals = JsonSerializer.Deserialize<List<WorkflowExecutionSignal>>(row.PendingSignalsJson, TnziJsonDefaults.Options)!;
        signals.ShouldHaveSingleItem().Type.ShouldBe(WorkflowExecutionSignalTypes.Cancel);
        row.CurrentWaitReason.ShouldBe("cancel_requested");
    }

    private sealed class RacingCheckpointStore(IWorkflowCheckpointStore inner, Func<Func<string, Task>?> afterRead) : IWorkflowCheckpointStore
    {
        public async Task<WorkflowCheckpoint?> GetCheckpointAsync(string executionId, CancellationToken ct = default)
        {
            var checkpoint = await inner.GetCheckpointAsync(executionId, ct);
            var hook = afterRead();
            if (hook != null)
            {
                await hook(executionId);
            }

            return checkpoint;
        }

        public Task SaveCheckpointAsync(WorkflowCheckpoint checkpoint, CancellationToken ct = default)
            => inner.SaveCheckpointAsync(checkpoint, ct);

        public Task DeleteCheckpointAsync(string executionId, CancellationToken ct = default)
            => inner.DeleteCheckpointAsync(executionId, ct);
    }

    private sealed class ExecutionCounter
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

    private sealed class CountingAgentNode(ExecutionCounter counter) : IWorkflowNode
    {
        public string NodeType => WorkflowNodeTypes.Agent;

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            counter.Record(context.Step.StepId!);
            return Task.FromResult(new WorkflowNodeResult { Output = $"{context.Step.StepId}-out", IsSuccess = true });
        }
    }

    private sealed class CountingHumanInputNode(ExecutionCounter counter) : IWorkflowNode
    {
        public string NodeType => "counting_human_input";

        public Task<WorkflowInterrupt?> CheckInterruptAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
            => Task.FromResult<WorkflowInterrupt?>(new WorkflowInterrupt
            {
                StepId = context.Step.StepId!,
                Reason = "Need an answer",
                Type = InterruptType.HumanInput
            });

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            counter.Record(context.Step.StepId!);
            return Task.FromResult(new WorkflowNodeResult { Output = "answered", IsSuccess = true });
        }
    }
}
