namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 待生效（非立即）计划变更的结算链路。
/// </summary>
/// <remarks>
/// 此前这条链路只有前半截：<c>ChangeSubscriptionPlanAsync</c> 插一条
/// <c>Status=Pending, EffectiveDate=NextBillingTime</c> 的变更记录，而全模块没有任何代码读
/// <c>EffectiveDate</c> 判到期，五条后台扫描一条都不碰变更表。后果有两层：
/// 降级到期后仍按旧价扣款（用户按新价的心理预期与账单不符），
/// 而「已有待生效变更」的守卫又让该订阅此后所有变更请求 400 —— 一次降级把这条订阅的
/// 变更能力永久锁死，唯一出口是一个用户界面上没有入口的取消端点。
/// </remarks>
public class SubscriptionPlanChangeIntegrationTests : SubscriptionsIntegrationTestBase
{
    private async Task<SubscriptionPlan> SeedPlanAsync(string code, decimal price, int cycleValue = 1)
    {
        var plan = new SubscriptionPlan
        {
            PlanCode = code,
            PlanName = code,
            ProductCode = "SUITE",
            Price = price,
            Currency = "USD",
            CycleType = BillingCycleType.Month,
            CycleValue = cycleValue,
            IsActive = true
        };
        await SeedAsync(plan);
        return plan;
    }

    private async Task<Subscription> SeedSubscriptionAsync(
        SubscriptionPlan plan,
        string subscriptionNo,
        DateTime? nextBilling,
        SubscriptionStatus status = SubscriptionStatus.Active,
        bool autoRenew = true,
        string? paymentMethodToken = "pm_test")
    {
        var subscription = new Subscription
        {
            SubscriptionNo = subscriptionNo,
            UserId = Guid.NewGuid(),
            PlanId = plan.Id,
            ProductCode = plan.ProductCode,
            Status = status,
            CycleType = plan.CycleType,
            CycleValue = plan.CycleValue,
            StartTime = DateTime.UtcNow.AddMonths(-1),
            NextBillingTime = nextBilling,
            OriginalPrice = plan.Price,
            Currency = plan.Currency,
            AutoRenew = autoRenew,
            ChannelCode = "Null",
            PaymentMethodToken = paymentMethodToken,
            ProviderCustomerId = "cus_test"
        };
        await SeedAsync(subscription);
        return subscription;
    }

    private Task<Result<SubscriptionChangeDto>> RequestChangeAsync(Guid subscriptionId, Guid newPlanId) =>
        InScopeAsync<ISubscriptionService, Result<SubscriptionChangeDto>>(
            svc => svc.ChangeSubscriptionPlanAsync(subscriptionId, new ChangeSubscriptionPlanDto { NewPlanId = newPlanId }));

    private Task<Result<int>> RenewAsync() =>
        InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.RenewExpiredSubscriptionsAsync());

    private Task<Result<int>> ApplyDueChangesAsync() =>
        InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ApplyDuePlanChangesAsync());

    private async Task<SubscriptionChange?> ReloadChangeAsync(Guid id) => await ReloadAsync<SubscriptionChange>(id);

    private async Task<PaymentEntity?> ReloadPaymentByOrderNoAsync(string businessOrderNo)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<PaymentEntity, Guid>>();
        return await repo.FirstOrDefaultAsync(p => p.BusinessOrderNo == businessOrderNo);
    }

    /// <summary>
    /// 降级到期 → 续费扫描必须**先**结算变更再扣款。顺序反过来，用户在自己已经降级的
    /// 那一期仍被按旧价收钱，而账单与订阅详情页此后显示的是新计划。
    /// </summary>
    [Fact]
    public async Task DueDowngrade_IsAppliedBeforeTheRenewalCharge()
    {
        var pro = await SeedPlanAsync("PRO", 30m);
        var basic = await SeedPlanAsync("BASIC", 10m);
        var sub = await SeedSubscriptionAsync(pro, "SUB-DOWN1", DateTime.UtcNow.AddDays(-1));

        var change = await RequestChangeAsync(sub.Id, basic.Id);
        change.Succeeded.ShouldBeTrue();
        change.Data!.Status.ShouldBe(SubscriptionChangeStatus.Pending);

        var renewed = await RenewAsync();
        renewed.Succeeded.ShouldBeTrue();
        renewed.Data.ShouldBe(1);

        var reloaded = await ReloadAsync<Subscription>(sub.Id);
        reloaded!.PlanId.ShouldBe(basic.Id);
        reloaded.OriginalPrice.ShouldBe(10m);

        (await ReloadChangeAsync(change.Data.Id))!.Status.ShouldBe(SubscriptionChangeStatus.Applied);

        var payment = await ReloadPaymentByOrderNoAsync("SUB-DOWN1");
        payment.ShouldNotBeNull();
        payment!.Status.ShouldBe(PaymentStatus.Succeeded);
        payment.PayableAmount.ShouldBe(10m);
    }

    /// <summary>
    /// 结算变更时**不能**顺手推进 NextBillingTime：那一刻的计费时钟由续费本身推进，
    /// 两处都拨等于一次降级换来两个周期的免费服务。
    /// </summary>
    [Fact]
    public async Task DueDowngrade_AppliedByRenewal_AdvancesTheBillingClockExactlyOneCycle()
    {
        var pro = await SeedPlanAsync("PRO2", 30m);
        var basic = await SeedPlanAsync("BASIC2", 10m);
        var periodEnd = DateTime.UtcNow.AddHours(-1);
        var sub = await SeedSubscriptionAsync(pro, "SUB-DOWN2", periodEnd);

        await RequestChangeAsync(sub.Id, basic.Id);
        await RenewAsync();

        var reloaded = await ReloadAsync<Subscription>(sub.Id);
        // 一个月周期：新的计费时间应落在 periodEnd 之后一个月上下，而不是两个月
        reloaded!.NextBillingTime!.Value.ShouldBeLessThan(DateTime.UtcNow.AddDays(40));
        reloaded.NextBillingTime.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddDays(20));
    }

    /// <summary>
    /// 结算之后该订阅必须重新可以提出变更。此前守卫看到一条永远处于 Pending 的记录，
    /// 于是这条订阅的所有后续变更请求恒 400。
    /// </summary>
    [Fact]
    public async Task AfterTheDueChangeIsApplied_FurtherChangesAreAcceptedAgain()
    {
        var pro = await SeedPlanAsync("PRO3", 30m);
        var basic = await SeedPlanAsync("BASIC3", 10m);
        var mid = await SeedPlanAsync("MID3", 20m);
        var sub = await SeedSubscriptionAsync(pro, "SUB-DOWN3", DateTime.UtcNow.AddDays(-1));

        await RequestChangeAsync(sub.Id, basic.Id);
        await RenewAsync();

        var second = await RequestChangeAsync(sub.Id, mid.Id);
        second.Succeeded.ShouldBeTrue();
        second.Data!.FromPlanId.ShouldBe(basic.Id);
    }

    /// <summary>
    /// 独立扫描覆盖不参与续费的订阅（关掉自动续费、暂停中、逾期）——
    /// 它们同样约定了「周期结束时降级」，不该因为没走续费路径就永远停在 Pending。
    /// </summary>
    [Fact]
    public async Task DueChange_OnASubscriptionThatDoesNotRenew_IsStillApplied()
    {
        var pro = await SeedPlanAsync("PRO4", 30m);
        var basic = await SeedPlanAsync("BASIC4", 10m);
        var periodEnd = DateTime.UtcNow.AddHours(-1);
        var sub = await SeedSubscriptionAsync(pro, "SUB-DOWN4", periodEnd, autoRenew: false);

        var change = await RequestChangeAsync(sub.Id, basic.Id);
        change.Succeeded.ShouldBeTrue();

        var applied = await ApplyDueChangesAsync();
        applied.Succeeded.ShouldBeTrue();
        applied.Data.ShouldBe(1);

        var reloaded = await ReloadAsync<Subscription>(sub.Id);
        reloaded!.PlanId.ShouldBe(basic.Id);
        // 计费时钟原样不动：这里没有发生任何一次收款
        reloaded.NextBillingTime!.Value.ShouldBe(periodEnd, TimeSpan.FromSeconds(1));
        (await ReloadChangeAsync(change.Data!.Id))!.Status.ShouldBe(SubscriptionChangeStatus.Applied);
    }

    /// <summary>未到期的变更不能被提前结算。</summary>
    [Fact]
    public async Task ChangeThatIsNotDueYet_IsLeftPending()
    {
        var pro = await SeedPlanAsync("PRO5", 30m);
        var basic = await SeedPlanAsync("BASIC5", 10m);
        var sub = await SeedSubscriptionAsync(pro, "SUB-DOWN5", DateTime.UtcNow.AddDays(10));

        var change = await RequestChangeAsync(sub.Id, basic.Id);
        change.Succeeded.ShouldBeTrue();

        var applied = await ApplyDueChangesAsync();
        applied.Data.ShouldBe(0);

        (await ReloadAsync<Subscription>(sub.Id))!.PlanId.ShouldBe(pro.Id);
        (await ReloadChangeAsync(change.Data!.Id))!.Status.ShouldBe(SubscriptionChangeStatus.Pending);
    }

    /// <summary>
    /// 已终止的订阅不能被一条旧的待生效变更改写 —— 那会给一个已取消的订阅换上新计划，
    /// 并让它重新看起来像一份活订阅。
    /// </summary>
    [Fact]
    public async Task DueChange_OnACancelledSubscription_IsCancelledNotApplied()
    {
        var pro = await SeedPlanAsync("PRO6", 30m);
        var basic = await SeedPlanAsync("BASIC6", 10m);
        var sub = await SeedSubscriptionAsync(pro, "SUB-DOWN6", DateTime.UtcNow.AddDays(-1));

        var change = await RequestChangeAsync(sub.Id, basic.Id);
        change.Succeeded.ShouldBeTrue();

        await InScopeAsync<ISubscriptionService, Result>(svc =>
            svc.CancelSubscriptionAsync(sub.Id, new CancelSubscriptionDto { Reason = "test", Immediate = true }));

        var applied = await ApplyDueChangesAsync();
        applied.Data.ShouldBe(0);

        (await ReloadAsync<Subscription>(sub.Id))!.PlanId.ShouldBe(pro.Id);
        (await ReloadChangeAsync(change.Data!.Id))!.Status.ShouldBe(SubscriptionChangeStatus.Cancelled);
    }

    /// <summary>
    /// 按比例金额在周期长度算不出来时的兜底：此前返回**新计划全价**，
    /// 于是一次降级会向用户收取新计划的整整一期费用 —— 方向是反的。
    /// 兜底应当等价于「整个周期都还剩着」，即两个计划的差额（降级为负数=信用）。
    /// </summary>
    [Fact]
    public async Task ProrationFallback_ForADegeneratePeriod_KeepsTheDowngradeDirection()
    {
        var pro = await SeedPlanAsync("PRO7", 30m, cycleValue: 0);
        var basic = await SeedPlanAsync("BASIC7", 10m, cycleValue: 0);
        var sub = await SeedSubscriptionAsync(pro, "SUB-DOWN7", DateTime.UtcNow.AddDays(5));

        var preview = await InScopeAsync<ISubscriptionService, Result<SubscriptionChangeDto>>(
            svc => svc.GetPlanChangePreviewAsync(sub.Id, basic.Id));

        preview.Succeeded.ShouldBeTrue();
        preview.Data!.ChangeType.ShouldBe(SubscriptionChangeType.Downgrade);
        preview.Data.ProratedAmount.ShouldBe(-20m);
    }
}
