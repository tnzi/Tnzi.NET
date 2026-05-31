namespace Tnzi.AI.Tests;

/// <summary>
/// Test DbContext exposing the WorkflowExecution entity with its ConcurrencyStamp
/// optimistic-concurrency token (see WorkflowExecutionConfiguration) so the SQLite
/// provider actually enforces stale-token conflicts on Update.
/// </summary>
public class WorkflowCheckpointStoreDbContext : TnziDbContext<WorkflowCheckpointStoreDbContext>
{
    public WorkflowCheckpointStoreDbContext(
        DbContextOptions<WorkflowCheckpointStoreDbContext> options,
        ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new WorkflowExecutionConfiguration());
        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// B13 — verifies DatabaseWorkflowCheckpointStore uses optimistic concurrency
/// (IConcurrencyStamp) plus a step union-merge so two concurrent writers do not
/// silently drop each other's CompletedSteps / StepOutputs (no last-write-wins),
/// and a stale-token Update is reloaded + re-merged + retried instead of overwriting.
/// </summary>
public class DatabaseWorkflowCheckpointStoreConcurrencyTests : IntegratedTestBase<WorkflowCheckpointStoreDbContext>
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<WorkflowExecution, Guid>,
            EFCoreRepository<WorkflowCheckpointStoreDbContext, WorkflowExecution, Guid>>();
    }

    private DatabaseWorkflowCheckpointStore CreateStore(IServiceProvider scopedProvider)
    {
        var repo = scopedProvider.GetRequiredService<IRepository<WorkflowExecution, Guid>>();
        return new DatabaseWorkflowCheckpointStore(repo, NullLogger<DatabaseWorkflowCheckpointStore>.Instance);
    }

    private static WorkflowCheckpoint Checkpoint(string executionId, IEnumerable<string> steps, params (string id, string text)[] outputs)
    {
        return new WorkflowCheckpoint
        {
            ExecutionId = executionId,
            InitialInput = "input",
            Status = WorkflowExecutionStatus.Running,
            CompletedStepIds = new HashSet<string>(steps, StringComparer.OrdinalIgnoreCase),
            StepOutputs = outputs.ToDictionary(
                o => o.id,
                o => (WorkflowStepOutput)o.text,
                StringComparer.OrdinalIgnoreCase)
        };
    }

    [Fact]
    public async Task SaveCheckpoint_TwoConcurrentWriters_UnionsCompletedSteps_NoLostUpdate()
    {
        const string executionId = "wf-concurrency-001";

        // Seed the row so both writers take the Update (not Insert) path.
        await CreateStore(ServiceProvider).SaveCheckpointAsync(
            Checkpoint(executionId, ["seed"], ("seed", "seed-out")));

        // Two independent scopes/DbContexts read the same row (same ConcurrencyStamp),
        // then each adds a distinct completed step. Without merge + token, one would clobber
        // the other (last-write-wins) and drop a completed step.
        using var scope1 = ServiceProvider.CreateScope();
        using var scope2 = ServiceProvider.CreateScope();
        var store1 = CreateStore(scope1.ServiceProvider);
        var store2 = CreateStore(scope2.ServiceProvider);

        var w1 = Checkpoint(executionId, ["seed", "step-a"], ("step-a", "out-a"));
        var w2 = Checkpoint(executionId, ["seed", "step-b"], ("step-b", "out-b"));

        var t1 = store1.SaveCheckpointAsync(w1);
        var t2 = store2.SaveCheckpointAsync(w2);

        await Should.NotThrowAsync(async () => await Task.WhenAll(t1, t2));

        // Read back the persisted union — all three steps + all three outputs survive.
        var final = await CreateStore(ServiceProvider).GetCheckpointAsync(executionId);
        final.ShouldNotBeNull();
        final!.CompletedStepIds.ShouldContain("seed");
        final.CompletedStepIds.ShouldContain("step-a");
        final.CompletedStepIds.ShouldContain("step-b");
        final.StepOutputs.ShouldContainKey("step-a");
        final.StepOutputs.ShouldContainKey("step-b");
        final.StepOutputs["step-a"].Text.ShouldBe("out-a");
        final.StepOutputs["step-b"].Text.ShouldBe("out-b");
    }

    [Fact]
    public async Task SaveCheckpoint_StaleToken_ReloadsAndRetries_PreservingBothWriters()
    {
        const string executionId = "wf-concurrency-002";

        await CreateStore(ServiceProvider).SaveCheckpointAsync(
            Checkpoint(executionId, ["s0"], ("s0", "o0")));

        // Deterministically force a stale-token conflict: both stores read the current
        // row (same stamp) BEFORE either writes, by having store1 commit first, then
        // store2 (which still holds the original stamp) attempt to write.
        using var scope1 = ServiceProvider.CreateScope();
        using var scope2 = ServiceProvider.CreateScope();
        var repo2 = scope2.ServiceProvider.GetRequiredService<IRepository<WorkflowExecution, Guid>>();

        // store2 reads the entity first (captures the original stamp in its tracker).
        var staleEntity = await repo2.FirstOrDefaultAsync(e => e.ExecutionId == executionId);
        staleEntity.ShouldNotBeNull();

        // store1 commits a change → bumps the DB stamp.
        await CreateStore(scope1.ServiceProvider).SaveCheckpointAsync(
            Checkpoint(executionId, ["s0", "from-writer-1"], ("from-writer-1", "o1")));

        // store2 now writes with its (now stale) view → store must reload + union-merge + retry,
        // not throw and not drop writer-1's step.
        var store2 = CreateStore(scope2.ServiceProvider);
        await Should.NotThrowAsync(async () => await store2.SaveCheckpointAsync(
            Checkpoint(executionId, ["s0", "from-writer-2"], ("from-writer-2", "o2"))));

        var final = await CreateStore(ServiceProvider).GetCheckpointAsync(executionId);
        final.ShouldNotBeNull();
        final!.CompletedStepIds.ShouldContain("from-writer-1");
        final.CompletedStepIds.ShouldContain("from-writer-2");
        final.StepOutputs.ShouldContainKey("from-writer-1");
        final.StepOutputs.ShouldContainKey("from-writer-2");
    }

    [Fact]
    public async Task SaveCheckpoint_AssignsConcurrencyStamp_AndBumpsOnUpdate()
    {
        const string executionId = "wf-concurrency-003";

        await CreateStore(ServiceProvider).SaveCheckpointAsync(
            Checkpoint(executionId, ["a"], ("a", "oa")));

        var afterInsert = await DbContext.Set<WorkflowExecution>()
            .AsNoTracking()
            .FirstAsync(e => e.ExecutionId == executionId);
        afterInsert.ConcurrencyStamp.ShouldNotBeNullOrWhiteSpace();
        var stampAfterInsert = afterInsert.ConcurrencyStamp;

        using var scope = ServiceProvider.CreateScope();
        await CreateStore(scope.ServiceProvider).SaveCheckpointAsync(
            Checkpoint(executionId, ["a", "b"], ("b", "ob")));

        var afterUpdate = await DbContext.Set<WorkflowExecution>()
            .AsNoTracking()
            .FirstAsync(e => e.ExecutionId == executionId);
        afterUpdate.ConcurrencyStamp.ShouldNotBe(stampAfterInsert);
    }
}
