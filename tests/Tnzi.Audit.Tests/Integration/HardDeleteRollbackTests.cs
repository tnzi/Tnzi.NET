using Tnzi.Data;
using Tnzi.EFCore;

namespace Tnzi.Audit.Tests.Integration;

/// <summary>
/// <see cref="HardDeleteDataDestroyer"/> 的裸 SQL（ExecuteDelete）必须加入调用方事务：
/// 手动触发销毁的端点包在工作单元里，外层回滚必须能撤掉已删的行。
/// </summary>
/// <remarks>
/// 框架物理事务延迟到首次 UoW SaveChanges 才 BEGIN；销毁是这条路径里的首个写操作，
/// 前面只有查询（找到期候选、问保全），没有任何 flush 会顺手开事务 ——
/// 去掉 <c>EnsureTransactionStartedAsync</c>，删除就落在自动提交模式下，回滚撤不掉。
/// </remarks>
public class HardDeleteRollbackTests : IntegrationTestBase
{
    private sealed class MapEntityManager : IEntityManager
    {
        public void Initialize()
        {
        }

        public IEntityRegister[] GetEntityRegisters(Type dbContextType) => [];

        public Type GetDbContextTypeForEntity(Type entityType) => typeof(AuditTestDbContext);

        public Type[] GetAllEntityTypes() => [typeof(RetentionTestRecord)];

        public Type[] GetAllDbContextTypes() => [typeof(AuditTestDbContext)];
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        services.AddSingleton<IEntityManager>(new MapEntityManager());
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
    }

    [Fact]
    public async Task Destroy_InsideCallerTransaction_RollbackRestoresRows()
    {
        var rows = new List<RetentionTestRecord>
        {
            new() { Category = "expired-a" },
            new() { Category = "expired-b" }
        };
        DbContext.Set<RetentionTestRecord>().AddRange(rows);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var manager = ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var destroyer = new HardDeleteDataDestroyer(
            ServiceProvider, ServiceProvider.GetRequiredService<IEntityManager>());

        manager.EnableTransaction();
        var destroyed = await destroyer.DestroyAsync<RetentionTestRecord>(rows);
        destroyed.ShouldBe(2);
        await manager.RollbackTransactionAsync();

        // no-tracking + IgnoreQueryFilters：证明行真的还在库里，而不是变更跟踪器里的残影
        DbContext.ChangeTracker.Clear();
        var survivors = await DbContext.Set<RetentionTestRecord>()
            .IgnoreQueryFilters().AsNoTracking().CountAsync();
        survivors.ShouldBe(2);
    }
}
