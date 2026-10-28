namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 用户关掉的自动续费不能被系统悄悄打开，也不能因为一笔付款到账而失去它的效果。
/// </summary>
/// <remarks>
/// <para>
/// 恢复（含暂停到期的后台自动恢复）此前一律写 <c>AutoRenew = true</c>：试用期里关掉自动续费再暂停，
/// 暂停到期后系统替用户把它打开，试用截止时照常转正扣款；正式订阅暂停期间关掉自动续费，
/// 自动恢复后下一期照扣。只有撤销一次取消（从 Cancelled / PendingRenewal 恢复）才是「重新要续费」。
/// </para>
/// <para>
/// 同形的另一处：付款回流一律把订阅置成 <c>Active</c>，不看 <c>AutoRenew</c>。而续费扫描只取
/// <c>AutoRenew</c> 为真的订阅、过期扫描只取 <c>PendingRenewal</c>，一条 <c>Active</c> 且关了自动续费的订阅
/// 既不再扣款也永不过期，付一期用到永远。
/// </para>
/// </remarks>
public class SubscriptionAutoRenewIntentTests : SubscriptionsIntegrationTestBase
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
            IsActive = true,
            AllowTrial = true,
            TrialDays = 14
        };
        await SeedAsync(plan);
        return plan;
    }

    private async Task<Subscription> SeedActiveAsync(SubscriptionPlan plan, string subscriptionNo)
    {
        var subscription = new Subscription
        {
            SubscriptionNo = subscriptionNo,
            UserId = TestHelper.DefaultTestUserId,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Active,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            StartTime = DateTime.UtcNow.AddDays(-20),
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
        await SeedAsync(subscription);
        return subscription;
    }

    private async Task<Subscription> SeedTrialAsync(SubscriptionPlan plan, string subscriptionNo)
    {
        var trialEnd = DateTime.UtcNow.AddDays(5);
        var subscription = new Subscription
        {
            SubscriptionNo = subscriptionNo,
            UserId = TestHelper.DefaultTestUserId,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Trial,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            StartTime = DateTime.UtcNow.AddDays(-9),
            TrialStartTime = DateTime.UtcNow.AddDays(-9),
            TrialEndTime = trialEnd,
            NextBillingTime = trialEnd,
            OriginalPrice = 30m,
            Currency = "USD",
            AutoRenew = true,
            ChannelCode = "Null",
            PaymentMethodToken = "pm_test",
            ProviderCustomerId = "cus_test"
        };
        await SeedAsync(subscription);
        return subscription;
    }

    private Task<Result> SetAutoRenewAsync(Guid id, bool autoRenew) =>
        InScopeAsync<ISubscriptionService, Result>(svc => svc.UpdateAutoRenewAsync(id, autoRenew));

    private Task<Result> PauseAsync(Guid id) =>
        InScopeAsync<ISubscriptionService, Result>(svc => svc.PauseSubscriptionAsync(id, new PauseSubscriptionDto
        {
            ResumeAt = DateTime.UtcNow.AddDays(7)
        }));

    private async Task MakePauseDueAsync(Guid id)
    {
        using var scope = ServiceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
        var entity = ctx.Set<Subscription>().First(s => s.Id == id);
        entity.PausedUntil = DateTime.UtcNow.AddMinutes(-1);
        await ctx.SaveChangesAsync();
    }

    private async Task UpdateAsync(Guid id, Action<Subscription> change)
    {
        using var scope = ServiceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
        change(ctx.Set<Subscription>().First(s => s.Id == id));
        await ctx.SaveChangesAsync();
    }

    private async Task<int> CountPaymentsAsync(string businessOrderNo)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<PaymentEntity, Guid>>();
        return await repo.AsQueryable().CountAsync(p => p.BusinessOrderNo == businessOrderNo);
    }

    [Fact]
    public async Task AutoResumeAfterAPause_KeepsAutoRenewOffWhenTheUserTurnedItOff()
    {
        var plan = await SeedPlanAsync();
        var subscription = await SeedActiveAsync(plan, "SUB-AR-1");
        (await PauseAsync(subscription.Id)).Succeeded.ShouldBeTrue();
        (await SetAutoRenewAsync(subscription.Id, false)).Succeeded.ShouldBeTrue();
        await MakePauseDueAsync(subscription.Id);

        (await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ResumeDuePausedSubscriptionsAsync())).Data.ShouldBe(1);

        var resumed = await ReloadAsync<Subscription>(subscription.Id);
        resumed!.AutoRenew.ShouldBeFalse();
        resumed.Status.ShouldBe(SubscriptionStatus.PendingRenewal);

        // 已付的那一期走完：到期过期，而不是扣款
        await UpdateAsync(subscription.Id, s => s.NextBillingTime = DateTime.UtcNow.AddMinutes(-1));
        await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.RenewExpiredSubscriptionsAsync());
        await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ExpireOverdueSubscriptionsAsync());

        (await ReloadAsync<Subscription>(subscription.Id))!.Status.ShouldBe(SubscriptionStatus.Expired);
        (await CountPaymentsAsync("SUB-AR-1")).ShouldBe(0);
    }

    [Fact]
    public async Task AutoResumeOfAPausedTrial_DoesNotConvertATrialWhoseAutoRenewWasTurnedOff()
    {
        var plan = await SeedPlanAsync();
        var subscription = await SeedTrialAsync(plan, "SUB-AR-2");
        (await SetAutoRenewAsync(subscription.Id, false)).Succeeded.ShouldBeTrue();
        (await PauseAsync(subscription.Id)).Succeeded.ShouldBeTrue();
        await MakePauseDueAsync(subscription.Id);

        await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ResumeDuePausedSubscriptionsAsync());

        var resumed = await ReloadAsync<Subscription>(subscription.Id);
        resumed!.Status.ShouldBe(SubscriptionStatus.Trial);
        resumed.AutoRenew.ShouldBeFalse();

        // 试用截止：不转正扣款，到期过期
        await UpdateAsync(subscription.Id, s => s.TrialEndTime = DateTime.UtcNow.AddMinutes(-1));
        await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ConvertDueTrialsAsync());

        (await ReloadAsync<Subscription>(subscription.Id))!.Status.ShouldBe(SubscriptionStatus.Expired);
        (await CountPaymentsAsync("SUB-AR-2")).ShouldBe(0);
    }

    /// <summary>用户自己点「恢复」离开暂停，同样不替他改自动续费开关。</summary>
    [Fact]
    public async Task ResumingFromAPause_LeavesTheAutoRenewChoiceAlone()
    {
        var plan = await SeedPlanAsync();
        var subscription = await SeedActiveAsync(plan, "SUB-AR-3");
        (await PauseAsync(subscription.Id)).Succeeded.ShouldBeTrue();
        (await SetAutoRenewAsync(subscription.Id, false)).Succeeded.ShouldBeTrue();

        (await InScopeAsync<ISubscriptionService, Result>(svc => svc.ResumeSubscriptionAsync(subscription.Id))).Succeeded.ShouldBeTrue();

        var resumed = await ReloadAsync<Subscription>(subscription.Id);
        resumed!.AutoRenew.ShouldBeFalse();
        resumed.Status.ShouldBe(SubscriptionStatus.PendingRenewal);
    }

    /// <summary>离开暂停时已付的那一期已经走完、而自动续费关着：不扣款，交给过期扫描。</summary>
    [Fact]
    public async Task ResumingWithAnElapsedPeriodAndAutoRenewOff_DoesNotCharge()
    {
        var plan = await SeedPlanAsync();
        var subscription = await SeedActiveAsync(plan, "SUB-AR-6");
        await UpdateAsync(subscription.Id, s =>
        {
            s.Status = SubscriptionStatus.Paused;
            s.AutoRenew = false;
            s.PausedAt = DateTime.UtcNow.AddHours(-1);
            s.PausedUntil = DateTime.UtcNow.AddMinutes(-1);
            s.NextBillingTime = DateTime.UtcNow.AddDays(-2);
        });

        await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ResumeDuePausedSubscriptionsAsync());

        (await CountPaymentsAsync("SUB-AR-6")).ShouldBe(0);
        (await ReloadAsync<Subscription>(subscription.Id))!.Status.ShouldBe(SubscriptionStatus.PendingRenewal);

        await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ExpireOverdueSubscriptionsAsync());
        (await ReloadAsync<Subscription>(subscription.Id))!.Status.ShouldBe(SubscriptionStatus.Expired);
    }

    /// <summary>撤销一次取消就是「重新要续费」：仍然打开自动续费（回归）。</summary>
    [Fact]
    public async Task ResumingAfterACancellation_TurnsAutoRenewBackOn()
    {
        var plan = await SeedPlanAsync();
        var subscription = await SeedActiveAsync(plan, "SUB-AR-4");
        (await InScopeAsync<ISubscriptionService, Result>(svc => svc.CancelSubscriptionAsync(
            subscription.Id, new CancelSubscriptionDto { Immediate = false }))).Succeeded.ShouldBeTrue();

        (await InScopeAsync<ISubscriptionService, Result>(svc => svc.ResumeSubscriptionAsync(subscription.Id))).Succeeded.ShouldBeTrue();

        var resumed = await ReloadAsync<Subscription>(subscription.Id);
        resumed!.AutoRenew.ShouldBeTrue();
        resumed.Status.ShouldBe(SubscriptionStatus.Active);
    }

    /// <summary>暂停时自动续费本来就开着：自动恢复后照常 Active（回归）。</summary>
    [Fact]
    public async Task AutoResumeWithAutoRenewOn_IsUnchanged()
    {
        var plan = await SeedPlanAsync();
        var subscription = await SeedActiveAsync(plan, "SUB-AR-5");
        (await PauseAsync(subscription.Id)).Succeeded.ShouldBeTrue();
        await MakePauseDueAsync(subscription.Id);

        await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ResumeDuePausedSubscriptionsAsync());

        var resumed = await ReloadAsync<Subscription>(subscription.Id);
        resumed!.Status.ShouldBe(SubscriptionStatus.Active);
        resumed.AutoRenew.ShouldBeTrue();
    }

    /// <summary>
    /// 开通后、付首期之前关掉自动续费：首期到账后订阅只覆盖这一期，到期过期，
    /// 而不是成为一条既不续费也不过期的 Active。
    /// </summary>
    [Fact]
    public async Task AFirstPaymentWithAutoRenewOff_LeavesASubscriptionThatEndsWithThePeriod()
    {
        var plan = await SeedPlanAsync();
        var created = await InScopeAsync<ISubscriptionService, Result<SubscriptionCreateResultDto>>(
            svc => svc.CreateSubscriptionAsync(new CreateSubscriptionDto { PlanId = plan.Id, ChannelCode = "Null" }));
        created.Succeeded.ShouldBeTrue(created.Message);
        var subscriptionId = created.Data!.Subscription.Id;
        (await SetAutoRenewAsync(subscriptionId, false)).Succeeded.ShouldBeTrue();

        (await InScopeAsync<IPaymentService, Result>(svc => svc.HandleCallbackAsync(new PaymentCallbackDto
        {
            ChannelCode = "Null",
            Parameters = new Dictionary<string, string>
            {
                ["trade_no"] = created.Data.Payment!.TradeNo,
                ["amount"] = "30",
                ["event_id"] = Guid.NewGuid().ToString("N")
            }
        }))).Succeeded.ShouldBeTrue();

        var paid = await ReloadAsync<Subscription>(subscriptionId);
        paid!.Status.ShouldBe(SubscriptionStatus.PendingRenewal);
        paid.NextBillingTime.ShouldNotBeNull();

        await UpdateAsync(subscriptionId, s => s.NextBillingTime = DateTime.UtcNow.AddMinutes(-1));
        await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ExpireOverdueSubscriptionsAsync());
        (await ReloadAsync<Subscription>(subscriptionId))!.Status.ShouldBe(SubscriptionStatus.Expired);
    }

    /// <summary>
    /// 扣款在途时用户关掉了自动续费：那笔续费到账覆盖的是新的一期，但订阅不能因此回到「会继续续费」的 Active。
    /// </summary>
    [Fact]
    public async Task ARenewalCompletingAfterAutoRenewWasTurnedOff_DoesNotLeaveAnActiveThatNeverEnds()
    {
        var plan = await SeedPlanAsync();
        var subscription = await SeedActiveAsync(plan, "SUB-AR-7");
        await UpdateAsync(subscription.Id, s =>
        {
            s.Status = SubscriptionStatus.PendingRenewal;
            s.AutoRenew = false;
        });

        var applied = await InScopeAsync<ISubscriptionService, Result>(svc => svc.ApplyPaymentCompletedAsync(new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.Renewal,
            SubscriptionId = subscription.Id,
            SubscriptionNo = subscription.SubscriptionNo,
            PayerUserId = subscription.UserId,
            PaymentTradeNo = "PAY-AR-7",
            Amount = 30m,
            Currency = "USD"
        }));

        applied.Succeeded.ShouldBeTrue();
        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.PendingRenewal);
        reloaded.NextBillingTime!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddDays(30));
    }
}
