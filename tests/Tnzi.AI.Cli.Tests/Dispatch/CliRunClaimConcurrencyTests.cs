namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 队列认领用的最小 DbContext。
/// </summary>
public class CliQueueDbContext : TnziDbContext<CliQueueDbContext>
{
    public CliQueueDbContext(DbContextOptions<CliQueueDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CliRunConfiguration());
        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// 并发认领：<b>恰好一个</b>赢。
/// </summary>
/// <remarks>
/// 这条性质必须在只有一个副本的时候就测掉。它出问题的表现是同一条运行被两个进程同时执行 ——
/// 用户看到两份重复输出、账单翻倍 —— 而这类故障只在生产的多副本下出现，本地永远撞不到。
/// <para>
/// 认领用的是<b>条件更新</b>（<c>WHERE Status = Queued</c>）而不是分布式锁，
/// 也不用 <c>FOR UPDATE SKIP LOCKED</c>（那是 PostgreSQL 方言，违反数据库无关铁律）。
/// </para>
/// </remarks>
public class CliRunClaimConcurrencyTests : IntegratedTestBase<CliQueueDbContext>
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliQueueDbContext, CliRun, Guid>>();
    }

    [Fact]
    public async Task ConditionalClaim_AcrossManyHosts_SucceedsExactlyOnce()
    {
        var run = new CliRun
        {
            AgentId = Guid.NewGuid(),
            CliRuntimeId = Guid.NewGuid(),
            Status = CliRunStatus.Queued,
            Prompt = "do the thing"
        };

        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var affectedCounts = new List<int>();

        // 每次尝试一个独立作用域 = 一个独立 DbContext，模拟不同宿主各自认领。
        // 刻意<b>不</b>并行发出：本测试要钉死的性质是「条件谓词让第二次认领变成 no-op」，
        // 而数据库层面的写串行化是数据库自己的职责。SQLite 内存库共用一条连接，
        // 真并行只会撞上连接不可并发的限制，测不出任何我们拥有的性质。
        foreach (var host in Enumerable.Range(0, 16))
        {
            using var scope = ServiceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>();

            affectedCounts.Add(await repository.AsQueryable()
                .Where(r => r.Id == run.Id && r.Status == CliRunStatus.Queued)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, CliRunStatus.Dispatched)
                    .SetProperty(r => r.ClaimedByHostId, $"host-{host}")
                    .SetProperty(r => r.DispatchedAt, now)
                    .SetProperty(r => r.LeaseExpiresAt, now.AddMinutes(2))));
        }

        affectedCounts.Count(affected => affected > 0).ShouldBe(1);
        affectedCounts[0].ShouldBe(1);

        var claimed = await DbContext.Set<CliRun>().AsNoTracking().SingleAsync(r => r.Id == run.Id);
        claimed.Status.ShouldBe(CliRunStatus.Dispatched);
        claimed.ClaimedByHostId.ShouldBe("host-0");
    }

    [Fact]
    public async Task ExpiredLeaseReclaim_ReturnsTheRunToTheQueue()
    {
        // 宿主崩溃后没人续租。不回收的话这一行会永远停在 Dispatched，任务静默消失。
        var run = new CliRun
        {
            AgentId = Guid.NewGuid(),
            CliRuntimeId = Guid.NewGuid(),
            Status = CliRunStatus.Dispatched,
            ClaimedByHostId = "dead-host",
            DispatchedAt = DateTime.UtcNow.AddMinutes(-10),
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(-5),
            Prompt = "orphaned"
        };

        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();

        var repository = ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>();
        var now = DateTime.UtcNow;

        var reclaimed = await repository.AsQueryable()
            .Where(r => r.LeaseExpiresAt != null
                        && r.LeaseExpiresAt < now
                        && (r.Status == CliRunStatus.Dispatched || r.Status == CliRunStatus.Running))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, CliRunStatus.Queued)
                .SetProperty(r => r.LeaseExpiresAt, (DateTime?)null)
                .SetProperty(r => r.ClaimedByHostId, (string?)null)
                .SetProperty(r => r.DispatchedAt, (DateTime?)null));

        reclaimed.ShouldBe(1);

        var restored = await DbContext.Set<CliRun>().AsNoTracking().SingleAsync(r => r.Id == run.Id);
        restored.Status.ShouldBe(CliRunStatus.Queued);
        restored.ClaimedByHostId.ShouldBeNull();
    }

    [Fact]
    public async Task LeaseReclaim_LeavesRunsWithALiveLeaseAlone()
    {
        // 回收一个还在跑的运行 = 让别的副本把它抢走并重跑一遍。
        var run = new CliRun
        {
            AgentId = Guid.NewGuid(),
            CliRuntimeId = Guid.NewGuid(),
            Status = CliRunStatus.Running,
            ClaimedByHostId = "live-host",
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(2),
            Prompt = "still running"
        };

        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();

        var repository = ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>();
        var now = DateTime.UtcNow;

        var reclaimed = await repository.AsQueryable()
            .Where(r => r.LeaseExpiresAt != null
                        && r.LeaseExpiresAt < now
                        && (r.Status == CliRunStatus.Dispatched || r.Status == CliRunStatus.Running))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, CliRunStatus.Queued));

        reclaimed.ShouldBe(0);
    }
}
