namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 一条忙线程上的积压不能挡住队列里其它所有运行。
/// </summary>
/// <remarks>
/// 此前候选查询先取队首 10 条，再在内存里逐条跳过「线程已有活跃运行」的行：同一线程积压 10 条
/// （长任务进行中连续追问，或者故意排满），所有用户、所有租户排在它们后面的运行就再也不会被看到，
/// 空着的并发槽也不会被用上，而且没有任何错误。线程互斥现在是候选查询本身的谓词。
/// </remarks>
public class CliRunClaimFairnessTests : IntegratedTestBase<CliQueueDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-1111111111f1");
    private static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-2222222222f2");

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliQueueDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliQueueDbContext, CliRuntime, Guid>>();
    }

    [Fact]
    public async Task ABacklogOnABusyThread_DoesNotHideTheRestOfTheQueue()
    {
        var busyThread = Guid.NewGuid();
        Seed(CliRunStatus.Running, busyThread, priority: 0);

        // 排在队首：优先级更高、也更早
        for (var i = 0; i < 11; i++)
        {
            Seed(CliRunStatus.Queued, busyThread, priority: 100);
        }

        var otherThread = Guid.NewGuid();
        var waiting = Seed(CliRunStatus.Queued, otherThread, priority: 0);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var claimed = await CreateProcessor().TryClaimAsync(CancellationToken.None);

        claimed.ShouldBe(waiting.Id);
    }

    [Fact]
    public async Task TheBacklogIsClaimedOnceItsThreadIsIdle()
    {
        // 对照：线程空闲时它自己的积压照常按优先级认领，谓词没有把它们永久排除
        var thread = Guid.NewGuid();
        var first = Seed(CliRunStatus.Queued, thread, priority: 5);
        Seed(CliRunStatus.Queued, thread, priority: 1);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var processor = CreateProcessor();

        (await processor.TryClaimAsync(CancellationToken.None)).ShouldBe(first.Id);
        (await processor.TryClaimAsync(CancellationToken.None)).ShouldBeNull(
            "the second turn of the same thread waits until the first one is done");
    }

    /// <summary>
    /// 运行时自己的并发上限：此前这一列只写不读，管理员改成 1 想让它串行执行，
    /// 保存成功、页面显示 1，宿主照样按全局上限同时认领它排着的运行。
    /// </summary>
    [Fact]
    public async Task ARuntimeAtItsOwnConcurrencyLimit_HasItsQueuedRunsSkipped_OtherRuntimesAreClaimed()
    {
        var otherRuntime = Guid.NewGuid();
        SeedRuntime(RuntimeId, maxConcurrentRuns: 1);
        SeedRuntime(otherRuntime, maxConcurrentRuns: 4, providerKey: "qwen");
        Seed(CliRunStatus.Running, Guid.NewGuid(), priority: 0);

        // 排在队首：同一个已满的运行时
        Seed(CliRunStatus.Queued, Guid.NewGuid(), priority: 100);
        var elsewhere = Seed(CliRunStatus.Queued, Guid.NewGuid(), priority: 0, runtimeId: otherRuntime);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var claimed = await CreateProcessor().TryClaimAsync(CancellationToken.None);

        claimed.ShouldBe(elsewhere.Id);
    }

    [Fact]
    public async Task ARuntimeBelowItsLimit_HasItsQueuedRunsClaimed_UntilTheLimitIsReached()
    {
        // 对照：谓词按运行时的上限比，不是「运行时上有运行就不认领」
        SeedRuntime(RuntimeId, maxConcurrentRuns: 2);
        Seed(CliRunStatus.Running, Guid.NewGuid(), priority: 0);
        var queued = Seed(CliRunStatus.Queued, Guid.NewGuid(), priority: 0);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var processor = CreateProcessor();

        (await processor.TryClaimAsync(CancellationToken.None)).ShouldBe(queued.Id);
        Seed(CliRunStatus.Queued, Guid.NewGuid(), priority: 0);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        (await processor.TryClaimAsync(CancellationToken.None)).ShouldBeNull("two runs are now in flight on a runtime limited to two");
    }

    [Fact]
    public async Task ARunWhoseRuntimeRowIsGone_IsStillClaimed()
    {
        // 运行时已删除：不拦，交给执行器以「运行时不存在」判失败，而不是让它永远停在 Queued
        var queued = Seed(CliRunStatus.Queued, Guid.NewGuid(), priority: 0, runtimeId: Guid.NewGuid());
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        (await CreateProcessor().TryClaimAsync(CancellationToken.None)).ShouldBe(queued.Id);
    }

    private void SeedRuntime(Guid id, int maxConcurrentRuns, string providerKey = "claude")
        => DbContext.Set<CliRuntime>().Add(new CliRuntime
        {
            Id = id,
            HostId = "TEST-HOST",
            ProviderKey = providerKey,
            Name = $"{providerKey} @ TEST-HOST",
            ExecutablePath = "/usr/bin/" + providerKey,
            Status = CliRuntimeStatus.Online,
            MaxConcurrentRuns = maxConcurrentRuns
        });

    private CliRun Seed(CliRunStatus status, Guid threadId, int priority, Guid? runtimeId = null)
    {
        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = runtimeId ?? RuntimeId,
            ThreadId = threadId,
            Status = status,
            Priority = priority,
            Prompt = "turn"
        };
        DbContext.Set<CliRun>().Add(run);
        return run;
    }

    private CliRunQueueProcessor CreateProcessor()
    {
        var options = new Mock<IOptionsMonitor<CliAgentOptions>>();
        options.Setup(m => m.CurrentValue).Returns(new CliAgentOptions { Enabled = true });

        return new CliRunQueueProcessor(
            ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            options.Object,
            NullLogger<CliRunQueueProcessor>.Instance);
    }
}
