namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 后台扫描按抢到计费锁那一刻库里的样子办事，而不是按一页快照读出来时的样子。
/// </summary>
/// <remarks>
/// <para>
/// 续费扫描一次读出至多一页并带上计划，逐条抢锁、扣款。此前抢锁之后不重读：同一条订阅在快照之后被换了计划
/// （另一个实例的第六条扫描结算了到期降级，或单实例下用户做了一次补差为 0 的立即升级），
/// 「先结算到期变更」那一步找不到待生效的变更，于是回退到快照里的旧计划、按旧价扣，账单与订阅详情对不上。
/// </para>
/// <para>
/// 这里用第一条订阅扣款完成的事件确定性地插进那个并发动作：扫描轮到第二条时，它的快照已经过时。
/// </para>
/// </remarks>
public class RenewalPricedAtClaimTests : SubscriptionsIntegrationTestBase
{
    private readonly TestEventHooks _hooks = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        _hooks.Register(services);
    }

    private async Task<SubscriptionPlan> SeedPlanAsync(string name, decimal price)
    {
        var plan = new SubscriptionPlan
        {
            PlanCode = $"P{Guid.NewGuid():N}"[..12],
            PlanName = name,
            Price = price,
            Currency = "USD",
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            IsActive = true
        };
        await SeedAsync(plan);
        return plan;
    }

    private async Task<Subscription> SeedDueAsync(SubscriptionPlan plan, TimeSpan overdueBy, Action<Subscription>? shape = null)
    {
        var subscription = new Subscription
        {
            SubscriptionNo = $"SUB{Guid.NewGuid():N}",
            UserId = Guid.NewGuid(),
            PlanId = plan.Id,
            Status = SubscriptionStatus.Active,
            CycleType = plan.CycleType,
            CycleValue = plan.CycleValue,
            StartTime = DateTime.UtcNow.AddMonths(-1),
            NextBillingTime = DateTime.UtcNow - overdueBy,
            OriginalPrice = plan.Price,
            Currency = plan.Currency,
            AutoRenew = true,
            ChannelCode = "Null",
            PaymentMethodToken = "pm_test",
            ProviderCustomerId = "cus_test",
            LastBillingTradeNo = "PAY-SEED-" + Guid.NewGuid().ToString("N")
        };
        shape?.Invoke(subscription);
        await SeedAsync(subscription);
        return subscription;
    }

    private async Task<decimal> ChargedAmountAsync(string subscriptionNo)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<PaymentEntity, Guid>>();
        var payment = await repo.FirstOrDefaultAsync(p => p.BusinessOrderNo == subscriptionNo && p.Status == PaymentStatus.Succeeded);
        payment.ShouldNotBeNull($"no successful charge for {subscriptionNo}");
        return payment!.OriginalAmount;
    }

    private Task<Result<int>> RenewAsync() =>
        InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.RenewExpiredSubscriptionsAsync());

    /// <summary>
    /// ★ 多实例：续费扫描读完快照之后，另一个实例的第六条扫描结算了这条订阅到期的降级。续费必须按降级后的价格扣。
    /// </summary>
    [Fact]
    public async Task ADowngradeSettledAfterTheSnapshot_IsChargedAtTheNewPrice()
    {
        var pro = await SeedPlanAsync("Pro", 50m);
        var basic = await SeedPlanAsync("Basic", 20m);
        var first = await SeedDueAsync(pro, TimeSpan.FromHours(2));
        var second = await SeedDueAsync(pro, TimeSpan.FromHours(1));
        await SeedAsync(new SubscriptionChange
        {
            SubscriptionId = second.Id,
            FromPlanId = pro.Id,
            ToPlanId = basic.Id,
            ChangeType = SubscriptionChangeType.Downgrade,
            EffectiveDate = second.NextBillingTime!.Value,
            Status = SubscriptionChangeStatus.Pending
        });
        _hooks.OnPaymentCompleted(first.SubscriptionNo, async () =>
            (await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ApplyDuePlanChangesAsync())).Data.ShouldBe(1));

        (await RenewAsync()).Data.ShouldBe(2);

        (await ChargedAmountAsync(first.SubscriptionNo)).ShouldBe(50m);
        (await ChargedAmountAsync(second.SubscriptionNo)).ShouldBe(20m);
        (await ReloadAsync<Subscription>(second.Id))!.PlanId.ShouldBe(basic.Id);
    }

    /// <summary>
    /// ★ 单实例：周期已过而续费还没轮到它时，用户做了一次补差为 0 的立即升级。续费必须按升级后的价格扣，
    /// 而不是以旧价续上新计划一整期。
    /// </summary>
    [Fact]
    public async Task AnUpgradeAppliedAfterTheSnapshot_IsChargedAtTheNewPrice()
    {
        var pro = await SeedPlanAsync("Pro", 50m);
        var premium = await SeedPlanAsync("Premium", 80m);
        var first = await SeedDueAsync(pro, TimeSpan.FromHours(2));
        var second = await SeedDueAsync(pro, TimeSpan.FromHours(1), s => s.UserId = TestHelper.DefaultTestUserId);
        _hooks.OnPaymentCompleted(first.SubscriptionNo, async () =>
        {
            var changed = await InScopeAsync<ISubscriptionService, Result<SubscriptionChangeDto>>(svc =>
                svc.ChangeSubscriptionPlanAsync(second.Id, new ChangeSubscriptionPlanDto { NewPlanId = premium.Id, EffectiveImmediately = true }));
            changed.Succeeded.ShouldBeTrue(changed.Message);
            changed.Data!.Status.ShouldBe(SubscriptionChangeStatus.Applied);
        });

        (await RenewAsync()).Data.ShouldBe(2);

        (await ChargedAmountAsync(second.SubscriptionNo)).ShouldBe(80m);
    }

    /// <summary>
    /// 第六条扫描与续费扫描抢同一把锁：续费正持有这条订阅时，第六条扫描这一轮不碰它的变更
    /// （由续费在扣款之前结算，或下一轮再来）。
    /// </summary>
    [Fact]
    public async Task TheDueChangeScan_LeavesALockedSubscriptionAlone()
    {
        var pro = await SeedPlanAsync("Pro", 50m);
        var basic = await SeedPlanAsync("Basic", 20m);
        var subscription = await SeedDueAsync(pro, TimeSpan.FromHours(1), s => s.BillingLockedUntil = DateTime.UtcNow.AddMinutes(5));
        var change = new SubscriptionChange
        {
            SubscriptionId = subscription.Id,
            FromPlanId = pro.Id,
            ToPlanId = basic.Id,
            ChangeType = SubscriptionChangeType.Downgrade,
            EffectiveDate = subscription.NextBillingTime!.Value,
            Status = SubscriptionChangeStatus.Pending
        };
        await SeedAsync(change);

        (await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ApplyDuePlanChangesAsync())).Data.ShouldBe(0);

        (await ReloadAsync<SubscriptionChange>(change.Id))!.Status.ShouldBe(SubscriptionChangeStatus.Pending);
        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.PlanId.ShouldBe(pro.Id);
        reloaded.BillingLockedUntil.ShouldNotBeNull();
    }

    /// <summary>第六条扫描结算完放锁：之后的续费照常抢得到。</summary>
    [Fact]
    public async Task TheDueChangeScan_ReleasesTheLockItTook()
    {
        var pro = await SeedPlanAsync("Pro", 50m);
        var basic = await SeedPlanAsync("Basic", 20m);
        var subscription = await SeedDueAsync(pro, TimeSpan.FromHours(1), s => s.AutoRenew = false);
        await SeedAsync(new SubscriptionChange
        {
            SubscriptionId = subscription.Id,
            FromPlanId = pro.Id,
            ToPlanId = basic.Id,
            ChangeType = SubscriptionChangeType.Downgrade,
            EffectiveDate = subscription.NextBillingTime!.Value,
            Status = SubscriptionChangeStatus.Pending
        });

        (await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ApplyDuePlanChangesAsync())).Data.ShouldBe(1);

        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.PlanId.ShouldBe(basic.Id);
        reloaded.OriginalPrice.ShouldBe(20m);
        reloaded.BillingLockedUntil.ShouldBeNull();
    }

    /// <summary>
    /// ★ 过期扫描同形：取出一页待过期的 Id 之后，第二条被用户重新打开了自动续费（回到在跑状态）。
    /// 抢到锁之后必须重新判定，而不是把一条已经在跑的订阅过期掉。
    /// </summary>
    [Fact]
    public async Task TheExpiryScan_DoesNotExpireASubscriptionResumedMeanwhile()
    {
        var pro = await SeedPlanAsync("Pro", 50m);
        var first = await SeedDueAsync(pro, TimeSpan.FromHours(2), s =>
        {
            s.Status = SubscriptionStatus.PendingRenewal;
            s.AutoRenew = false;
        });
        var second = await SeedDueAsync(pro, TimeSpan.FromHours(1), s =>
        {
            s.UserId = TestHelper.DefaultTestUserId;
            s.Status = SubscriptionStatus.PendingRenewal;
            s.AutoRenew = false;
        });
        _hooks.OnSubscriptionExpired(first.SubscriptionNo, async () =>
            (await InScopeAsync<ISubscriptionService, Result>(svc => svc.UpdateAutoRenewAsync(second.Id, true))).Succeeded.ShouldBeTrue());

        (await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ExpireOverdueSubscriptionsAsync())).Data.ShouldBe(1);

        (await ReloadAsync<Subscription>(first.Id))!.Status.ShouldBe(SubscriptionStatus.Expired);
        var resumed = await ReloadAsync<Subscription>(second.Id);
        resumed!.Status.ShouldBe(SubscriptionStatus.Active);
        resumed.AutoRenew.ShouldBeTrue();
        resumed.BillingLockedUntil.ShouldBeNull();
    }
}
