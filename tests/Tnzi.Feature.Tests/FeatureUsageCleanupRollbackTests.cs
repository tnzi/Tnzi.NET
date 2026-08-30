using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tnzi.Data;
using Tnzi.Domain.Repositories;
using Tnzi.EFCore;
using Tnzi.Feature.Entities;
using Tnzi.Feature.Entities.Configs;
using Tnzi.Feature.Services;
using Tnzi.Security.Claims;
using Tnzi.TestBase;

namespace Tnzi.Feature.Tests;

/// <summary>Feature 用量清理的集成测试 DbContext（只挂被试实体）。</summary>
public class FeatureUsageTestDbContext : TnziDbContext<FeatureUsageTestDbContext>
{
    public FeatureUsageTestDbContext(DbContextOptions<FeatureUsageTestDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new FeatureUsageRecordConfiguration());
        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// <see cref="FeatureUsageService.CleanupOldRecordsAsync"/> 的裸 SQL（ExecuteDelete）
/// 必须加入调用方事务：请求回滚时，清理掉的用量记录要能被撤回。
/// </summary>
/// <remarks>
/// 框架物理事务延迟到首次 UoW SaveChanges 才 BEGIN；清理是这条路径的首个写操作，
/// 前面没有任何 flush 会顺手开事务——去掉 <c>EnsureTransactionStartedAsync</c>，
/// 删除就落在自动提交模式下，外层回滚撤不掉（真实 SQLite + UnitOfWorkManager，
/// Mock 仓储下这个缺陷根本不存在）。
/// </remarks>
public class FeatureUsageCleanupRollbackTests : IntegratedTestBase<FeatureUsageTestDbContext>
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns([typeof(FeatureUsageTestDbContext)]);
        services.AddSingleton(_ => entityManagerMock.Object);

        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
        services.AddScoped<IRepository<FeatureUsageRecord, long>,
            EFCoreRepository<FeatureUsageTestDbContext, FeatureUsageRecord, long>>();
        services.AddScoped<IFeatureUsageService, FeatureUsageService>();
    }

    [Fact]
    public async Task Cleanup_InsideCallerTransaction_RollbackRestoresRows()
    {
        // Arrange：两条一年前的记录（CreationTime 由审计填充器自动盖章，
        // 用 ExecuteUpdate 裸 SQL 回填旧日期以绕过它）
        DbContext.Set<FeatureUsageRecord>().AddRange(
            new FeatureUsageRecord { FeatureName = "feature-a" },
            new FeatureUsageRecord { FeatureName = "feature-b" });
        await DbContext.SaveChangesAsync();

        var backdated = DateTime.UtcNow.AddDays(-365);
        await DbContext.Set<FeatureUsageRecord>()
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreationTime, backdated));
        DbContext.ChangeTracker.Clear();

        var manager = ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var service = ServiceProvider.GetRequiredService<IFeatureUsageService>();

        // Act：调用方事务内清理，然后回滚
        manager.EnableTransaction();
        var result = await service.CleanupOldRecordsAsync(90);
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBe(2);
        await manager.RollbackTransactionAsync();

        // Assert：no-tracking 读，证明行真的还在库里
        DbContext.ChangeTracker.Clear();
        var survivors = await DbContext.Set<FeatureUsageRecord>().AsNoTracking().CountAsync();
        survivors.ShouldBe(2);
    }
}
