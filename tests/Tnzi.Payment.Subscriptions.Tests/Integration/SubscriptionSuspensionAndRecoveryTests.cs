using Tnzi.EventBus;

namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 宽限期满仍未付清 → 暂停服务（<c>GracePeriodEndAction = Suspend</c>）→ 管理者当场补缴（有人在场、可做 3-D Secure）→ 原样恢复。
/// </summary>
/// <remarks>
/// 三条要守住的事：暂停服务不是终态（照常占着这个产品的名额，补缴恢复的是同一条订阅）；
/// 恢复时新的一期从付款那一刻起算（暂停期间没有提供服务，不追收）；补缴单没付成不是一次扣款失败（不计重试、不催款）。
/// </remarks>
public class SubscriptionSuspensionAndRecoveryTests : SubscriptionsIntegrationTestBase
{
    private static readonly Guid Owner = TestHelper.DefaultTestUserId;

    private readonly EventRecorder _events = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<SubscriptionOptions>(o => o.GracePeriodEndAction = GracePeriodEndAction.Suspend);
        services.AddSingleton(_events);
        services.AddScoped<IEventHandler<SubscriptionSuspendedEvent>, RecordingHandler<SubscriptionSuspendedEvent>>();
        services.AddScoped<IEventHandler<SubscriptionRecoveredEvent>, RecordingHandler<SubscriptionRecoveredEvent>>();
        services.AddScoped<IEventHandler<SubscriptionPastDueEvent>, RecordingHandler<SubscriptionPastDueEvent>>();
        services.AddScoped<IEventHandler<SubscriptionActivatedEvent>, RecordingHandler<SubscriptionActivatedEvent>>();
        services.AddScoped<IEventHandler<BillingAccountStandingChangedEvent>, RecordingHandler<BillingAccountStandingChangedEvent>>();
    }

    private async Task<(SubscriptionPlan Plan, Subscription Subscription)> SeedAsync(
        SubscriptionStatus status, DateTime? pastDueSince, string? card = "pm_old_card", Guid? userId = null, string? productCode = "crm")
    {
        var plan = new SubscriptionPlan
        {
            PlanCode = $"P{Guid.NewGuid():N}"[..12],
            PlanName = "Firm",
            ProductCode = productCode,
            Price = 300m,
            Currency = "CAD",
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            IsActive = true
        };
        await SeedAsync(plan);

        var subscription = new Subscription
        {
            SubscriptionNo = $"SUB{Guid.NewGuid():N}"[..16],
            UserId = userId ?? Owner,
            PlanId = plan.Id,
            ProductCode = productCode,
            Status = status,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            StartTime = DateTime.UtcNow.AddMonths(-2),
            NextBillingTime = DateTime.UtcNow.AddDays(-5),
            OriginalPrice = 300m,
            Currency = "CAD",
            AutoRenew = true,
            ChannelCode = "Null",
            PaymentMethodToken = card,
            PastDueSince = pastDueSince,
            RenewalRetryCount = pastDueSince == null ? 0 : 3,
            SuspendedAt = status == SubscriptionStatus.Suspended ? DateTime.UtcNow.AddDays(-1) : null,
            LastBillingTradeNo = "PAY-SEED"
        };
        await SeedAsync(subscription);
        return (plan, subscription);
    }

    private Task<Result<int>> ExpireScanAsync() =>
        InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ExpireOverdueSubscriptionsAsync());

    private Task<Result<SubscriptionPayNowResultDto>> PayNowAsync(Guid subscriptionId, Guid? owner = null) =>
        InScopeAsync<ISubscriptionService, Result<SubscriptionPayNowResultDto>>(svc => svc.PayNowAsync(subscriptionId, owner ?? Owner));

    private Task<Result> CallbackAsync(string tradeNo, string amount, string? status = null, string? savedToken = null)
    {
        var parameters = new Dictionary<string, string>
        {
            ["trade_no"] = tradeNo,
            ["amount"] = amount,
            ["currency"] = "CAD",
            ["event_id"] = $"evt_{Guid.NewGuid():N}"
        };
        if (status != null)
            parameters["status"] = status;
        if (savedToken != null)
            parameters["saved_token"] = savedToken;

        return InScopeAsync<IPaymentService, Result>(svc => svc.HandleCallbackAsync(new PaymentCallbackDto
        {
            ChannelCode = "Null",
            Parameters = parameters
        }));
    }

    [Fact]
    public async Task UnpaidAfterTheGracePeriod_IsSuspendedNotExpired()
    {
        var (_, subscription) = await SeedAsync(SubscriptionStatus.PastDue, DateTime.UtcNow.AddDays(-4));

        await ExpireScanAsync();

        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.Suspended);
        reloaded.SuspendedAt.ShouldNotBeNull();
        reloaded.EndTime.ShouldBeNull("a suspended subscription has not ended");
        reloaded.NextBillingTime.ShouldNotBeNull("the unpaid period is still owed");

        _events.Of<SubscriptionSuspendedEvent>().ShouldHaveSingleItem().SubscriptionId.ShouldBe(subscription.Id);
        var standing = _events.Of<BillingAccountStandingChangedEvent>().ShouldHaveSingleItem();
        standing.PreviousStanding.ShouldBe(BillingStanding.Warning);
        standing.CurrentStanding.ShouldBe(BillingStanding.Suspended);
        standing.BillingAccountType.ShouldBe(BillingAccountTypes.User);
        standing.BillingAccountId.ShouldBe(Owner.ToString());

        (await InScopeAsync<ISubscriptionService, BillingStanding>(svc => svc.GetStandingAsync(BillingAccountRef.ForUser(Owner), "crm")))
            .ShouldBe(BillingStanding.Suspended);
    }

    [Fact]
    public async Task ASuspendedSubscription_StillHoldsItsProductSlot()
    {
        var (plan, _) = await SeedAsync(SubscriptionStatus.Suspended, DateTime.UtcNow.AddDays(-4));

        var again = await InScopeAsync<ISubscriptionService, Result<SubscriptionCreateResultDto>>(svc => svc.CreateSubscriptionAsync(new CreateSubscriptionDto
        {
            PlanId = plan.Id,
            ChannelCode = "Null"
        }));

        again.Succeeded.ShouldBeFalse("paying restores the suspended subscription; a second one would double-bill the account");
        again.Message.ShouldBe(ErrorCodes.SubscriptionProductAlreadySubscribed);
    }

    [Fact]
    public async Task PayNow_ReturnsACheckout_AndTheSamePaymentWhenAskedAgain()
    {
        var (_, subscription) = await SeedAsync(SubscriptionStatus.Suspended, DateTime.UtcNow.AddDays(-4));

        var first = await PayNowAsync(subscription.Id);
        first.Succeeded.ShouldBeTrue(first.Message);
        first.Data!.TradeNo.ShouldNotBeNullOrWhiteSpace();
        first.Data.PayParams.ShouldNotBeNullOrWhiteSpace("the browser needs the client secret to confirm (and run 3-D Secure)");
        first.Data.Amount.ShouldBe(300m);
        first.Data.Currency.ShouldBe("CAD");

        var second = await PayNowAsync(subscription.Id);
        second.Succeeded.ShouldBeTrue(second.Message);
        second.Data!.TradeNo.ShouldBe(first.Data.TradeNo, "two payable recovery payments could both be paid");

        (await ReloadAsync<Subscription>(subscription.Id))!.BillingLockedUntil.ShouldBeNull("the lock is released once the checkout exists");
    }

    [Fact]
    public async Task PayingTheRecovery_RestoresService_RestartsThePeriodNow_AndKeepsTheNewCard()
    {
        var (_, subscription) = await SeedAsync(SubscriptionStatus.Suspended, DateTime.UtcNow.AddDays(-4));
        var checkout = await PayNowAsync(subscription.Id);

        var paid = await CallbackAsync(checkout.Data!.TradeNo!, "300", savedToken: "pm_recovered_card");
        paid.Succeeded.ShouldBeTrue(paid.Message);

        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.Active);
        reloaded.SuspendedAt.ShouldBeNull();
        reloaded.PastDueSince.ShouldBeNull();
        reloaded.RenewalRetryCount.ShouldBe(0);
        reloaded.RecoveryPaymentTradeNo.ShouldBeNull();
        reloaded.NextBillingTime!.Value.ShouldBe(DateTime.UtcNow.AddMonths(1), TimeSpan.FromMinutes(5),
            "the new period starts when the account paid, not when the unpaid one started");
        reloaded.PaymentMethodToken.ShouldBe("pm_recovered_card", "the card that just worked replaces the one that kept failing");
        reloaded.StoredPaymentMethodId.ShouldNotBeNull();

        var recovered = _events.Of<SubscriptionRecoveredEvent>().ShouldHaveSingleItem();
        recovered.PreviousStatus.ShouldBe(SubscriptionStatus.Suspended);
        recovered.PaymentTradeNo.ShouldBe(checkout.Data.TradeNo);
        _events.Of<BillingAccountStandingChangedEvent>().Last().CurrentStanding.ShouldBe(BillingStanding.Good);
    }

    [Fact]
    public async Task AnUnpaidRecoveryPayment_IsNotABillingFailure()
    {
        var (_, subscription) = await SeedAsync(SubscriptionStatus.PastDue, DateTime.UtcNow.AddHours(-2));
        var checkout = await PayNowAsync(subscription.Id);
        checkout.Succeeded.ShouldBeTrue(checkout.Message);

        var failed = await CallbackAsync(checkout.Data!.TradeNo!, "0", status: "Failed");
        failed.Succeeded.ShouldBeTrue(failed.Message);

        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.PastDue);
        reloaded.RenewalRetryCount.ShouldBe(3, "an abandoned checkout does not count as another failed charge");
        reloaded.RecoveryPaymentTradeNo.ShouldBeNull("the next pay-now starts a fresh payment");
        _events.Of<SubscriptionPastDueEvent>().ShouldBeEmpty();
    }

    /// <summary>
    /// 消费方实测：管理者点了「立即付款」就走开，补缴单过期关闭，流水号清掉了，计费锁却还停在等那张单的时长上，
    /// 续费扫描一直「Skipping ... has not settled yet」，过期扫描也跳过它直到锁自己到期。
    /// </summary>
    [Fact]
    public async Task AnAbandonedRecoveryPayment_ReleasesTheLockTheScanHeldForIt()
    {
        var (_, subscription) = await SeedAsync(SubscriptionStatus.PastDue, DateTime.UtcNow.AddDays(-10));
        // 还有重试余量：续费扫描会来重试这一期
        await InScopeAsync<IRepository<Subscription, Guid>, int>(repo => repo.AsQueryable()
            .Where(s => s.Id == subscription.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RenewalRetryCount, 1)));
        var checkout = await PayNowAsync(subscription.Id);
        checkout.Succeeded.ShouldBeTrue(checkout.Message);

        // 续费扫描看见一张还能付的补缴单：不再扣一笔，把锁一直持到那张单落定
        await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.RenewExpiredSubscriptionsAsync());
        (await ReloadAsync<Subscription>(subscription.Id))!.BillingLockedUntil.ShouldNotBeNull("precondition: a scan is waiting on the open recovery payment");

        (await CallbackAsync(checkout.Data!.TradeNo!, "0", status: "Failed")).Succeeded.ShouldBeTrue();

        var released = await ReloadAsync<Subscription>(subscription.Id);
        released!.RecoveryPaymentTradeNo.ShouldBeNull();
        released.BillingLockedUntil.ShouldBeNull("nothing is left to wait for");

        await ExpireScanAsync();
        (await ReloadAsync<Subscription>(subscription.Id))!.Status.ShouldBe(SubscriptionStatus.Suspended, "the overdue handling resumes at once instead of after the lock runs out");
    }

    [Fact]
    public async Task PayNow_IsOnlyForPastDueOrSuspended()
    {
        var (_, subscription) = await SeedAsync(SubscriptionStatus.Active, pastDueSince: null);

        var refused = await PayNowAsync(subscription.Id);

        refused.Code.ShouldBe(400);
        refused.Message.ShouldBe(ErrorCodes.SubscriptionCannotPayNow);
    }

    [Fact]
    public async Task PayNow_OnSomeoneElsesSubscription_IsNotFound()
    {
        var (_, subscription) = await SeedAsync(SubscriptionStatus.Suspended, DateTime.UtcNow.AddDays(-4), userId: Guid.NewGuid());

        var refused = await PayNowAsync(subscription.Id, Owner);

        refused.Code.ShouldBe(404);
    }

    [Fact]
    public async Task ReplacingTheCardOnASuspendedSubscription_RetriesAtOnce_AndRecoversWhenTheChargeSucceeds()
    {
        var (_, subscription) = await SeedAsync(SubscriptionStatus.Suspended, DateTime.UtcNow.AddDays(-4), card: null);

        var updated = await InScopeAsync<ISubscriptionService, Result<SubscriptionDto>>(svc => svc.UpdatePaymentMethodAsync(subscription.Id, new AttachPaymentMethodDto
        {
            PaymentMethodToken = "pm_fresh_card"
        }, Owner));

        updated.Succeeded.ShouldBeTrue(updated.Message);
        // 测试渠道的无人值守扣款当场成功
        updated.Data!.Status.ShouldBe(SubscriptionStatus.Active);
        _events.Of<SubscriptionRecoveredEvent>().ShouldHaveSingleItem().PreviousStatus.ShouldBe(SubscriptionStatus.Suspended);
    }

    [Fact]
    public async Task EnteringPastDue_IsAnnouncedOnce_NotOnEveryRetry()
    {
        var (_, subscription) = await SeedAsync(SubscriptionStatus.Active, pastDueSince: null, card: null);

        await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.RenewExpiredSubscriptionsAsync());
        (await ReloadAsync<Subscription>(subscription.Id))!.Status.ShouldBe(SubscriptionStatus.PastDue);

        await InScopeAsync<ISubscriptionService, Result>(svc => svc.ApplyPaymentFailedAsync(new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.Renewal,
            SubscriptionId = subscription.Id,
            PayerAccount = BillingAccountRef.ForUser(Owner),
            FailReason = "card declined"
        }));

        var pastDue = _events.Of<SubscriptionPastDueEvent>().ShouldHaveSingleItem();
        pastDue.GraceDeadline.ShouldBe(DateTime.UtcNow.AddDays(3), TimeSpan.FromMinutes(5));
    }

    /// <summary>按类型收集事件。</summary>
    public sealed class EventRecorder
    {
        private readonly List<object> _events = [];
        public void Add(object e) { lock (_events) _events.Add(e); }
        public List<T> Of<T>() { lock (_events) return _events.OfType<T>().ToList(); }
    }

    private sealed class RecordingHandler<T>(EventRecorder recorder) : IEventHandler<T> where T : IEvent
    {
        public Task HandleAsync(T @event, CancellationToken cancellationToken = default)
        {
            recorder.Add(@event);
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// 付款回流按<b>计费账户</b>认主：单客户部署里 A 开通、B 付款，钱记在同一个账户上，照样推进；别的账户付的钱不认。
/// </summary>
public class SubscriptionAccountOwnershipTests : SubscriptionsIntegrationTestBase
{
    private static readonly BillingAccountRef Firm = new(BillingAccountTypes.Installation, "firm");

    private async Task<Subscription> SeedFirmSubscriptionAsync()
    {
        var plan = new SubscriptionPlan
        {
            PlanCode = $"P{Guid.NewGuid():N}"[..12],
            PlanName = "Firm",
            Price = 300m,
            Currency = "CAD",
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            IsActive = true
        };
        await SeedAsync(plan);

        var subscription = new Subscription
        {
            SubscriptionNo = $"SUB{Guid.NewGuid():N}"[..16],
            UserId = Guid.NewGuid(), // 开通的那位管理者
            BillingAccountType = Firm.Type,
            BillingAccountId = Firm.Id,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Active,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            StartTime = DateTime.UtcNow.AddMonths(-1),
            NextBillingTime = DateTime.UtcNow.AddMinutes(-5),
            OriginalPrice = 300m,
            Currency = "CAD",
            AutoRenew = true,
            ChannelCode = "Null"
        };
        await SeedAsync(subscription);
        return subscription;
    }

    private Task<Result> RenewalPaidAsync(Subscription subscription, BillingAccountRef payerAccount, Guid payerUser) =>
        InScopeAsync<ISubscriptionService, Result>(svc => svc.ApplyPaymentCompletedAsync(new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.Renewal,
            SubscriptionId = subscription.Id,
            SubscriptionNo = subscription.SubscriptionNo,
            PayerAccount = payerAccount,
            PayerUserId = payerUser,
            PaymentTradeNo = $"PAY{Guid.NewGuid():N}"[..16],
            Amount = 300m,
            Currency = "CAD"
        }));

    [Fact]
    public async Task APaymentByAnotherManagerOfTheSameAccount_RenewsTheSubscription()
    {
        var subscription = await SeedFirmSubscriptionAsync();

        var applied = await RenewalPaidAsync(subscription, Firm, payerUser: Guid.NewGuid());

        applied.Succeeded.ShouldBeTrue(applied.Message);
        (await ReloadAsync<Subscription>(subscription.Id))!.NextBillingTime!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddDays(20));
    }

    [Fact]
    public async Task APaymentOnAnotherAccount_IsRejected_EvenFromTheUserWhoSubscribed()
    {
        var subscription = await SeedFirmSubscriptionAsync();

        var refused = await RenewalPaidAsync(subscription, BillingAccountRef.ForUser(subscription.UserId), subscription.UserId);

        refused.Code.ShouldBe(403);
        refused.Message.ShouldBe(ErrorCodes.SubscriptionPaymentOwnerMismatch);
        (await ReloadAsync<Subscription>(subscription.Id))!.NextBillingTime!.Value.ShouldBeLessThan(DateTime.UtcNow);
    }
}
