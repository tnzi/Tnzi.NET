using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tnzi.Data;
using Tnzi.Domain.Repositories;
using Tnzi.EFCore;
using Tnzi.Identity.Services;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// 启动那一刻数据库不可达：锁定修复任务必须<b>抛出</b>（运行器据此在后台重试、健康检查报未就绪），
/// 数据库恢复后再跑一次即修好。吞掉失败、正常返回的任务永远不会被重试。
/// </summary>
/// <remarks>跑在真实 SQLite 上，「不可达」由命令拦截器在执行前抛 SQLite 的 CANTOPEN 模拟。</remarks>
public class LockoutRepairDatabaseOutageTests : RelationalIdentityIntegrationTestBase
{
    private static readonly OutageInterceptor Outage = new();

    public LockoutRepairDatabaseOutageTests()
        : base(
            configureIdentity: o => o.AccountSecurity.EnableLockout = true,
            configureServices: s =>
            {
                s.ConfigureDbContext<TestIdentityDbContext>(o => o.AddInterceptors(Outage));
                s.AddScoped<IRepository<User, Guid>>(sp =>
                    new EFCoreRepository<TestIdentityDbContext, User, Guid>(sp.GetRequiredService<TestIdentityDbContext>(), serviceProvider: sp));
            })
    {
        Outage.Down = false;
    }

    [Fact]
    public async Task TheRepair_FailsLoudlyWhileTheDatabaseIsUnreachable_AndRepairsOnceItIsBack()
    {
        var victim = await CreateUserAsync(email: "victim@example.com");
        victim.LockoutEnabled = false;
        await SaveChangesAsync();
        IPostMigrationStartupTask task = new LockoutProtectionRepairStartupTask();

        Outage.Down = true;
        await Assert.ThrowsAsync<SqliteException>(() => task.ExecuteAsync(ServiceProvider));

        Outage.Down = false;
        await task.ExecuteAsync(ServiceProvider);

        DbContext.ChangeTracker.Clear();
        Assert.True((await DbContext.Users.SingleAsync(u => u.Id == victim.Id)).LockoutEnabled);
    }

    /// <summary><see cref="Down"/> 时每条命令都像连不上库那样失败。xUnit 在同一个类内顺序执行。</summary>
    private sealed class OutageInterceptor : DbCommandInterceptor
    {
        public volatile bool Down;

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => Down ? throw Unreachable() : base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
            => Down ? throw Unreachable() : base.ReaderExecutingAsync(command, eventData, result, cancellationToken);

        private static SqliteException Unreachable() => new("unable to open database file", 14);
    }
}
