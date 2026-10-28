namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 暂停冻结的是计费时钟：离开暂停的每一条路径都要把暂停时长还回去，待生效的变更跟着一起顺延。
/// </summary>
/// <remarks>
/// <para>
/// 暂停期间 <c>NextBillingTime</c> 刻意停在暂停前的值，恢复时才加上暂停了多久。此前只有「恢复」这一条路径折算：
/// 暂停中选择「到期取消」直接置 <c>PendingRenewal</c>，<c>NextBillingTime</c> 早已在过去，下一轮过期扫描当场把它过期，
/// 冻结的那段已付时间一天都没还。
/// </para>
/// <para>
/// 待生效变更（典型是降级）的生效日是排定那一刻的 <c>NextBillingTime</c> 快照：暂停期间到期结算照样按它应用，
/// 恢复也不顺延它，用户在已按高价付过、被冻结的那段时间里被换到低计划上。
/// </para>
/// </remarks>
public class SubscriptionPauseAccountingTests : SubscriptionsIntegrationTestBase
{
    private async Task<SubscriptionPlan> SeedPlanAsync(string code, decimal price)
    {
        var plan = new SubscriptionPlan
        {
            PlanCode = $"{code}{Guid.NewGuid():N}"[..12],
            PlanName = code,
            ProductCode = "SUITE",
            Price = price,
            Currency = "USD",
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            IsActive = true
        };
        await SeedAsync(plan);
        return plan;
    }

    /// <summary>
    /// 一条 30 天前暂停的订阅，暂停那一刻还剩 20 天已付时间：冻结的计费时间停在 10 天前。
    /// </summary>
    private async Task<Subscription> SeedPausedAsync(SubscriptionPlan plan, DateTime now)
    {
        var subscription = new Subscription
        {
            SubscriptionNo = $"SUB{Guid.NewGuid():N}",
            UserId = TestHelper.DefaultTestUserId,
            PlanId = plan.Id,
            ProductCode = plan.ProductCode,
            Status = SubscriptionStatus.Paused,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            StartTime = now.AddDays(-40),
            NextBillingTime = now.AddDays(-10),
            PausedAt = now.AddDays(-30),
            PausedUntil = now.AddDays(30),
            OriginalPrice = plan.Price,
            PaidAmount = plan.Price,
            Currency = "USD",
            AutoRenew = true,
            ChannelCode = "Null",
            PaymentMethodToken = "pm_test",
            LastBillingTradeNo = "PAY-SEED-" + Guid.NewGuid().ToString("N")
        };
        await SeedAsync(subscription);
        return subscription;
    }

    /// <summary>
    /// ★ 暂停中选择「到期取消」：先把 30 天暂停折回计费时钟（还剩 20 天），清掉暂停标记，到那时才过期。
    /// </summary>
    [Fact]
    public async Task CancellingAtPeriodEndWhilePaused_KeepsTheFrozenDays()
    {
        var now = DateTime.UtcNow;
        var plan = await SeedPlanAsync("PRO", 30m);
        var subscription = await SeedPausedAsync(plan, now);

        var cancelled = await InScopeAsync<ISubscriptionService, Result>(svc => svc.CancelSubscriptionAsync(
            subscription.Id, new CancelSubscriptionDto { Reason = "moving on", Immediate = false }));
        cancelled.Succeeded.ShouldBeTrue(cancelled.Message);

        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.PendingRenewal);
        reloaded.NextBillingTime!.Value.ShouldBe(now.AddDays(20), TimeSpan.FromMinutes(1));
        reloaded.PausedAt.ShouldBeNull();
        reloaded.PausedUntil.ShouldBeNull();

        (await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ExpireOverdueSubscriptionsAsync())).Data.ShouldBe(0);
        (await ReloadAsync<Subscription>(subscription.Id))!.Status.ShouldBe(SubscriptionStatus.PendingRenewal);
    }

    /// <summary>
    /// 暂停中立即取消同样先折算：之后的时间按普通取消计（恢复时已付到的那一刻没过就不扣款），而不是被一直当成暂停。
    /// </summary>
    [Fact]
    public async Task CancellingImmediatelyWhilePaused_FoldsThePauseFirst()
    {
        var now = DateTime.UtcNow;
        var plan = await SeedPlanAsync("PRO", 30m);
        var subscription = await SeedPausedAsync(plan, now);

        (await InScopeAsync<ISubscriptionService, Result>(svc => svc.CancelSubscriptionAsync(
            subscription.Id, new CancelSubscriptionDto { Reason = "moving on", Immediate = true }))).Succeeded.ShouldBeTrue();

        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.Cancelled);
        reloaded.NextBillingTime!.Value.ShouldBe(now.AddDays(20), TimeSpan.FromMinutes(1));
        reloaded.PausedAt.ShouldBeNull();
    }

    /// <summary>
    /// ★ 暂停前排定的降级：暂停期间不被到期结算应用；恢复后生效日与续费日一起顺延，在那之前用户仍在原计划上。
    /// </summary>
    [Fact]
    public async Task ADowngradeScheduledBeforeAPause_WaitsForTheShiftedRenewal()
    {
        var now = DateTime.UtcNow;
        var pro = await SeedPlanAsync("PRO", 30m);
        var basic = await SeedPlanAsync("BASIC", 10m);
        var subscription = await SeedPausedAsync(pro, now);
        var downgrade = new SubscriptionChange
        {
            SubscriptionId = subscription.Id,
            FromPlanId = pro.Id,
            ToPlanId = basic.Id,
            ChangeType = SubscriptionChangeType.Downgrade,
            ProratedAmount = 0m,
            // 排定那一刻的周期边界 = 暂停前冻结的计费时间
            EffectiveDate = subscription.NextBillingTime!.Value,
            Status = SubscriptionChangeStatus.Pending
        };
        await SeedAsync(downgrade);

        // 暂停期间：到期结算不碰它
        (await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ApplyDuePlanChangesAsync())).Data.ShouldBe(0);
        (await ReloadAsync<SubscriptionChange>(downgrade.Id))!.Status.ShouldBe(SubscriptionChangeStatus.Pending);
        (await ReloadAsync<Subscription>(subscription.Id))!.PlanId.ShouldBe(pro.Id);

        // 恢复：续费日顺延 30 天，生效日跟着顺延
        (await InScopeAsync<ISubscriptionService, Result>(svc => svc.ResumeSubscriptionAsync(subscription.Id))).Succeeded.ShouldBeTrue();
        var resumed = await ReloadAsync<Subscription>(subscription.Id);
        resumed!.Status.ShouldBe(SubscriptionStatus.Active);
        resumed.NextBillingTime!.Value.ShouldBe(now.AddDays(20), TimeSpan.FromMinutes(1));
        var shifted = await ReloadAsync<SubscriptionChange>(downgrade.Id);
        shifted!.Status.ShouldBe(SubscriptionChangeStatus.Pending);
        shifted.EffectiveDate.ShouldBe(resumed.NextBillingTime.Value, TimeSpan.FromSeconds(1));

        // 顺延后的生效日之前，到期结算仍不应用它
        (await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ApplyDuePlanChangesAsync())).Data.ShouldBe(0);
        (await ReloadAsync<Subscription>(subscription.Id))!.PlanId.ShouldBe(pro.Id);
    }
}
