
namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 订阅计费端到端集成测试：验证 off-session 扣款 → PaymentCompletedEvent → 订阅状态机推进的完整链路
/// （这是审计 P0「订阅实际收款链路」的回归保护）
/// </summary>
public class SubscriptionBillingIntegrationTests : SubscriptionsIntegrationTestBase
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
            var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
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
            var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
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
    /// 立即取消后再恢复：清掉 EndTime / CancelTime，但「已付到何时」必须原样保留 ——
    /// 剩 5 天取消、恢复之后仍然剩 5 天。此前取消把 NextBillingTime 清空、恢复对 null
    /// 一律从现在起算一个新周期且不扣款，于是「取消再恢复」= 白送一个完整周期，可无限重复；
    /// 这条用例原先只断言 NextBillingTime 在未来，恰好把那个行为钉成了预期。
    /// </summary>
    [Fact]
    public async Task ResumeAfterImmediateCancel_KeepsThePaidThroughDate()
    {
        var plan = await SeedPlanAsync(30m);
        var paidThrough = DateTime.UtcNow.AddDays(5);
        var sub = await SeedSubscriptionAsync(plan.Id, "SUB-RESUME", SubscriptionStatus.Active, paidThrough, "pm_test");

        var cancelled = await InScopeAsync<ISubscriptionService, Result>(
            svc => svc.CancelSubscriptionAsync(sub.Id, new Dtos.CancelSubscriptionDto { Immediate = true }));
        cancelled.Succeeded.ShouldBeTrue();

        var afterCancel = await ReloadAsync<Subscription>(sub.Id);
        afterCancel!.Status.ShouldBe(SubscriptionStatus.Cancelled);
        afterCancel.EndTime.ShouldNotBeNull();
        afterCancel.NextBillingTime!.Value.ShouldBe(paidThrough, TimeSpan.FromSeconds(1));

        var resumed = await InScopeAsync<ISubscriptionService, Result>(
            svc => svc.ResumeSubscriptionAsync(sub.Id));
        resumed.Succeeded.ShouldBeTrue();

        var afterResume = await ReloadAsync<Subscription>(sub.Id);
        afterResume!.Status.ShouldBe(SubscriptionStatus.Active);
        afterResume.EndTime.ShouldBeNull();
        afterResume.CancelTime.ShouldBeNull();
        afterResume.NextBillingTime!.Value.ShouldBe(paidThrough, TimeSpan.FromSeconds(1));
        // 恢复不是一次收款
        (await ReloadPaymentByOrderNoAsync("SUB-RESUME")).ShouldBeNull();

        // 恢复后不应被过期扫描重新过期
        await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ExpireOverdueSubscriptionsAsync());
        (await ReloadAsync<Subscription>(sub.Id))!.Status.ShouldBe(SubscriptionStatus.Active);
    }

    /// <summary>
    /// 取消时已付的那一期早就走完了：恢复不能凭空开一个新周期，而是当场按新的一期扣款；
    /// 扣款成功才推进计费时间，失败就落 PastDue 走催款 —— 没有付款事实就没有延长。
    /// </summary>
    [Fact]
    public async Task ResumeAfterImmediateCancel_WhenThePeriodAlreadyElapsed_ChargesInsteadOfGrantingAFreeCycle()
    {
        var plan = await SeedPlanAsync(30m);
        var sub = await SeedSubscriptionAsync(plan.Id, "SUB-RESUME-DUE", SubscriptionStatus.Active,
            DateTime.UtcNow.AddDays(2), "pm_test");

        (await InScopeAsync<ISubscriptionService, Result>(
            svc => svc.CancelSubscriptionAsync(sub.Id, new Dtos.CancelSubscriptionDto { Immediate = true }))).Succeeded.ShouldBeTrue();

        // 取消之后过了一段时间，已付的那一期走完了
        using (var scope = ServiceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
            var entity = ctx.Set<Subscription>().First(s => s.Id == sub.Id);
            entity.NextBillingTime = DateTime.UtcNow.AddDays(-3);
            await ctx.SaveChangesAsync();
        }

        (await InScopeAsync<ISubscriptionService, Result>(svc => svc.ResumeSubscriptionAsync(sub.Id))).Succeeded.ShouldBeTrue();

        var payment = await ReloadPaymentByOrderNoAsync("SUB-RESUME-DUE");
        payment.ShouldNotBeNull();
        payment!.Status.ShouldBe(PaymentStatus.Succeeded);
        payment.PayableAmount.ShouldBe(30m);

        var afterResume = await ReloadAsync<Subscription>(sub.Id);
        afterResume!.Status.ShouldBe(SubscriptionStatus.Active);
        afterResume.NextBillingTime!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddDays(20));
    }

    /// <summary>同上，但没有支付方式：恢复不得把计费时间拨到未来，订阅落 PastDue 等用户绑卡。</summary>
    [Fact]
    public async Task ResumeAfterImmediateCancel_WhenThePeriodElapsedAndNoCardIsOnFile_DoesNotExtend()
    {
        var plan = await SeedPlanAsync(30m);
        var sub = await SeedSubscriptionAsync(plan.Id, "SUB-RESUME-NOPM", SubscriptionStatus.Active,
            DateTime.UtcNow.AddDays(-3), paymentMethodToken: null);
        using (var scope = ServiceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
            var entity = ctx.Set<Subscription>().First(s => s.Id == sub.Id);
            entity.Status = SubscriptionStatus.Cancelled;
            entity.AutoRenew = false;
            entity.EndTime = DateTime.UtcNow.AddDays(-1);
            await ctx.SaveChangesAsync();
        }

        (await InScopeAsync<ISubscriptionService, Result>(svc => svc.ResumeSubscriptionAsync(sub.Id))).Succeeded.ShouldBeTrue();

        var afterResume = await ReloadAsync<Subscription>(sub.Id);
        afterResume!.Status.ShouldBe(SubscriptionStatus.PastDue);
        afterResume.NextBillingTime!.Value.ShouldBeLessThanOrEqualTo(DateTime.UtcNow);
        (await ReloadPaymentByOrderNoAsync("SUB-RESUME-NOPM")).ShouldBeNull();
    }

    /// <summary>反复取消再恢复，已付到的日期一步都不该往后挪。</summary>
    [Fact]
    public async Task CancelThenResumeRepeatedly_NeverExtendsThePaidThroughDate()
    {
        var plan = await SeedPlanAsync(30m);
        var paidThrough = DateTime.UtcNow.AddDays(1);
        var sub = await SeedSubscriptionAsync(plan.Id, "SUB-RESUME-LOOP", SubscriptionStatus.Active, paidThrough, "pm_test");

        for (var i = 0; i < 3; i++)
        {
            (await InScopeAsync<ISubscriptionService, Result>(
                svc => svc.CancelSubscriptionAsync(sub.Id, new Dtos.CancelSubscriptionDto { Immediate = true }))).Succeeded.ShouldBeTrue();
            (await InScopeAsync<ISubscriptionService, Result>(svc => svc.ResumeSubscriptionAsync(sub.Id))).Succeeded.ShouldBeTrue();
        }

        (await ReloadAsync<Subscription>(sub.Id))!.NextBillingTime!.Value.ShouldBe(paidThrough, TimeSpan.FromSeconds(1));
        (await ReloadPaymentByOrderNoAsync("SUB-RESUME-LOOP")).ShouldBeNull();
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
            var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
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
