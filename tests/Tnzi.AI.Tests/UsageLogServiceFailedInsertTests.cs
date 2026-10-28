using Microsoft.AspNetCore.Http;

namespace Tnzi.AI.Tests;

/// <summary>
/// 记账失败不能变成运行失败：插入失败的那一行若留在变更跟踪器里（仍是 Added），
/// 本作用域下一次 SaveChanges（运行收尾、历史落库）会重放它再失败一次。
/// </summary>
public class UsageLogServiceFailedInsertTests : AIAnalyticsTestBase
{
    [Fact]
    public async Task LogUsageAsync_InsertFails_LeavesNothingPendingInTheChangeTracker()
    {
        var service = new UsageLogService(
            new FailingInsertRepository(DbContext),
            new HttpContextAccessor(),
            ServiceProvider);

        await service.LogUsageAsync("chat", "OpenAI", "gpt-4o", 10, 5, 100, true);

        DbContext.ChangeTracker.Entries<UsageLog>().ShouldBeEmpty("a failed usage row must not be replayed by the next save in this scope");
        await Should.NotThrowAsync(() => DbContext.SaveChangesAsync());
    }

    /// <summary>行已加入跟踪器、保存时失败，与真实的约束冲突 / 列超长同一形状。</summary>
    private sealed class FailingInsertRepository : EFCoreRepository<AIAnalyticsDbContext, UsageLog, Guid>
    {
        private readonly AIAnalyticsDbContext _context;

        public FailingInsertRepository(AIAnalyticsDbContext context) : base(context)
        {
            _context = context;
        }

        public override async Task InsertAsync(UsageLog entity, CancellationToken cancellationToken = default)
        {
            await _context.Set<UsageLog>().AddAsync(entity, cancellationToken);
            throw new DbUpdateException("simulated constraint violation");
        }
    }
}
