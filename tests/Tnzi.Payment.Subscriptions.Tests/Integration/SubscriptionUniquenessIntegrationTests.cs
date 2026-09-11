
namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 「同一用户同一产品至多一条有效订阅」由数据库兜底，而不是只由一次读检查兜底。
/// </summary>
/// <remarks>
/// <c>CreateSubscriptionAsync</c> 先查一次「有没有有效订阅」，再做绑卡、试算券、计税、
/// 建首期支付单，最后才写入 —— 中间隔着好几次外部调用。结账页一次双击（或客户端重试）
/// 落在这个窗口里，两条请求都读到「没有」，于是两条 Active 订阅、两次首付、
/// 此后每个周期双倍扣款。索引不唯一时，这条不变量没有任何东西在守。
/// <para>
/// 可空的 <c>ProductCode</c> 必须拆成两条索引：各家数据库对唯一索引里的 NULL 判定不同
/// （PostgreSQL / SQLite 认为 NULL 互不相等，SQL Server 认为相等），
/// 而「在另一个库上跑得好好的」正是这类缺陷最擅长的伪装。
/// </para>
/// </remarks>
public class SubscriptionUniquenessIntegrationTests : SubscriptionsIntegrationTestBase
{
    /// <summary>
    /// 计划是真实存在的一行：<c>PlanId</c> 是外键，随手编一个 Guid 会让每条用例都因为
    /// 外键失败而抛 <c>DbUpdateException</c> —— 断言「抛了」的那几条会因此假绿。
    /// </summary>
    private async Task<Guid> SeedPlanAsync()
    {
        var plan = new SubscriptionPlan
        {
            PlanCode = $"P{Guid.NewGuid():N}",
            PlanName = "Pro",
            Price = 30m,
            Currency = "USD",
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            IsActive = true
        };
        await SeedAsync(plan);
        return plan.Id;
    }

    private static Subscription Active(Guid userId, Guid planId, string? productCode, SubscriptionStatus status = SubscriptionStatus.Active) => new()
    {
        SubscriptionNo = $"SUB{Guid.NewGuid():N}",
        UserId = userId,
        ProductCode = productCode,
        PlanId = planId,
        Status = status,
        CycleType = BillingCycleType.Month,
        CycleValue = 1,
        StartTime = DateTime.UtcNow,
        OriginalPrice = 30m,
        Currency = "USD",
        ChannelCode = "Null"
    };

    [Fact]
    public async Task ASecondActiveSubscriptionForTheSameProduct_IsRefusedByTheDatabase()
    {
        var user = Guid.NewGuid();
        var plan = await SeedPlanAsync();
        await SeedAsync(Active(user, plan, "SUITE"));

        await Should.ThrowAsync<DbUpdateException>(() => SeedAsync(Active(user, plan, "SUITE")));
    }

    /// <summary>
    /// <c>ProductCode</c> 为 null 的那一支同样只许一条 —— 不设产品概念的宿主
    /// 全部订阅都落在这一支上，漏掉它等于对它们完全没有约束。
    /// </summary>
    [Fact]
    public async Task ASecondActiveSubscriptionWithoutAProductCode_IsAlsoRefused()
    {
        var user = Guid.NewGuid();
        var plan = await SeedPlanAsync();
        await SeedAsync(Active(user, plan, null));

        await Should.ThrowAsync<DbUpdateException>(() => SeedAsync(Active(user, plan, null)));
    }

    [Fact]
    public async Task DifferentProducts_Coexist()
    {
        var user = Guid.NewGuid();
        var plan = await SeedPlanAsync();
        await SeedAsync(Active(user, plan, "SUITE"));
        await SeedAsync(Active(user, plan, "ADDON"));
    }

    [Fact]
    public async Task DifferentUsers_Coexist()
    {
        var plan = await SeedPlanAsync();
        await SeedAsync(Active(Guid.NewGuid(), plan, "SUITE"));
        await SeedAsync(Active(Guid.NewGuid(), plan, "SUITE"));
    }

    /// <summary>
    /// 已终止的订阅不占名额：退订之后必须能重新订阅同一个产品，
    /// 否则这条约束会把一个正常的业务动作永久堵死。
    /// </summary>
    [Theory]
    [InlineData(SubscriptionStatus.Cancelled)]
    [InlineData(SubscriptionStatus.Expired)]
    public async Task ATerminatedSubscription_DoesNotBlockANewOne(SubscriptionStatus terminated)
    {
        var user = Guid.NewGuid();
        var plan = await SeedPlanAsync();
        await SeedAsync(Active(user, plan, "SUITE", terminated));

        await SeedAsync(Active(user, plan, "SUITE"));
    }

    /// <summary>
    /// 中间态之间也不许并存：<c>Pending</c>（等首付）与 <c>Active</c> 各一条，
    /// 正是双击结账最典型的落点。
    /// </summary>
    [Fact]
    public async Task APendingAndAnActiveSubscription_CannotCoexist()
    {
        var user = Guid.NewGuid();
        var plan = await SeedPlanAsync();
        await SeedAsync(Active(user, plan, "SUITE", SubscriptionStatus.Pending));

        await Should.ThrowAsync<DbUpdateException>(() => SeedAsync(Active(user, plan, "SUITE")));
    }
}
