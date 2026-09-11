namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 取消订阅：两条成功路径，以及与在途扣款的竞态。
/// </summary>
/// <remarks>
/// 后台扣款抢到一条订阅之后（<c>TryClaimAsync</c> 写下 <c>BillingLockedUntil</c>），
/// 它与渠道之间那次往返可能持续数秒。此前取消完全不看这把锁：那几秒里用户点「取消」当场成功，
/// 而钱随后照扣不误 —— 用户看到的是「已取消」外加一笔扣款，日志里只有一句 orphan payment，
/// 退款要靠人工。取消现在按与后台同一条件抢同一把锁，抢不到就让调用方稍后再来。
/// </remarks>
public class SubscriptionCancellationIntegrationTests : SubscriptionsIntegrationTestBase
{
    private async Task<Subscription> SeedSubscriptionAsync(DateTime? billingLockedUntil = null)
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

        var subscription = new Subscription
        {
            SubscriptionNo = $"SUB{Guid.NewGuid():N}",
            UserId = Guid.NewGuid(),
            PlanId = plan.Id,
            Status = SubscriptionStatus.Active,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            StartTime = DateTime.UtcNow.AddMonths(-1),
            NextBillingTime = DateTime.UtcNow.AddDays(5),
            OriginalPrice = 30m,
            Currency = "USD",
            AutoRenew = true,
            ChannelCode = "Null",
            BillingLockedUntil = billingLockedUntil
        };
        await SeedAsync(subscription);
        return subscription;
    }

    private Task<Result> CancelAsync(Guid id, bool immediate) =>
        InScopeAsync<ISubscriptionService, Result>(
            svc => svc.CancelSubscriptionAsync(id, new CancelSubscriptionDto { Reason = "test", Immediate = immediate }));

    [Fact]
    public async Task ImmediateCancel_MovesToCancelledAndStopsTheBillingClock()
    {
        var subscription = await SeedSubscriptionAsync();

        (await CancelAsync(subscription.Id, immediate: true)).Succeeded.ShouldBeTrue();

        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.Cancelled);
        reloaded.CancelReason.ShouldBe("test");
        reloaded.EndTime.ShouldNotBeNull();
        reloaded.NextBillingTime.ShouldBeNull();
        // 取消完成即释放计费锁，否则后台扫描白等一个锁窗口
        reloaded.BillingLockedUntil.ShouldBeNull();
    }

    [Fact]
    public async Task DeferredCancel_TurnsOffAutoRenewAndWaitsForThePeriodToEnd()
    {
        var subscription = await SeedSubscriptionAsync();

        (await CancelAsync(subscription.Id, immediate: false)).Succeeded.ShouldBeTrue();

        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.PendingRenewal);
        reloaded.AutoRenew.ShouldBeFalse();
        reloaded.BillingLockedUntil.ShouldBeNull();
    }

    /// <summary>
    /// 后台正拿着计费锁时，取消必须被拒 —— 那几秒里钱正在路上。
    /// 409 而不是 400：锁最多持有 <c>BillingLockMinutes</c> 分钟，这不是一件永远不会好的事。
    /// </summary>
    [Fact]
    public async Task CancellingWhileAChargeIsInFlight_IsRefused()
    {
        var subscription = await SeedSubscriptionAsync(billingLockedUntil: DateTime.UtcNow.AddMinutes(5));

        var result = await CancelAsync(subscription.Id, immediate: true);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
        result.Message.ShouldBe(ErrorCodes.SubscriptionBillingInProgress);
        (await ReloadAsync<Subscription>(subscription.Id))!.Status.ShouldBe(SubscriptionStatus.Active);
    }

    /// <summary>
    /// 过期的锁不算锁：扣款进程崩在半路不能把这条订阅永久变成不可取消。
    /// </summary>
    [Fact]
    public async Task AnExpiredBillingLock_DoesNotBlockCancellation()
    {
        var subscription = await SeedSubscriptionAsync(billingLockedUntil: DateTime.UtcNow.AddMinutes(-1));

        (await CancelAsync(subscription.Id, immediate: true)).Succeeded.ShouldBeTrue();

        (await ReloadAsync<Subscription>(subscription.Id))!.Status.ShouldBe(SubscriptionStatus.Cancelled);
    }
}
