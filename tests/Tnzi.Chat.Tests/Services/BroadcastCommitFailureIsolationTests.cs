using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tnzi.Chat.Tests.Services;

/// <summary>
/// 广播扇出时，一个收件人在<b>提交</b>时失败（唯一约束、并发戳冲突），后面的收件人照常送达。
/// </summary>
/// <remarks>
/// 每个收件人各一个工作单元、在同一个作用域里顺序执行。提交失败后被回滚的会话 / 成员 / 消息若留在变更跟踪器里，
/// 下一个收件人的提交会把它们一起重放、同一个异常再抛，此后的收件人一个都送不到，
/// 而调用方看到的只是「有几个没送到」。
/// </remarks>
public class BroadcastCommitFailureIsolationTests : Integration.IntegrationTestBase
{
    // 字段初始化先于基类构造函数执行，ConfigureDbContextOptions 被调用时它已经就位
    private readonly FailingMemberInterceptor _interceptor = new();

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        => options.AddInterceptors(_interceptor);

    [Fact]
    public async Task A_recipient_failing_at_commit_does_not_take_down_the_rest_of_the_fan_out()
    {
        var first = Guid.NewGuid();
        var failing = Guid.NewGuid();
        var last = Guid.NewGuid();
        _interceptor.FailingUserId = failing;

        var result = await ServiceProvider.GetRequiredService<IBroadcastService>()
            .BroadcastToUsersAsync([first, failing, last], "Maintenance at 21:00 UTC");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(2, result.Data);

        DbContext.ChangeTracker.Clear();
        var members = await DbContext.Set<ConversationMember>().AsNoTracking().Select(m => m.UserId).ToListAsync();
        Assert.Contains(first, members);
        Assert.Contains(last, members);
        Assert.DoesNotContain(failing, members);
    }

    /// <summary>保存一个指定用户的新成员时抛异常，模拟提交阶段撞上唯一约束。</summary>
    private sealed class FailingMemberInterceptor : SaveChangesInterceptor
    {
        public Guid? FailingUserId { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var failing = eventData.Context?.ChangeTracker.Entries<ConversationMember>()
                .Any(e => e.State == EntityState.Added && e.Entity.UserId == FailingUserId) == true;
            if (failing)
            {
                throw new DbUpdateException("simulated unique constraint violation at commit");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
