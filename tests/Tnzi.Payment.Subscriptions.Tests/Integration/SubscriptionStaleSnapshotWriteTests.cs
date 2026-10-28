using Tnzi.Notification.Dtos;
using Tnzi.Notification.Services;

namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 订阅行不能被一份读得太早的快照整行写回。
/// </summary>
/// <remarks>
/// <para>
/// 仓储的 <c>UpdateAsync</c> 把实体的每一列都标成已修改，<c>Subscription</c> 又没有并发令牌：
/// 读与写之间隔着发信、渠道往返或一整页别的订阅时，中途别人写下的取消、续费推进会被安静地写回旧值。
/// </para>
/// <para>
/// 这里的每一条都按同一个形状复现：扫描或请求读到快照 → 并发的那一笔在自己的 scope 里落库 → 原操作写回。
/// 修复之后，用户发起的状态变更（暂停 / 恢复 / 开关自动续费）与取消一样先抢计费锁再读行，
/// 后台扫描抢锁之后重读并重新判定，只改几列的操作（续费提醒标记、换卡）只写那几列。
/// </para>
/// </remarks>
public class SubscriptionStaleSnapshotWriteTests : SubscriptionsIntegrationTestBase
{
    private readonly TestEventHooks _hooks = new();
    private Func<Task<bool>> _onReminder = () => Task.FromResult(true);
    private Func<Task> _onFindPaymentMethod = () => Task.CompletedTask;

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        _hooks.Register(services);
        services.Configure<SubscriptionOptions>(o => o.AutoRenewalReminderDays = 7);

        var notifications = new Mock<INotificationService>();
        notifications
            .Setup(n => n.CreateAndSendAsync(It.IsAny<CreateNotificationRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async () => await _onReminder()
                ? Result.Success(new NotificationInfo())
                : Result.Failure<NotificationInfo>("delivery failed", 500));
        services.AddScoped(_ => notifications.Object);

        services.AddScoped<PaymentMethodService>();
        services.AddScoped<IPaymentMethodService>(sp =>
            new HookedPaymentMethodService(sp.GetRequiredService<PaymentMethodService>(), () => _onFindPaymentMethod()));
    }

    private async Task<SubscriptionPlan> SeedPlanAsync(decimal price = 30m)
    {
        var plan = new SubscriptionPlan
        {
            PlanCode = $"P{Guid.NewGuid():N}"[..12],
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

    private async Task<Subscription> SeedSubscriptionAsync(SubscriptionPlan plan, Action<Subscription>? shape = null)
    {
        var subscription = new Subscription
        {
            SubscriptionNo = $"SUB{Guid.NewGuid():N}",
            UserId = TestHelper.DefaultTestUserId,
            CustomerEmail = "payer@example.com",
            PlanId = plan.Id,
            Status = SubscriptionStatus.Active,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            StartTime = DateTime.UtcNow.AddMonths(-1),
            NextBillingTime = DateTime.UtcNow.AddDays(3),
            OriginalPrice = plan.Price,
            Currency = "USD",
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

    private async Task UpdateRowAsync(Guid id, Action<Subscription> change)
    {
        using var scope = ServiceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
        var row = await ctx.Set<Subscription>().FirstAsync(s => s.Id == id);
        change(row);
        await ctx.SaveChangesAsync();
    }

    private Task<Result> CancelAsync(Guid id, bool immediate) =>
        InScopeAsync<ISubscriptionService, Result>(svc => svc.CancelSubscriptionAsync(id, new CancelSubscriptionDto { Reason = "changed my mind", Immediate = immediate }));

    // ── 续费提醒 ────────────────────────────────────────────────────────────

    /// <summary>
    /// ★ 提醒扫描逐封发信的那几分钟里用户取消了：扫描写回快照会把取消撤销（Active、AutoRenew=true、CancelTime=null），到期照扣。
    /// </summary>
    [Fact]
    public async Task ACancellationDuringTheReminderRun_Survives()
    {
        var plan = await SeedPlanAsync();
        var subscription = await SeedSubscriptionAsync(plan);
        _onReminder = async () =>
        {
            (await CancelAsync(subscription.Id, immediate: false)).Succeeded.ShouldBeTrue();
            return true;
        };

        var sent = await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.SendRenewalRemindersAsync());

        sent.Data.ShouldBe(1);
        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.PendingRenewal);
        reloaded.AutoRenew.ShouldBeFalse();
        reloaded.CancelTime.ShouldNotBeNull();
        reloaded.RenewalReminderSentFor.ShouldBe(subscription.NextBillingTime);
    }

    /// <summary>没送达的提醒不算提醒过：下一轮还会再发。</summary>
    [Fact]
    public async Task AReminderThatWasNotDelivered_IsSentAgainNextRun()
    {
        var plan = await SeedPlanAsync();
        var subscription = await SeedSubscriptionAsync(plan);
        _onReminder = () => Task.FromResult(false);

        (await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.SendRenewalRemindersAsync())).Data.ShouldBe(0);
        (await ReloadAsync<Subscription>(subscription.Id))!.RenewalReminderSentFor.ShouldBeNull();

        _onReminder = () => Task.FromResult(true);
        (await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.SendRenewalRemindersAsync())).Data.ShouldBe(1);
        (await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.SendRenewalRemindersAsync())).Data.ShouldBe(0);
    }

    // ── 用户发起的变更：与取消同一把计费锁 ──────────────────────────────────────

    /// <summary>
    /// ★ 后台扣款在途（计费锁未过期）时暂停：完成回流随后把 Paused 改回 Active 并推进周期，用户以为已暂停却被扣款。
    /// 与取消一样答 409，什么都不改。
    /// </summary>
    [Fact]
    public async Task PausingWhileAChargeIsInFlight_IsRefusedWithAConflict()
    {
        var plan = await SeedPlanAsync();
        var subscription = await SeedSubscriptionAsync(plan, s => s.BillingLockedUntil = DateTime.UtcNow.AddMinutes(5));

        var paused = await InScopeAsync<ISubscriptionService, Result>(svc => svc.PauseSubscriptionAsync(subscription.Id, new PauseSubscriptionDto()));

        paused.Succeeded.ShouldBeFalse();
        paused.Code.ShouldBe(409);
        paused.Message.ShouldBe(ErrorCodes.SubscriptionBillingInProgress);
        (await ReloadAsync<Subscription>(subscription.Id))!.Status.ShouldBe(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task TurningAutoRenewOffWhileAChargeIsInFlight_IsRefusedWithAConflict()
    {
        var plan = await SeedPlanAsync();
        var subscription = await SeedSubscriptionAsync(plan, s => s.BillingLockedUntil = DateTime.UtcNow.AddMinutes(5));

        var toggled = await InScopeAsync<ISubscriptionService, Result>(svc => svc.UpdateAutoRenewAsync(subscription.Id, false));

        toggled.Code.ShouldBe(409);
        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.AutoRenew.ShouldBeTrue();
        reloaded.Status.ShouldBe(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task ResumingWhileTheSubscriptionIsLocked_IsRefusedWithAConflict()
    {
        var plan = await SeedPlanAsync();
        var subscription = await SeedSubscriptionAsync(plan, s =>
        {
            s.Status = SubscriptionStatus.Paused;
            s.PausedAt = DateTime.UtcNow.AddDays(-1);
            s.BillingLockedUntil = DateTime.UtcNow.AddMinutes(5);
        });

        var resumed = await InScopeAsync<ISubscriptionService, Result>(svc => svc.ResumeSubscriptionAsync(subscription.Id));

        resumed.Code.ShouldBe(409);
        (await ReloadAsync<Subscription>(subscription.Id))!.Status.ShouldBe(SubscriptionStatus.Paused);
    }

    /// <summary>变更完成即放锁，否则后台扫描要白等一个锁窗口。</summary>
    [Fact]
    public async Task APauseReleasesTheBillingLock()
    {
        var plan = await SeedPlanAsync();
        var subscription = await SeedSubscriptionAsync(plan);

        (await InScopeAsync<ISubscriptionService, Result>(svc => svc.PauseSubscriptionAsync(subscription.Id, new PauseSubscriptionDto()))).Succeeded.ShouldBeTrue();

        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.Paused);
        reloaded.BillingLockedUntil.ShouldBeNull();
    }

    /// <summary>别人的订阅：先认归属再抢锁，不能借一次 404 请求把别人的订阅锁上。</summary>
    [Fact]
    public async Task AChangeOnSomeoneElsesSubscription_DoesNotTakeItsLock()
    {
        var plan = await SeedPlanAsync();
        var subscription = await SeedSubscriptionAsync(plan);

        var paused = await InScopeAsync<ISubscriptionService, Result>(svc =>
            svc.PauseSubscriptionAsync(subscription.Id, new PauseSubscriptionDto(), ownerUserId: Guid.NewGuid()));

        paused.Code.ShouldBe(404);
        (await ReloadAsync<Subscription>(subscription.Id))!.BillingLockedUntil.ShouldBeNull();
    }

    // ── 换卡 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// ★ 换卡在读行之后、写行之前隔着一次绑卡（渠道往返）。这期间续费回流推进了周期：整行写回会把「已付到」拨回旧值，
    /// 锁过期后同一期再扣一次。换卡只写支付方式那几列。
    /// </summary>
    [Fact]
    public async Task ChangingTheCard_KeepsARenewalThatLandedMeanwhile()
    {
        var plan = await SeedPlanAsync();
        var subscription = await SeedSubscriptionAsync(plan);
        var card = new StoredPaymentMethod
        {
            UserId = TestHelper.DefaultTestUserId,
            ChannelCode = "Null",
            Token = "pm_new",
            ProviderCustomerId = "cus_test",
            Brand = "visa",
            Last4 = "4242",
            IsActive = true
        };
        await SeedAsync(card);

        var renewedTo = DateTime.UtcNow.AddMonths(2);
        _onFindPaymentMethod = () => UpdateRowAsync(subscription.Id, s =>
        {
            s.NextBillingTime = renewedTo;
            s.LastBillingTradeNo = "PAY-RENEWED-MEANWHILE";
        });

        var changed = await InScopeAsync<ISubscriptionService, Result<SubscriptionDto>>(svc =>
            svc.UpdatePaymentMethodAsync(subscription.Id, new AttachPaymentMethodDto { PaymentMethodId = card.Id }));

        changed.Succeeded.ShouldBeTrue(changed.Message);
        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.PaymentMethodToken.ShouldBe("pm_new");
        reloaded.PaymentMethodLast4.ShouldBe("4242");
        reloaded.StoredPaymentMethodId.ShouldBe(card.Id);
        reloaded.NextBillingTime!.Value.ShouldBe(renewedTo, TimeSpan.FromSeconds(1));
        reloaded.LastBillingTradeNo.ShouldBe("PAY-RENEWED-MEANWHILE");
    }

    // ── 暂停到期的自动恢复 ───────────────────────────────────────────────────────

    /// <summary>后台恢复与用户操作抢同一把锁：锁被别人持有的那一条这一轮不动。</summary>
    [Fact]
    public async Task TheAutoResumeScan_SkipsALockedSubscription()
    {
        var plan = await SeedPlanAsync();
        var subscription = await SeedSubscriptionAsync(plan, s =>
        {
            s.Status = SubscriptionStatus.Paused;
            s.PausedAt = DateTime.UtcNow.AddDays(-2);
            s.PausedUntil = DateTime.UtcNow.AddMinutes(-1);
            s.BillingLockedUntil = DateTime.UtcNow.AddMinutes(5);
        });

        (await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ResumeDuePausedSubscriptionsAsync())).Data.ShouldBe(0);

        (await ReloadAsync<Subscription>(subscription.Id))!.Status.ShouldBe(SubscriptionStatus.Paused);
    }

    /// <summary>
    /// ★ 自动恢复一次读出一整页，逐条恢复（可能含一次渠道扣款）。轮到第二条之前它被取消了：
    /// 写回快照会把取消撤销、让它重新在跑。
    /// </summary>
    [Fact]
    public async Task TheAutoResumeScan_DoesNotReviveASubscriptionCancelledMeanwhile()
    {
        var plan = await SeedPlanAsync();
        var first = await SeedSubscriptionAsync(plan, s =>
        {
            s.Status = SubscriptionStatus.Paused;
            s.PausedAt = DateTime.UtcNow.AddDays(-2);
            s.PausedUntil = DateTime.UtcNow.AddMinutes(-10);
            // 恢复时已付的那期早就走完：当场续费扣款，扣款完成事件就是插进并发动作的那个时刻
            s.NextBillingTime = DateTime.UtcNow.AddDays(-5);
        });
        var second = await SeedSubscriptionAsync(plan, s =>
        {
            s.UserId = TestHelper.DefaultTestUserId;
            s.ProductCode = "other";
            s.Status = SubscriptionStatus.Paused;
            s.PausedAt = DateTime.UtcNow.AddDays(-2);
            s.PausedUntil = DateTime.UtcNow.AddMinutes(-5);
        });
        _hooks.OnPaymentCompleted(first.SubscriptionNo, async () =>
            (await CancelAsync(second.Id, immediate: true)).Succeeded.ShouldBeTrue());

        await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ResumeDuePausedSubscriptionsAsync());

        (await ReloadAsync<Subscription>(first.Id))!.Status.ShouldBe(SubscriptionStatus.Active);
        (await ReloadAsync<Subscription>(second.Id))!.Status.ShouldBe(SubscriptionStatus.Cancelled);
    }

    /// <summary>把查询交给真实实现，只在按 Id 取支付方式时先跑一段并发动作。</summary>
    private sealed class HookedPaymentMethodService(IPaymentMethodService inner, Func<Task> onFindById) : IPaymentMethodService
    {
        public Task<Result<SetupSessionDto>> CreateSetupSessionAsync(Guid userId, CreateSetupSessionDto request, CancellationToken cancellationToken = default)
            => inner.CreateSetupSessionAsync(userId, request, cancellationToken);

        public Task<Result<StoredPaymentMethodDto>> BindAsync(Guid userId, BindPaymentMethodDto request, CancellationToken cancellationToken = default)
            => inner.BindAsync(userId, request, cancellationToken);

        public Task<Result<List<StoredPaymentMethodDto>>> GetUserMethodsAsync(Guid userId, CancellationToken cancellationToken = default)
            => inner.GetUserMethodsAsync(userId, cancellationToken);

        public Task<Result> SetDefaultAsync(Guid userId, Guid paymentMethodId, CancellationToken cancellationToken = default)
            => inner.SetDefaultAsync(userId, paymentMethodId, cancellationToken);

        public Task<Result> RemoveAsync(Guid userId, Guid paymentMethodId, CancellationToken cancellationToken = default)
            => inner.RemoveAsync(userId, paymentMethodId, cancellationToken);

        public Task<StoredPaymentMethod?> FindDefaultAsync(BillingAccountRef account, string channelCode, CancellationToken cancellationToken = default)
            => inner.FindDefaultAsync(account, channelCode, cancellationToken);

        public async Task<StoredPaymentMethod?> FindByIdAsync(BillingAccountRef account, Guid paymentMethodId, CancellationToken cancellationToken = default)
        {
            await onFindById();
            return await inner.FindByIdAsync(account, paymentMethodId, cancellationToken);
        }

        public Task<Result<StoredPaymentMethod>> BindEntityAsync(BillingAccountRef account, Guid actingUserId, string channelCode, string paymentMethodToken, string? customerName, string? customerEmail, bool setAsDefault, CancellationToken cancellationToken = default)
            => inner.BindEntityAsync(account, actingUserId, channelCode, paymentMethodToken, customerName, customerEmail, setAsDefault, cancellationToken);

        public Task<string?> FindProviderCustomerIdAsync(BillingAccountRef account, string channelCode, CancellationToken cancellationToken = default)
            => inner.FindProviderCustomerIdAsync(account, channelCode, cancellationToken);

        public Result<PaymentClientConfigDto> GetClientConfig(string? channelCode)
            => inner.GetClientConfig(channelCode);

        public Task<Result> RefreshByTokenAsync(string channelCode, PaymentProviderPaymentMethodResult details, CancellationToken cancellationToken = default)
            => inner.RefreshByTokenAsync(channelCode, details, cancellationToken);

        public Task MarkUsedAsync(Guid paymentMethodId, CancellationToken cancellationToken = default)
            => inner.MarkUsedAsync(paymentMethodId, cancellationToken);

        public Task<Result> DeactivateByTokenAsync(string channelCode, string token, CancellationToken cancellationToken = default)
            => inner.DeactivateByTokenAsync(channelCode, token, cancellationToken);
    }
}
