using Microsoft.Extensions.DependencyInjection;
using Tnzi.Domain.Repositories;
using Tnzi.Payment.Entities;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Services;
using Tnzi.Results;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 订阅计费端到端集成测试：验证 off-session 扣款 → PaymentCompletedEvent → 订阅状态机推进的完整链路
/// （这是审计 P0「订阅实际收款链路」的回归保护）
/// </summary>
public class SubscriptionBillingIntegrationTests : PaymentIntegrationTestBase
{
    private async Task<SubscriptionPlan> SeedPlanAsync(decimal price = 30m)
    {
        var plan = new SubscriptionPlan
        {
            PlanName = "Pro",
            Price = price,
            Currency = "USD",
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            IsActive = true
        };
        await SeedAsync(plan);
        return plan;
    }

    private async Task<Subscription> SeedSubscriptionAsync(
        Guid planId, string subscriptionNo, SubscriptionStatus status,
        DateTime? nextBilling, string? paymentMethodToken, DateTime? trialEnd = null)
    {
        var subscription = new Subscription
        {
            SubscriptionNo = subscriptionNo,
            UserId = Guid.NewGuid(),
            PlanId = planId,
            Status = status,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            StartTime = DateTime.UtcNow.AddMonths(-1),
            NextBillingTime = nextBilling,
            TrialEndTime = trialEnd,
            OriginalPrice = 30m,
            Currency = "USD",
            AutoRenew = true,
            ChannelCode = "Null",
            PaymentMethodToken = paymentMethodToken,
            ProviderCustomerId = "cus_test"
        };
        await SeedAsync(subscription);
        return subscription;
    }

    private Task<Result<int>> RenewAsync() =>
        InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.RenewExpiredSubscriptionsAsync());

    private Task<Result<int>> ConvertTrialsAsync() =>
        InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ConvertDueTrialsAsync());

    [Fact]
    public async Task DueRenewal_WithSavedPaymentMethod_ChargesAndAdvancesPeriod()
    {
        // Arrange：到期、已存支付方式的自动续费订阅
        var plan = await SeedPlanAsync(30m);
        var sub = await SeedSubscriptionAsync(plan.Id, "SUB-RENEW1", SubscriptionStatus.Active,
            DateTime.UtcNow.AddDays(-1), "pm_test");

        // Act：后台续费扫描
        var result = await RenewAsync();

        // Assert：扣款成功 → 周期推进 + 仍 Active + 落地一笔成功支付
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(1);

        var reloaded = await ReloadAsync<Subscription>(sub.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.Active);
        reloaded.NextBillingTime!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddDays(20));
        reloaded.BillingLockedUntil.ShouldBeNull();

        var payment = await ReloadPaymentByOrderNoAsync("SUB-RENEW1");
        payment.ShouldNotBeNull();
        payment!.Status.ShouldBe(PaymentStatus.Succeeded);
        payment.BusinessType.ShouldBe(BusinessType.Subscription);
    }

    [Fact]
    public async Task DueRenewal_WithoutPaymentMethod_DowngradesToPastDue()
    {
        // Arrange：到期但无已存支付方式
        var plan = await SeedPlanAsync(30m);
        var sub = await SeedSubscriptionAsync(plan.Id, "SUB-NOPM", SubscriptionStatus.Active,
            DateTime.UtcNow.AddDays(-1), paymentMethodToken: null);

        // Act
        var result = await RenewAsync();

        // Assert：无法 off-session 扣款 → PastDue + 累计重试，周期不推进
        result.Succeeded.ShouldBeTrue();

        var reloaded = await ReloadAsync<Subscription>(sub.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.PastDue);
        reloaded.RenewalRetryCount.ShouldBe(1);
        reloaded.PastDueSince.ShouldNotBeNull();
        reloaded.NextBillingTime!.Value.ShouldBeLessThan(DateTime.UtcNow);
    }

    [Fact]
    public async Task DueTrial_WithSavedPaymentMethod_ConvertsToActive()
    {
        // Arrange：试用到期 + 已存支付方式
        var plan = await SeedPlanAsync(20m);
        var sub = await SeedSubscriptionAsync(plan.Id, "SUB-TRIAL1", SubscriptionStatus.Trial,
            nextBilling: DateTime.UtcNow.AddDays(-1), paymentMethodToken: "pm_test",
            trialEnd: DateTime.UtcNow.AddDays(-1));

        // Act
        var result = await ConvertTrialsAsync();

        // Assert：转正扣款成功 → Active + 记录转正时间
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(1);

        var reloaded = await ReloadAsync<Subscription>(sub.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.Active);
        reloaded.TrialConvertedTime.ShouldNotBeNull();

        var payment = await ReloadPaymentByOrderNoAsync("SUB-TRIAL1");
        payment!.Status.ShouldBe(PaymentStatus.Succeeded);
    }

    /// <summary>
    /// 暂停期间不参与续费扫描，到期后由后台自动恢复。
    /// Paused 此前是个死状态：只有 Resume 判断它，却没有任何入口能进入。
    /// </summary>
    [Fact]
    public async Task PausedSubscription_IsSkippedByRenewalAndAutoResumesWhenDue()
    {
        var plan = await SeedPlanAsync(30m);
        var sub = await SeedSubscriptionAsync(plan.Id, "SUB-PAUSE", SubscriptionStatus.Active,
            DateTime.UtcNow.AddDays(10), "pm_test");

        var paused = await InScopeAsync<ISubscriptionService, Result>(
            svc => svc.PauseSubscriptionAsync(sub.Id, new Dtos.PauseSubscriptionDto
            {
                ResumeAt = DateTime.UtcNow.AddDays(-1),
                Reason = "Vacation"
            }));

        // 恢复时间必须在未来，过去的时间应被拒绝
        paused.Succeeded.ShouldBeFalse();

        var pausedOk = await InScopeAsync<ISubscriptionService, Result>(
            svc => svc.PauseSubscriptionAsync(sub.Id, new Dtos.PauseSubscriptionDto
            {
                ResumeAt = DateTime.UtcNow.AddDays(7)
            }));
        pausedOk.Succeeded.ShouldBeTrue();

        var afterPause = await ReloadAsync<Subscription>(sub.Id);
        afterPause!.Status.ShouldBe(SubscriptionStatus.Paused);
        afterPause.PausedAt.ShouldNotBeNull();

        // 暂停期内续费扫描不该碰它
        (await RenewAsync()).Data.ShouldBe(0);

        // 把恢复时间拨到过去，模拟到期
        using (var scope = ServiceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PaymentTestDbContext>();
            var entity = ctx.Set<Subscription>().First(s => s.Id == sub.Id);
            entity.PausedUntil = DateTime.UtcNow.AddMinutes(-1);
            await ctx.SaveChangesAsync();
        }

        var resumed = await InScopeAsync<ISubscriptionService, Result<int>>(
            svc => svc.ResumeDuePausedSubscriptionsAsync());
        resumed.Data.ShouldBe(1);

        var afterResume = await ReloadAsync<Subscription>(sub.Id);
        afterResume!.Status.ShouldBe(SubscriptionStatus.Active);
        afterResume.PausedUntil.ShouldBeNull();
        afterResume.PausedAt.ShouldBeNull();
        afterResume.NextBillingTime!.Value.ShouldBeGreaterThan(DateTime.UtcNow);
    }

    /// <summary>
    /// 恢复时必须把暂停时长原样加回计费时间——剩余周期分毫不差地还给用户。
    /// </summary>
    /// <remarks>
    /// 反面行为（恢复时重算一个完整周期）是资损：在扣款日前一天暂停、次日恢复，
    /// 就能把"还剩 1 天"变成"还剩一整个周期"，反复操作即可无限白嫖。
    /// </remarks>
    [Fact]
    public async Task Resume_PreservesRemainingPeriod_InsteadOfGrantingAFreeCycle()
    {
        var plan = await SeedPlanAsync(30m);
        // 距离扣款只剩 1 天
        var dueIn1Day = DateTime.UtcNow.AddDays(1);
        var sub = await SeedSubscriptionAsync(plan.Id, "SUB-PAUSE-FAIR", SubscriptionStatus.Active,
            dueIn1Day, "pm_test");

        (await InScopeAsync<ISubscriptionService, Result>(
            svc => svc.PauseSubscriptionAsync(sub.Id, new Dtos.PauseSubscriptionDto()))).Succeeded.ShouldBeTrue();

        // 模拟暂停了 10 天
        using (var scope = ServiceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PaymentTestDbContext>();
            var entity = ctx.Set<Subscription>().First(s => s.Id == sub.Id);
            entity.PausedAt = DateTime.UtcNow.AddDays(-10);
            await ctx.SaveChangesAsync();
        }

        (await InScopeAsync<ISubscriptionService, Result>(
            svc => svc.ResumeSubscriptionAsync(sub.Id))).Succeeded.ShouldBeTrue();

        var afterResume = await ReloadAsync<Subscription>(sub.Id);
        // 剩余 1 天 + 暂停 10 天 ≈ 11 天后扣款；而不是被重算成一整个月
        var daysUntilBilling = (afterResume!.NextBillingTime!.Value - DateTime.UtcNow).TotalDays;
        daysUntilBilling.ShouldBeInRange(10.5, 11.5);
    }

    /// <summary>
    /// 立即取消后再恢复：必须清掉 EndTime 并把计费时间拨回未来，
    /// 否则下一轮过期扫描会立刻把刚恢复的订阅再次过期掉。
    /// </summary>
    [Fact]
    public async Task ResumeAfterImmediateCancel_ClearsEndTimeAndMovesBillingForward()
    {
        var plan = await SeedPlanAsync(30m);
        var sub = await SeedSubscriptionAsync(plan.Id, "SUB-RESUME", SubscriptionStatus.Active,
            DateTime.UtcNow.AddDays(5), "pm_test");

        var cancelled = await InScopeAsync<ISubscriptionService, Result>(
            svc => svc.CancelSubscriptionAsync(sub.Id, new Dtos.CancelSubscriptionDto { Immediate = true }));
        cancelled.Succeeded.ShouldBeTrue();

        var afterCancel = await ReloadAsync<Subscription>(sub.Id);
        afterCancel!.Status.ShouldBe(SubscriptionStatus.Cancelled);
        afterCancel.EndTime.ShouldNotBeNull();

        var resumed = await InScopeAsync<ISubscriptionService, Result>(
            svc => svc.ResumeSubscriptionAsync(sub.Id));
        resumed.Succeeded.ShouldBeTrue();

        var afterResume = await ReloadAsync<Subscription>(sub.Id);
        afterResume!.Status.ShouldBe(SubscriptionStatus.Active);
        afterResume.EndTime.ShouldBeNull();
        afterResume.CancelTime.ShouldBeNull();
        afterResume.NextBillingTime!.Value.ShouldBeGreaterThan(DateTime.UtcNow);

        // 恢复后不应被过期扫描重新过期
        await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ExpireOverdueSubscriptionsAsync());
        (await ReloadAsync<Subscription>(sub.Id))!.Status.ShouldBe(SubscriptionStatus.Active);
    }

    /// <summary>
    /// 逾期欠费的订阅换卡后应立即重试扣款，而不是干等下一轮扫描
    /// </summary>
    [Fact]
    public async Task RetryBilling_OnPastDueSubscription_RecoversToActive()
    {
        var plan = await SeedPlanAsync(30m);
        var sub = await SeedSubscriptionAsync(plan.Id, "SUB-RETRY", SubscriptionStatus.Active,
            DateTime.UtcNow.AddDays(-1), paymentMethodToken: null);

        await RenewAsync();
        (await ReloadAsync<Subscription>(sub.Id))!.Status.ShouldBe(SubscriptionStatus.PastDue);

        // 补上支付方式后主动重试
        using (var scope = ServiceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PaymentTestDbContext>();
            var entity = ctx.Set<Subscription>().First(s => s.Id == sub.Id);
            entity.PaymentMethodToken = "pm_recovered";
            entity.BillingLockedUntil = null;
            await ctx.SaveChangesAsync();
        }

        var retried = await InScopeAsync<ISubscriptionService, Result>(
            svc => svc.RetryBillingAsync(sub.Id));
        retried.Succeeded.ShouldBeTrue();

        var reloaded = await ReloadAsync<Subscription>(sub.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.Active);
        reloaded.RenewalRetryCount.ShouldBe(0);
        reloaded.PastDueSince.ShouldBeNull();
    }

    /// <summary>
    /// 非逾期状态不允许触发重试扣款（避免被当成"随时扣一笔"的接口）
    /// </summary>
    [Fact]
    public async Task RetryBilling_OnActiveSubscription_IsRejected()
    {
        var plan = await SeedPlanAsync(30m);
        var sub = await SeedSubscriptionAsync(plan.Id, "SUB-RETRY-ACTIVE", SubscriptionStatus.Active,
            DateTime.UtcNow.AddDays(10), "pm_test");

        var retried = await InScopeAsync<ISubscriptionService, Result>(
            svc => svc.RetryBillingAsync(sub.Id));

        retried.Succeeded.ShouldBeFalse();
        retried.Message.ShouldBe(ErrorCodes.SubscriptionCannotRetryBilling);
    }

    private async Task<PaymentEntity?> ReloadPaymentByOrderNoAsync(string businessOrderNo)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<PaymentEntity, Guid>>();
        return await repo.FirstOrDefaultAsync(p => p.BusinessOrderNo == businessOrderNo);
    }
}
