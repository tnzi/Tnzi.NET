namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 关了自动续费的订阅到期即过期，与它是从哪个状态进入「在跑」的无关；扣款在途的订阅不被过期扫描抢先结束。
/// </summary>
/// <remarks>
/// 续费扫描只取 <c>AutoRenew</c> 为真的订阅，过期扫描此前只取 <c>PendingRenewal</c>：一条 <c>Active</c> 且关了
/// 自动续费的订阅两边都不收，付一期用到永远，统计里还一直算作活跃。所有进入在跑状态的写入点现在都按开关推导
/// <c>Active</c> / <c>PendingRenewal</c>，但修复之前落下的这种行仍在库里，过期扫描必须把它们也收掉。
/// </remarks>
public class SubscriptionEndOfPeriodExpiryTests : SubscriptionsIntegrationTestBase
{
    private async Task<SubscriptionPlan> SeedPlanAsync()
    {
        var plan = new SubscriptionPlan
        {
            PlanCode = $"P{Guid.NewGuid():N}"[..12],
            PlanName = "Pro",
            Price = 30m,
            Currency = "USD",
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            IsActive = true
        };
        await SeedAsync(plan);
        return plan;
    }

    private async Task<Subscription> SeedAsync(SubscriptionPlan plan, string subscriptionNo, Action<Subscription>? shape = null)
    {
        var subscription = new Subscription
        {
            SubscriptionNo = subscriptionNo,
            UserId = TestHelper.DefaultTestUserId,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Active,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            StartTime = DateTime.UtcNow.AddMonths(-1),
            NextBillingTime = DateTime.UtcNow.AddDays(10),
            OriginalPrice = 30m,
            PaidAmount = 30m,
            Currency = "USD",
            AutoRenew = true,
            ChannelCode = "Null",
            PaymentMethodToken = "pm_test",
            ProviderCustomerId = "cus_test",
            LastBillingTradeNo = $"PAY-SEED-{subscriptionNo}"
        };
        shape?.Invoke(subscription);
        await SeedAsync(subscription);
        return subscription;
    }

    private async Task UpdateAsync(Guid id, Action<Subscription> change)
    {
        using var scope = ServiceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
        change(ctx.Set<Subscription>().First(s => s.Id == id));
        await ctx.SaveChangesAsync();
    }

    private Task<Result<int>> ExpireAsync() =>
        InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ExpireOverdueSubscriptionsAsync());

    private Task<Result> SetAutoRenewAsync(Guid id, bool autoRenew) =>
        InScopeAsync<ISubscriptionService, Result>(svc => svc.UpdateAutoRenewAsync(id, autoRenew));

    /// <summary>修复之前落下的 Active + 关了自动续费：周期走完即过期，而不是永远 Active。</summary>
    [Fact]
    public async Task AnActiveSubscriptionWithAutoRenewOff_ExpiresWhenItsPeriodEnds()
    {
        var plan = await SeedPlanAsync();
        var subscription = await SeedAsync(plan, "SUB-EOP-1", s =>
        {
            s.AutoRenew = false;
            s.NextBillingTime = DateTime.UtcNow.AddMinutes(-1);
        });

        await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.RenewExpiredSubscriptionsAsync());
        await ExpireAsync();

        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.Expired);
    }

    /// <summary>同一形状但周期还没走完：不提前结束。</summary>
    [Fact]
    public async Task AnActiveSubscriptionWithAutoRenewOff_KeepsTheRestOfItsPaidPeriod()
    {
        var plan = await SeedPlanAsync();
        var subscription = await SeedAsync(plan, "SUB-EOP-2", s => s.AutoRenew = false);

        await ExpireAsync();

        (await ReloadAsync<Subscription>(subscription.Id))!.Status.ShouldBe(SubscriptionStatus.Active);
    }

    /// <summary>
    /// 逾期时关掉自动续费、再手动重试扣款成功：这一期归他，到期过期，而不是回到一条永不过期的 Active。
    /// </summary>
    [Fact]
    public async Task APastDueRetrySucceedingAfterAutoRenewWasTurnedOff_EndsWithThePeriod()
    {
        var plan = await SeedPlanAsync();
        var subscription = await SeedAsync(plan, "SUB-EOP-3", s =>
        {
            s.Status = SubscriptionStatus.PastDue;
            s.PastDueSince = DateTime.UtcNow.AddDays(-1);
            s.RenewalRetryCount = 1;
            s.NextBillingTime = DateTime.UtcNow.AddDays(-1);
        });

        (await SetAutoRenewAsync(subscription.Id, false)).Succeeded.ShouldBeTrue();
        (await InScopeAsync<ISubscriptionService, Result>(svc => svc.RetryBillingAsync(subscription.Id))).Succeeded.ShouldBeTrue();

        var paid = await ReloadAsync<Subscription>(subscription.Id);
        paid!.Status.ShouldBe(SubscriptionStatus.PendingRenewal);
        paid.NextBillingTime!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddDays(25));

        await UpdateAsync(subscription.Id, s => s.NextBillingTime = DateTime.UtcNow.AddMinutes(-1));
        await ExpireAsync();
        (await ReloadAsync<Subscription>(subscription.Id))!.Status.ShouldBe(SubscriptionStatus.Expired);
    }

    /// <summary>关掉再打开自动续费：回到 Active 照常续费（回归）。</summary>
    [Fact]
    public async Task TurningAutoRenewOffAndOnAgain_LeavesAnActiveSubscription()
    {
        var plan = await SeedPlanAsync();
        var subscription = await SeedAsync(plan, "SUB-EOP-4");

        (await SetAutoRenewAsync(subscription.Id, false)).Succeeded.ShouldBeTrue();
        (await ReloadAsync<Subscription>(subscription.Id))!.Status.ShouldBe(SubscriptionStatus.PendingRenewal);

        (await SetAutoRenewAsync(subscription.Id, true)).Succeeded.ShouldBeTrue();
        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.Active);
        reloaded.AutoRenew.ShouldBeTrue();
    }

    /// <summary>
    /// 扣款在途（计费锁未过期）的订阅不被过期扫描结束：否则钱随后到账，撞上已过期的订阅只剩一行孤儿付款告警。
    /// 锁释放之后照常过期。
    /// </summary>
    [Fact]
    public async Task ASubscriptionWithAChargeInFlight_IsNotExpiredUntilTheChargeSettles()
    {
        var plan = await SeedPlanAsync();
        var subscription = await SeedAsync(plan, "SUB-EOP-5", s =>
        {
            s.Status = SubscriptionStatus.PendingRenewal;
            s.AutoRenew = false;
            s.NextBillingTime = DateTime.UtcNow.AddMinutes(-1);
            s.BillingLockedUntil = DateTime.UtcNow.AddMinutes(10);
        });

        await ExpireAsync();
        (await ReloadAsync<Subscription>(subscription.Id))!.Status.ShouldBe(SubscriptionStatus.PendingRenewal);

        await UpdateAsync(subscription.Id, s => s.BillingLockedUntil = null);
        await ExpireAsync();
        (await ReloadAsync<Subscription>(subscription.Id))!.Status.ShouldBe(SubscriptionStatus.Expired);
    }
}
