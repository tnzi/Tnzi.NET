namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 续费扫描取到的每一条订阅都必须离开「带着计费锁停在原地」：推进一个周期，或降级 PastDue。
/// </summary>
/// <remarks>
/// ★ 扫描按 <c>NextBillingTime</c> 升序取前一页。支付侧在建单之前就拒绝的扣款不产生任何支付事件，
/// 状态机拿不到回流：订阅既不推进也不降级，下一轮还排在队首，攒满一页后全体续费停摆，而扫描照样报「已处理」。
/// </remarks>
public class SubscriptionRenewalNeverStallsTests : SubscriptionsIntegrationTestBase
{
    private bool _taxFails;

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // 后注册的赢：按开关决定计税成败，模拟计税服务不可用 / 配置缺失。
        services.AddScoped<IPaymentTaxCalculator>(sp => _taxFails
            ? new FailingTaxCalculator()
            : ActivatorUtilities.CreateInstance<DefaultPaymentTaxCalculator>(sp));

        // 一个建了单才拒付的渠道：失败经 ApplyFailedAsync 落库并发出 PaymentFailedEvent。
        var declining = new Mock<IPaymentProvider>();
        declining.SetupGet(p => p.ChannelCode).Returns(DecliningChannel);
        declining.SetupGet(p => p.ChannelName).Returns(DecliningChannel);
        declining.SetupGet(p => p.SupportsOffSessionCharge).Returns(true);
        declining.Setup(p => p.ChargeOffSessionAsync(It.IsAny<PaymentProviderChargeDto>()))
            .ReturnsAsync(Result<PaymentProviderChargeResult>.Failure("card_declined"));
        services.AddScoped(_ => declining.Object);
        services.Configure<PaymentOptions>(o => o.Channels[DecliningChannel] = new ChannelOptions { Enabled = true, Currency = "USD" });
    }

    private const string DecliningChannel = "Declining";

    private async Task<(Subscription Subscription, SubscriptionPlan Plan)> SeedDueRenewalAsync(string subscriptionNo, decimal price, string channelCode = "Null")
    {
        var plan = new SubscriptionPlan
        {
            PlanName = "Plan",
            Price = price,
            Currency = "USD",
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            IsActive = true
        };
        await SeedAsync(plan);

        var subscription = new Subscription
        {
            SubscriptionNo = subscriptionNo,
            UserId = Guid.NewGuid(),
            PlanId = plan.Id,
            Status = SubscriptionStatus.Active,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            StartTime = DateTime.UtcNow.AddMonths(-1),
            NextBillingTime = DateTime.UtcNow.AddDays(-1),
            OriginalPrice = price,
            Currency = "USD",
            AutoRenew = true,
            ChannelCode = channelCode,
            PaymentMethodToken = "pm_test",
            ProviderCustomerId = "cus_test"
        };
        await SeedAsync(subscription);
        return (subscription, plan);
    }

    private Task<Result<int>> RenewAsync() =>
        InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.RenewExpiredSubscriptionsAsync());

    private async Task<int> CountPaymentsAsync(string businessOrderNo)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<PaymentEntity, Guid>>();
        return await repo.AsQueryable().CountAsync(p => p.BusinessOrderNo == businessOrderNo);
    }

    /// <summary>★ 0 元计划到期：不扣款，直接推进一个周期、解锁，而不是停在到期那一刻被每轮重扫。</summary>
    [Fact]
    public async Task AZeroPricePlan_RenewsWithoutACharge()
    {
        var (subscription, _) = await SeedDueRenewalAsync("SUB-FREE", 0m);

        var result = await RenewAsync();

        result.Succeeded.ShouldBeTrue(result.Message);
        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.Active);
        reloaded.NextBillingTime!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddDays(20));
        reloaded.BillingLockedUntil.ShouldBeNull();
        reloaded.RenewalRetryCount.ShouldBe(0);
        (await CountPaymentsAsync("SUB-FREE")).ShouldBe(0, "a zero-price renewal creates no payment");
    }

    /// <summary>★ 计税失败 = 建单之前就被拒绝、没有事件回流：在扣款处当场降级 PastDue 并解锁，交给宽限期与重试上限。</summary>
    [Fact]
    public async Task AChargeRejectedBeforeAPaymentExists_DowngradesToPastDue()
    {
        _taxFails = true;
        var (subscription, _) = await SeedDueRenewalAsync("SUB-TAXFAIL", 30m);

        await RenewAsync();

        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.PastDue);
        reloaded.RenewalRetryCount.ShouldBe(1);
        reloaded.PastDueSince.ShouldNotBeNull();
        reloaded.BillingLockedUntil.ShouldBeNull();
        (await CountPaymentsAsync("SUB-TAXFAIL")).ShouldBe(0);
    }

    /// <summary>
    /// 建单之后的失败（渠道拒付）只经支付失败事件收口一次：重试计数加 1，不因扣款处再收口一次而加 2。
    /// </summary>
    [Fact]
    public async Task AChargeDeclinedAfterThePaymentExists_IsCountedOnce()
    {
        var (subscription, _) = await SeedDueRenewalAsync("SUB-DECLINE", 30m, DecliningChannel);

        await RenewAsync();

        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.PastDue);
        reloaded.RenewalRetryCount.ShouldBe(1);
        (await CountPaymentsAsync("SUB-DECLINE")).ShouldBe(1);
    }

    private sealed class FailingTaxCalculator : IPaymentTaxCalculator
    {
        public Task<Result<TaxCalculationResult>> CalculateAsync(TaxCalculationRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<TaxCalculationResult>.Failure("Tax service unavailable.", 503));
    }
}
