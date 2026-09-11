using Tnzi.Audit.Tests.TestSupport;
using Tnzi.Data;

namespace Tnzi.Audit.Tests.Integration;

/// <summary>
/// 记录级读取审计的哈希链在<b>环境事务</b>里与<b>并发冲突</b>下仍然成链。
/// </summary>
/// <remarks>
/// <para>
/// 仓储在事务启用时把 SaveChanges 推迟到提交。此前 <c>RecordAsync</c> 只调 <c>InsertAsync</c>，
/// 于是业务代码在 <c>ExecuteInUnitOfWorkAsync</c> 里登记两次读取，两次读到的链尾都是库里那条，
/// 算出同一个 <c>Sequence</c>，唯一索引在<b>提交时</b>才拒绝 —— 整个业务操作 500，
/// 而设计中的「重读链尾重试」循环早已退出。
/// </para>
/// <para>
/// 还有一层更早就坏了：冲突后失败的实体从没被 <c>Discard</c>，仍以 Added 状态留在变更跟踪器里，
/// 下一次重试的 SaveChanges 会把它一起重放、再撞一次同一条索引 —— 所以即使没有事务，
/// 重试循环也从来没成功过。
/// </para>
/// </remarks>
public class RecordAccessAuditorTransactionTests : IntegrationTestBase
{
    private sealed class MapEntityManager : IEntityManager
    {
        public void Initialize()
        {
        }

        public IEntityRegister[] GetEntityRegisters(Type dbContextType) => [];

        public Type GetDbContextTypeForEntity(Type entityType) => typeof(AuditTestDbContext);

        public Type[] GetAllEntityTypes() => [typeof(AuditRecordAccess)];

        public Type[] GetAllDbContextTypes() => [typeof(AuditTestDbContext)];
    }

    /// <summary>
    /// 第一次插入前，「另一个请求」抢先写入同一序号：模拟两个请求同时读到同一条链尾。
    /// </summary>
    private sealed class RacingRepository : EFCoreRepository<AuditTestDbContext, AuditRecordAccess, Guid>
    {
        private bool _raced;

        public RacingRepository(AuditTestDbContext dbContext, IServiceProvider serviceProvider)
            : base(dbContext, serviceProvider: serviceProvider)
        {
        }

        public int InsertAttempts { get; private set; }

        public override async Task InsertAsync(AuditRecordAccess entity, CancellationToken cancellationToken = default)
        {
            InsertAttempts++;
            if (!_raced)
            {
                _raced = true;
                DbContext.Set<AuditRecordAccess>().Add(new AuditRecordAccess
                {
                    Sequence = entity.Sequence,
                    UserId = entity.UserId,
                    UserName = entity.UserName,
                    ResourceType = "Tip",
                    ResourceId = "rival",
                    PreviousHash = entity.PreviousHash,
                    Hash = "rival-hash",
                    CreationTime = DateTime.UtcNow
                });
                // 经仓储的事务感知 flush 落库：事务启用时它开启物理事务而不是绕过它。
                await SaveChangesAsync(cancellationToken);
            }

            await base.InsertAsync(entity, cancellationToken);
        }
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddSingleton<IEntityManager>(new MapEntityManager());
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
    }

    private static RecordAccessAuditOptions Enabled() => new() { Enabled = true, MaxWriteRetries = 3 };

    private RecordAccessAuditor CreateAuditor(IRepository<AuditRecordAccess, Guid>? repository = null)
    {
        repository ??= new EFCoreRepository<AuditTestDbContext, AuditRecordAccess, Guid>(
            DbContext, serviceProvider: ServiceProvider);
        return new RecordAccessAuditor(repository, new StaticOptionsMonitor<RecordAccessAuditOptions>(Enabled()), ServiceProvider);
    }

    private IUnitOfWorkManager Manager => ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

    private Task<List<AuditRecordAccess>> ChainAsync()
    {
        DbContext.ChangeTracker.Clear();
        return DbContext.Set<AuditRecordAccess>().AsNoTracking().OrderBy(e => e.Sequence).ToListAsync();
    }

    [Fact]
    public async Task TwoRegistrationsInsideOneUnitOfWork_GetConsecutiveSequences_AndCommit()
    {
        var auditor = CreateAuditor();

        Manager.EnableTransaction();
        var first = await auditor.RecordAsync("Tip", "1");
        var second = await auditor.RecordAsync("Tip", "2");
        await Manager.CommitTransactionAsync();

        first.Succeeded.ShouldBeTrue();
        second.Succeeded.ShouldBeTrue();

        var chain = await ChainAsync();
        chain.Select(e => e.Sequence).ShouldBe([1L, 2L]);
        chain[1].PreviousHash.ShouldBe(chain[0].Hash);
    }

    [Fact]
    public async Task RollingBackTheUnitOfWork_RemovesTheRegistrations()
    {
        // flush 必须留在调用方的事务里：审计条目不能比它所审计的业务操作活得更久。
        var auditor = CreateAuditor();

        Manager.EnableTransaction();
        (await auditor.RecordAsync("Tip", "1")).Succeeded.ShouldBeTrue();
        await Manager.RollbackTransactionAsync();

        (await ChainAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task AConflictInsideAUnitOfWork_IsRetriedInsideTheLoop_NotAtCommit()
    {
        var repository = new RacingRepository(DbContext, ServiceProvider);
        var auditor = CreateAuditor(repository);

        Manager.EnableTransaction();
        var result = await auditor.RecordAsync("Tip", "mine");
        await Manager.CommitTransactionAsync();

        result.Succeeded.ShouldBeTrue();
        repository.InsertAttempts.ShouldBe(2);

        var chain = await ChainAsync();
        chain.Select(e => e.ResourceId).ShouldBe(["rival", "mine"]);
        chain.Select(e => e.Sequence).ShouldBe([1L, 2L]);
        chain[1].PreviousHash.ShouldBe("rival-hash");
    }

    [Fact]
    public async Task AConflictWithoutATransaction_IsRetried_AndTheFailedEntityIsNotReplayed()
    {
        var repository = new RacingRepository(DbContext, ServiceProvider);
        var auditor = CreateAuditor(repository);

        var result = await auditor.RecordAsync("Tip", "mine");

        result.Succeeded.ShouldBeTrue();
        repository.InsertAttempts.ShouldBe(2);

        // 失败的那个实体必须已被丢弃：作用域内下一次无关的 SaveChanges 不能再撞一次索引。
        await Should.NotThrowAsync(() => DbContext.SaveChangesAsync());

        var chain = await ChainAsync();
        chain.Select(e => e.Sequence).ShouldBe([1L, 2L]);
        chain[1].PreviousHash.ShouldBe("rival-hash");
    }
}
