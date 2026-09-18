using Microsoft.EntityFrameworkCore.Diagnostics;

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
/// B13 - verifies DatabaseWorkflowCheckpointStore uses optimistic concurrency
/// (IConcurrencyStamp): a stale-token Update is reloaded + union-merged + retried so two
/// live writers do not drop each other's CompletedSteps / StepOutputs, while a writer
/// that read the current row and saved without conflict is authoritative (its smaller
/// set is a deliberate removal, not a lost update).
/// </summary>
/// <remarks>
/// ★ The union-merge used to run on the normal path as well, and that silently undid every
/// deliberate removal (ResumeWithInputAsync's "re-enter the interrupted node", the
/// engine's loop-body reset). The earlier "two concurrent writers" test never reached the
/// conflict path either: on SQLite the two saves serialize, so the second writer read the
/// first writer's committed row and the union on the normal path is what kept it green.
/// The stale-token test below makes the other writer commit <b>between the store's read and its
/// SaveChanges</b> (a SavingChanges interceptor), so the optimistic-concurrency check really fires
/// on a genuinely stale token. It used to force the conflict by tracking a stale instance in the
/// second scope's DbContext instead; that was a pseudo-conflict (the store had read the latest row),
/// and the repository's duplicate merge now carries the newer stamp so it no longer conflicts.
/// </remarks>
public class DatabaseWorkflowCheckpointStoreConcurrencyTests : IntegratedTestBase<WorkflowCheckpointStoreDbContext>
{
    private readonly ForeignWriterInterceptor _foreignWriter = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<WorkflowExecution, Guid>,
            EFCoreRepository<WorkflowCheckpointStoreDbContext, WorkflowExecution, Guid>>();
    }

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
    {
        options.AddInterceptors(_foreignWriter);
    }

    /// <summary>
    /// 在下一次 SaveChanges 真正生成 SQL 之前跑一次 <see cref="BeforeNextSave"/>（一次性）：
    /// 用来把「别的写者在我读之后、写之前提交了」这个竞态做成确定性的。
    /// </summary>
    private sealed class ForeignWriterInterceptor : SaveChangesInterceptor
    {
        public Func<Task>? BeforeNextSave { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var hook = BeforeNextSave;
            if (hook != null)
            {
                BeforeNextSave = null;
                await hook();
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
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
    public async Task SaveCheckpoint_WithoutConflict_HonoursExplicitStepRemoval()
    {
        const string executionId = "wf-concurrency-001";

        await CreateStore(ServiceProvider).SaveCheckpointAsync(
            Checkpoint(executionId, ["a", "x"], ("a", "out-a"), ("x", "[Awaiting HumanInput: ...]")));

        // A writer that read the committed row and removes a step (the interrupted node
        // that must re-enter the ready queue) is authoritative: nothing wrote in between,
        // so the smaller set must be persisted as-is, not unioned back to the old one.
        using var scope = ServiceProvider.CreateScope();
        await CreateStore(scope.ServiceProvider).SaveCheckpointAsync(
            Checkpoint(executionId, ["a"], ("a", "out-a")));

        var final = await CreateStore(ServiceProvider).GetCheckpointAsync(executionId);
        final.ShouldNotBeNull();
        final!.CompletedStepIds.ShouldBe(["a"], ignoreOrder: true);
        final.StepOutputs.Keys.ShouldBe(["a"], ignoreOrder: true);
    }

    [Fact]
    public async Task SaveCheckpoint_StaleToken_ReloadsAndRetries_PreservingBothWriters()
    {
        const string executionId = "wf-concurrency-002";

        await CreateStore(ServiceProvider).SaveCheckpointAsync(
            Checkpoint(executionId, ["s0"], ("s0", "o0")));

        var staleStamp = (await DbContext.Set<WorkflowExecution>().AsNoTracking().FirstAsync(e => e.ExecutionId == executionId))
            .ConcurrencyStamp;

        // Deterministically force a REAL stale-token conflict: writer-1 commits between store2's
        // read (which sees the original stamp) and store2's SaveChanges (whose WHERE still carries
        // that stamp). store2 must reload + union-merge + retry, not throw and not drop writer-1's step.
        using var scope2 = ServiceProvider.CreateScope();
        var store2 = CreateStore(scope2.ServiceProvider);
        _foreignWriter.BeforeNextSave = async () =>
        {
            using var scope1 = ServiceProvider.CreateScope();
            await CreateStore(scope1.ServiceProvider).SaveCheckpointAsync(
                Checkpoint(executionId, ["s0", "from-writer-1"], ("from-writer-1", "o1")));
        };

        await Should.NotThrowAsync(async () => await store2.SaveCheckpointAsync(
            Checkpoint(executionId, ["s0", "from-writer-2"], ("from-writer-2", "o2"))));
        _foreignWriter.BeforeNextSave.ShouldBeNull("the foreign writer must have run inside store2's save");

        var final = await CreateStore(ServiceProvider).GetCheckpointAsync(executionId);
        final.ShouldNotBeNull();
        final!.CompletedStepIds.ShouldContain("from-writer-1");
        final.CompletedStepIds.ShouldContain("from-writer-2");
        final.StepOutputs.ShouldContainKey("from-writer-1");
        final.StepOutputs.ShouldContainKey("from-writer-2");

        var persisted = await DbContext.Set<WorkflowExecution>().AsNoTracking().FirstAsync(e => e.ExecutionId == executionId);
        persisted.ConcurrencyStamp.ShouldNotBe(staleStamp, "the conflict path must have re-saved with a fresh stamp");
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
