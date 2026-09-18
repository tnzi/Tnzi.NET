using Tnzi.Audit.Tests.TestSupport;
using Tnzi.Locking;
using Tnzi.MultiTenancy;

namespace Tnzi.Audit.Tests.Integration;

/// <summary>
/// 销毁证明的链尾争用：撞上唯一索引后重试必须<b>真的</b>能成功，而手动 Run 与定时轮要过同一把锁。
/// </summary>
/// <remarks>
/// <para>
/// ★ 与 <c>RecordAccessAuditor</c> 在 2026-09-04 修掉的形态逐字相同：<c>InsertAsync</c> 撞唯一索引后失败的实体
/// 仍以 Added 留在变更跟踪器里，第二、三次重试的 SaveChanges 会把它一起重放、再撞同一条索引 ⇒ 三次全败 ⇒
/// 抛异常 —— 而 <c>DestroyAsync</c>（硬删，不可撤销）早已执行完。文档里写成「窄窗口」的
/// 「数据已销毁而证明没有落库」于是变成一撞必现。那次修复只改了 <c>RecordAccessAuditor</c>，没有顺手改这里。
/// </para>
/// <para>
/// ★ 制造这个撞车的入口是现成的：<c>POST admin/data-destruction/run</c> 直接调 <c>RunAsync</c>，
/// 分布式锁只存在于后台服务里。锁现在下移到 <c>RunAsync</c>，抢不到时手动端点答 409。
/// </para>
/// <para>
/// SQLite 强制唯一索引，冲突不会被掩盖（夹具用显式 <c>enabled: true</c> 的配置建表）。
/// </para>
/// </remarks>
public class DataDestructionCertificateConcurrencyTests : IntegrationTestBase
{
    private const string PolicyName = "test-retention";

    private sealed class StubPolicyProvider(params RetentionPolicy[] policies) : IRetentionPolicyProvider
    {
        public IEnumerable<RetentionPolicy> GetPolicies() => policies;
    }

    private sealed class StubEntityManager : IEntityManager
    {
        public void Initialize() { }
        public IEntityRegister[] GetEntityRegisters(Type dbContextType) => [];
        public Type GetDbContextTypeForEntity(Type entityType) => typeof(AuditTestDbContext);
        public Type[] GetAllEntityTypes() => [typeof(RetentionTestRecord)];
        public Type[] GetAllDbContextTypes() => [typeof(AuditTestDbContext)];
    }

    /// <summary>
    /// 第一次插入前，「另一个写入方」抢先写入同一序号：模拟手动 Run 撞上定时轮（或两个实例）都读到同一条链尾。
    /// </summary>
    private sealed class RacingCertificateRepository : EFCoreRepository<AuditTestDbContext, AuditDataDestruction, Guid>
    {
        private bool _raced;

        public RacingCertificateRepository(AuditTestDbContext dbContext, IServiceProvider serviceProvider)
            : base(dbContext, serviceProvider: serviceProvider)
        {
        }

        public int InsertAttempts { get; private set; }

        public override async Task InsertAsync(AuditDataDestruction entity, CancellationToken cancellationToken = default)
        {
            InsertAttempts++;
            if (!_raced)
            {
                _raced = true;
                DbContext.Set<AuditDataDestruction>().Add(new AuditDataDestruction
                {
                    Sequence = entity.Sequence,
                    PolicyName = "rival-policy",
                    EntityType = entity.EntityType,
                    Cutoff = entity.Cutoff,
                    DestroyedCount = 1,
                    HeldCount = 0,
                    IdentifierDigest = "rival-digest",
                    Mode = entity.Mode,
                    PreviousHash = entity.PreviousHash,
                    Hash = "rival-hash",
                    CreationTime = DateTime.UtcNow
                });
                await SaveChangesAsync(cancellationToken);
            }

            await base.InsertAsync(entity, cancellationToken);
        }
    }

    /// <summary>固定答案的分布式锁：<c>acquired</c> 决定每次 Acquire 的结果；记录释放次数。</summary>
    private sealed class StubDistributedLock(bool acquired) : IDistributedLock
    {
        public int AcquireCalls { get; private set; }
        public int Released { get; private set; }

        public Task<IDistributedLockHandle?> AcquireAsync(string key, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            AcquireCalls++;
            return Task.FromResult<IDistributedLockHandle?>(acquired ? new Handle(key, this) : null);
        }

        public Task<(bool Success, IDistributedLockHandle? Handle)> TryAcquireAsync(string key, TimeSpan timeout, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        private sealed class Handle(string key, StubDistributedLock owner) : IDistributedLockHandle
        {
            public string Key => key;
            public bool IsAcquired => true;
            public Task<bool> ExtendAsync(TimeSpan extension) => Task.FromResult(true);
            public ValueTask DisposeAsync()
            {
                owner.Released++;
                return ValueTask.CompletedTask;
            }
        }
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        // 工作单元提交时经 IEntityManager 找 DbContext；测试 DbContext 不走 AddTnziDbContext，要手工指路。
        services.AddSingleton<IEntityManager>(new StubEntityManager());
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
    }

    private static RetentionPolicy<RetentionTestRecord> Policy(string name = PolicyName, string? category = null)
        => new()
        {
            Name = name,
            RetentionPeriod = TimeSpan.FromDays(30),
            Timestamp = r => r.CreationTime,
            Scope = category == null ? null : r => r.Category == category,
        };

    private DataDestructionService CreateService(
        IRepository<AuditDataDestruction, Guid>? repository = null,
        IDistributedLock? distributedLock = null,
        params RetentionPolicy[] policies)
    {
        repository ??= new EFCoreRepository<AuditTestDbContext, AuditDataDestruction, Guid>(DbContext, serviceProvider: ServiceProvider);

        return new DataDestructionService(
            ServiceProvider,
            repository,
            new StaticOptionsMonitor<DataDestructionOptions>(new DataDestructionOptions { Enabled = true }),
            [new StubPolicyProvider(policies.Length == 0 ? [Policy()] : policies)],
            [],
            new HardDeleteDataDestroyer(ServiceProvider, new StubEntityManager()),
            ServiceProvider.GetRequiredService<ICurrentTenant>(),
            keyStateProvider: null,
            multiTenancyOptions: null,
            distributedLock: distributedLock);
    }

    private async Task SeedAsync(int ageInDays = 100, string category = "general")
    {
        DbContext.Set<RetentionTestRecord>().Add(new RetentionTestRecord
        {
            Id = Guid.NewGuid(),
            Category = category,
            CreationTime = DateTime.UtcNow.AddDays(-ageInDays),
            ClosedAt = DateTime.UtcNow.AddDays(-ageInDays),
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private Task<List<AuditDataDestruction>> ChainAsync()
    {
        DbContext.ChangeTracker.Clear();
        return DbContext.Set<AuditDataDestruction>().AsNoTracking().OrderBy(e => e.Sequence).ToListAsync();
    }

    private IUnitOfWorkManager Manager => ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

    // ── 链尾争用 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteCertificate_ChainTailConflict_RetriesWithNextSequence()
    {
        await SeedAsync();
        var repository = new RacingCertificateRepository(DbContext, ServiceProvider);
        var service = CreateService(repository);

        var result = await service.RunAsync();

        result.Succeeded.ShouldBeTrue(result.Message);
        var policy = result.Data!.Policies.ShouldHaveSingleItem();
        policy.Error.ShouldBeNull("the retry must succeed after one conflict, the data is already destroyed");
        policy.CertificateId.ShouldNotBeNull();
        repository.InsertAttempts.ShouldBe(2);

        var chain = await ChainAsync();
        chain.Select(e => e.PolicyName).ShouldBe(["rival-policy", PolicyName]);
        chain.Select(e => e.Sequence).ShouldBe([1L, 2L]);
        chain[1].PreviousHash.ShouldBe("rival-hash");
    }

    [Fact]
    public async Task WriteCertificate_AfterAConflict_TheFailedEntityIsNotReplayed()
    {
        await SeedAsync();
        var service = CreateService(new RacingCertificateRepository(DbContext, ServiceProvider));

        (await service.RunAsync()).Data!.Policies[0].Error.ShouldBeNull();

        // 失败的那个实体必须已被丢弃：作用域内下一次无关的 SaveChanges 不能再撞一次索引。
        await Should.NotThrowAsync(() => DbContext.SaveChangesAsync());
        (await ChainAsync()).Count.ShouldBe(2);
    }

    /// <summary>
    /// 宿主开了 <c>EnableGlobalUnitOfWork</c>：同一次 Run 里两条策略各自读库里的链尾，
    /// 不显式 flush 就会算出同一个 Sequence 并在提交时整批炸掉。
    /// </summary>
    [Fact]
    public async Task WriteCertificate_UnderAmbientUnitOfWork_TwoPoliciesGetConsecutiveSequences()
    {
        await SeedAsync(category: "a");
        await SeedAsync(category: "b");
        var service = CreateService(policies: [Policy("policy-a", "a"), Policy("policy-b", "b")]);

        Manager.EnableTransaction();
        var result = await service.RunAsync();
        await Manager.CommitTransactionAsync();

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Policies.ShouldAllBe(p => p.Error == null);

        var chain = await ChainAsync();
        chain.Select(e => e.Sequence).ShouldBe([1L, 2L]);
        chain[1].PreviousHash.ShouldBe(chain[0].Hash);
        (await service.VerifyChainAsync()).Succeeded.ShouldBeTrue();
    }

    // ── 锁下移到 RunAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ManualRun_WhileLockHeld_Returns409AndDestroysNothing()
    {
        await SeedAsync();
        var service = CreateService(distributedLock: new StubDistributedLock(acquired: false));

        var result = await service.RunAsync();

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
        // 后台服务只凭这个错误码把一轮判成「跳过」；同名策略与锁中途丢失也答 409，不能共用它。
        result.ErrorCode.ShouldBe(ErrorCodes.AuditDestructionRunInProgress);
        (await DbContext.Set<RetentionTestRecord>().IgnoreQueryFilters().CountAsync()).ShouldBe(1);
        (await ChainAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Run_AcquiresAndReleasesTheLock()
    {
        await SeedAsync();
        var distributedLock = new StubDistributedLock(acquired: true);
        var service = CreateService(distributedLock: distributedLock);

        var result = await service.RunAsync();

        result.Succeeded.ShouldBeTrue(result.Message);
        distributedLock.AcquireCalls.ShouldBe(1);
        distributedLock.Released.ShouldBe(1);
        (await ChainAsync()).Count.ShouldBe(1);
    }

    /// <summary>没装锁实现时照常跑（单实例部署），与此前逐字相同。</summary>
    [Fact]
    public async Task Run_WithoutADistributedLock_StillRuns()
    {
        await SeedAsync();
        var service = CreateService(distributedLock: null);

        (await service.RunAsync()).Succeeded.ShouldBeTrue();
        (await ChainAsync()).Count.ShouldBe(1);
    }
}
