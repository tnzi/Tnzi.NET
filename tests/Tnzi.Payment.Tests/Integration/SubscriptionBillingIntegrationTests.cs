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

    private async Task<PaymentEntity?> ReloadPaymentByOrderNoAsync(string businessOrderNo)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<PaymentEntity, Guid>>();
        return await repo.FirstOrDefaultAsync(p => p.BusinessOrderNo == businessOrderNo);
    }
}
