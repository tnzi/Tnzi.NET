namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 续费扣款还没落定（渠道回报处理中，或这次调用的结果无从得知）时，订阅等它落定：不再扣一笔、不降级、不能被取消或过期。
/// </summary>
/// <remarks>
/// 此前支付侧把这类结果一律落成失败：订阅降级 PastDue、发催款信，下一轮换一个流水号（也就换了幂等键）再扣一笔；
/// 原来那笔随后入账时撞上 Failed 终态，只留一行告警，同一期扣了两次，订阅却显示逾期。
/// </remarks>
public class SubscriptionChargeInFlightTests : SubscriptionsIntegrationTestBase
{
    private const string Channel = "InFlight";

    private readonly Mock<IPaymentProvider> _channel = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        _channel.SetupGet(p => p.ChannelCode).Returns(Channel);
        _channel.SetupGet(p => p.ChannelName).Returns(Channel);
        _channel.SetupGet(p => p.SupportsOffSessionCharge).Returns(true);
        // 到期核实时渠道答不上来：不能作废的渠道就地过期（与 PayPal 订单同形）
        _channel.Setup(p => p.SyncOrderAsync(It.IsAny<string>()))
            .ReturnsAsync(Result.Failure<PaymentProviderQueryResult>("not found", 404));
        services.AddScoped(_ => _channel.Object);
        services.Configure<PaymentOptions>(o => o.Channels[Channel] = new ChannelOptions { Enabled = true, Currency = "USD" });
    }

    private void ChannelAnswers(Result<PaymentProviderChargeResult> answer) =>
        _channel.Setup(p => p.ChargeOffSessionAsync(It.IsAny<PaymentProviderChargeDto>())).ReturnsAsync(answer);

    private static Result<PaymentProviderChargeResult> Pending() => Result.Success(new PaymentProviderChargeResult
    {
        ExternalTradeNo = "CAP-PENDING",
        Status = PaymentStatus.Processing,
        PaidAmount = 30m
    });

    private async Task<Subscription> SeedDueAsync(string subscriptionNo)
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

        var subscription = new Subscription
        {
            SubscriptionNo = subscriptionNo,
            UserId = TestHelper.DefaultTestUserId,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Active,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            StartTime = DateTime.UtcNow.AddMonths(-1),
            NextBillingTime = DateTime.UtcNow.AddMinutes(-5),
            OriginalPrice = 30m,
            PaidAmount = 30m,
            Currency = "USD",
            AutoRenew = true,
            ChannelCode = Channel,
            PaymentMethodToken = "pm_test",
            ProviderCustomerId = "cus_test",
            LastBillingTradeNo = $"PAY-SEED-{subscriptionNo}"
        };
        await SeedAsync(subscription);
        return subscription;
    }

    private Task<Result<int>> RenewAsync() =>
        InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.RenewExpiredSubscriptionsAsync());

    private async Task<List<PaymentEntity>> PaymentsOfAsync(string subscriptionNo)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<PaymentEntity, Guid>>();
        return await repo.AsQueryable().Where(p => p.BusinessOrderNo == subscriptionNo).ToListAsync();
    }

    private async Task UpdateSubscriptionAsync(Guid id, Action<Subscription> change)
    {
        using var scope = ServiceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
        change(ctx.Set<Subscription>().First(s => s.Id == id));
        await ctx.SaveChangesAsync();
    }

    private async Task UpdatePaymentAsync(string tradeNo, Action<PaymentEntity> change)
    {
        using var scope = ServiceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
        change(ctx.Set<PaymentEntity>().First(p => p.TradeNo == tradeNo));
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task ARenewalTheChannelReportsAsProcessing_IsNeitherDowngradedNorChargedAgain()
    {
        ChannelAnswers(Pending());
        var subscription = await SeedDueAsync("SUB-FLIGHT-1");

        await RenewAsync();

        var held = await ReloadAsync<Subscription>(subscription.Id);
        held!.Status.ShouldBe(SubscriptionStatus.Active);
        held.RenewalRetryCount.ShouldBe(0);
        held.PastDueSince.ShouldBeNull();
        held.BillingLockedUntil!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddDays(6));
        (await PaymentsOfAsync("SUB-FLIGHT-1")).Single().Status.ShouldBe(PaymentStatus.Processing);

        // 扣款在途：取消答 409，过期扫描跳过它
        var cancel = await InScopeAsync<ISubscriptionService, Result>(svc => svc.CancelSubscriptionAsync(
            subscription.Id, new CancelSubscriptionDto { Immediate = true }));
        cancel.Code.ShouldBe(409);

        // 锁先于支付到期（回流迟迟不来）：下一轮扫描选中它，也不会再扣一笔
        await UpdateSubscriptionAsync(subscription.Id, s => s.BillingLockedUntil = DateTime.UtcNow.AddMinutes(-1));
        await RenewAsync();

        (await PaymentsOfAsync("SUB-FLIGHT-1")).Count.ShouldBe(1);
        _channel.Verify(p => p.ChargeOffSessionAsync(It.IsAny<PaymentProviderChargeDto>()), Times.Once);
        (await ReloadAsync<Subscription>(subscription.Id))!.BillingLockedUntil!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddDays(6));
    }

    [Fact]
    public async Task WhenTheInFlightRenewalSettlesAsPaid_TheSubscriptionAdvancesAndIsUnlocked()
    {
        ChannelAnswers(Pending());
        var subscription = await SeedDueAsync("SUB-FLIGHT-2");
        await RenewAsync();
        var payment = (await PaymentsOfAsync("SUB-FLIGHT-2")).Single();

        var settled = await InScopeAsync<IPaymentService, Result>(svc => svc.HandleCallbackAsync(new PaymentCallbackDto
        {
            ChannelCode = "Null",
            Parameters = new Dictionary<string, string>
            {
                ["trade_no"] = payment.TradeNo,
                ["amount"] = "30",
                ["event_id"] = Guid.NewGuid().ToString("N")
            }
        }));

        settled.Succeeded.ShouldBeTrue(settled.Message);
        var renewed = await ReloadAsync<Subscription>(subscription.Id);
        renewed!.Status.ShouldBe(SubscriptionStatus.Active);
        renewed.NextBillingTime!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddDays(25));
        renewed.BillingLockedUntil.ShouldBeNull();
        renewed.LastBillingTradeNo.ShouldBe(payment.TradeNo);
    }

    /// <summary>回调始终没来、支付到期：核实之后过期，订阅这才按一次失败降级 PastDue。</summary>
    [Fact]
    public async Task WhenTheInFlightRenewalExpiresUnpaid_TheSubscriptionFallsPastDueOnce()
    {
        ChannelAnswers(Pending());
        var subscription = await SeedDueAsync("SUB-FLIGHT-3");
        await RenewAsync();
        var payment = (await PaymentsOfAsync("SUB-FLIGHT-3")).Single();

        // 还没到它的有效期：过期清扫不碰它
        (await InScopeAsync<IPaymentService, Result<int>>(svc => svc.CloseExpiredPaymentsAsync())).Data.ShouldBe(0);

        await UpdatePaymentAsync(payment.TradeNo, p => p.ExpireTime = DateTime.UtcNow.AddMinutes(-1));
        (await InScopeAsync<IPaymentService, Result<int>>(svc => svc.CloseExpiredPaymentsAsync())).Data.ShouldBe(1);

        var lapsed = await ReloadAsync<Subscription>(subscription.Id);
        lapsed!.Status.ShouldBe(SubscriptionStatus.PastDue);
        lapsed.RenewalRetryCount.ShouldBe(1);
        lapsed.BillingLockedUntil.ShouldBeNull();
    }

    /// <summary>
    /// 续费在等一笔还没落定的 off-session 补差扣款（持锁等它）：那笔补差以「没收到钱」结束，变更取消、锁随之放掉，续费不必干等到锁过期。
    /// </summary>
    [Fact]
    public async Task WhenTheProrationChargeARenewalWaitsOnEndsUnpaid_TheRenewalIsReleased()
    {
        ChannelAnswers(Pending());
        var subscription = await SeedDueAsync("SUB-FLIGHT-5");
        var pro = new SubscriptionPlan
        {
            PlanCode = $"P{Guid.NewGuid():N}"[..12],
            PlanName = "Pro Plus",
            Price = 90m,
            Currency = "USD",
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            IsActive = true
        };
        await SeedAsync(pro);
        await UpdateSubscriptionAsync(subscription.Id, s => s.NextBillingTime = DateTime.UtcNow.AddDays(15));

        var change = await InScopeAsync<ISubscriptionService, Result<SubscriptionChangeDto>>(svc => svc.ChangeSubscriptionPlanAsync(
            subscription.Id, new ChangeSubscriptionPlanDto { NewPlanId = pro.Id, EffectiveImmediately = true }));
        change.Succeeded.ShouldBeTrue(change.Message);
        change.Data!.Status.ShouldBe(SubscriptionChangeStatus.AwaitingPayment);
        var proration = (await PaymentsOfAsync("SUB-FLIGHT-5")).Single();
        proration.Status.ShouldBe(PaymentStatus.Processing);

        // 周期到了：续费看到这笔还没落定的补差，持锁等它而不是再扣一笔
        await UpdateSubscriptionAsync(subscription.Id, s => s.NextBillingTime = DateTime.UtcNow.AddMinutes(-5));
        await RenewAsync();
        (await ReloadAsync<Subscription>(subscription.Id))!.BillingLockedUntil!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddDays(6));

        await UpdatePaymentAsync(proration.TradeNo, p => p.ExpireTime = DateTime.UtcNow.AddMinutes(-1));
        (await InScopeAsync<IPaymentService, Result<int>>(svc => svc.CloseExpiredPaymentsAsync())).Data.ShouldBe(1);

        (await ReloadAsync<SubscriptionChange>(change.Data.Id))!.Status.ShouldBe(SubscriptionChangeStatus.Cancelled);
        var released = await ReloadAsync<Subscription>(subscription.Id);
        released!.BillingLockedUntil.ShouldBeNull();
        released.Status.ShouldBe(SubscriptionStatus.Active);
    }

    /// <summary>渠道返回失败、请求是否到达渠道无从得知：同样不降级、不再扣，只是等得短（收银台的默认有效期）。</summary>
    [Fact]
    public async Task AChargeOfUnknownOutcome_IsNeitherDowngradedNorChargedAgain()
    {
        ChannelAnswers(Result.Failure<PaymentProviderChargeResult>("upstream timeout", 502));
        var subscription = await SeedDueAsync("SUB-FLIGHT-4");

        await RenewAsync();

        var held = await ReloadAsync<Subscription>(subscription.Id);
        held!.Status.ShouldBe(SubscriptionStatus.Active);
        held.RenewalRetryCount.ShouldBe(0);
        held.BillingLockedUntil!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(30));
        held.BillingLockedUntil.Value.ShouldBeLessThan(DateTime.UtcNow.AddHours(2));

        await UpdateSubscriptionAsync(subscription.Id, s => s.BillingLockedUntil = DateTime.UtcNow.AddMinutes(-1));
        await RenewAsync();

        (await PaymentsOfAsync("SUB-FLIGHT-4")).Count.ShouldBe(1);
    }
}
