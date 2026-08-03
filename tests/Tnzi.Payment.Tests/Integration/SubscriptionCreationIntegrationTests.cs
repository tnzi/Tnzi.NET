using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Entities;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Services;
using Tnzi.Results;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 订阅开通集成测试：首期支付凭据回传、优惠券贯通、绑卡一步完成。
/// </summary>
public class SubscriptionCreationIntegrationTests : PaymentIntegrationTestBase
{
    private async Task<SubscriptionPlan> SeedPlanAsync(bool allowTrial = false, decimal price = 100m)
    {
        var plan = new SubscriptionPlan
        {
            PlanCode = $"P{Guid.NewGuid():N}"[..12],
            PlanName = "Pro",
            Price = price,
            Currency = "USD",
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            AllowTrial = allowTrial,
            TrialDays = allowTrial ? 14 : 0,
            IsActive = true
        };
        await SeedAsync(plan);
        return plan;
    }

    private Task<Result<SubscriptionCreateResultDto>> CreateAsync(CreateSubscriptionDto request) =>
        InScopeAsync<ISubscriptionService, Result<SubscriptionCreateResultDto>>(
            svc => svc.CreateSubscriptionAsync(request));

    /// <summary>
    /// 首期支付凭据必须随订阅一起返回，前端才能直接拉起收银台。
    /// 此前只返回订阅本体，凭据被丢弃，前端只能靠业务单号反查支付列表。
    /// </summary>
    [Fact]
    public async Task CreateSubscription_ReturnsFirstPaymentCredentials()
    {
        var plan = await SeedPlanAsync();

        var created = await CreateAsync(new CreateSubscriptionDto { PlanId = plan.Id, ChannelCode = "Null" });

        created.Succeeded.ShouldBeTrue();
        created.Data!.RequiresPayment.ShouldBeTrue();
        created.Data.Payment!.TradeNo.ShouldNotBeNullOrWhiteSpace();
        created.Data.Payment.PayParams.ShouldNotBeNullOrWhiteSpace();
        created.Data.Payment.Amount.ShouldBe(100m);
        created.Data.Subscription.Status.ShouldBe(SubscriptionStatus.Pending);
    }

    /// <summary>
    /// 限定到某个计划的促销必须在订阅开通链路全程通过：
    /// 试算与核销用的是两个不同的调用点，范围参数漏传会造成"验得过、核销不掉"。
    /// </summary>
    [Fact]
    public async Task CreateSubscription_WithPlanScopedCoupon_AppliesThroughToPayment()
    {
        var plan = await SeedPlanAsync();
        await SeedAsync(new Promotion
        {
            PromotionCode = "PLANONLY",
            Name = "Plan scoped",
            IsActive = true,
            IsPublic = true,
            StartTime = DateTime.UtcNow.AddDays(-1),
            DiscountType = DiscountType.Fixed,
            DiscountValue = 25m,
            Currency = "USD",
            Stackable = true,
            ProductType = ProductType.Subscription,
            ApplyScope = ApplyScope.Plan,
            ScopeIdsJson = JsonSerializer.Serialize(new List<Guid> { plan.Id }),
            UsedCount = 0
        });

        var created = await CreateAsync(new CreateSubscriptionDto
        {
            PlanId = plan.Id,
            ChannelCode = "Null",
            CouponCode = "PLANONLY"
        });

        created.Succeeded.ShouldBeTrue();
        created.Data!.Payment!.DiscountAmount.ShouldBe(25m);
        created.Data.Payment.Amount.ShouldBe(75m);
        created.Data.Subscription.DiscountAmount.ShouldBe(25m);

        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<Tnzi.Domain.Repositories.IRepository<PaymentEntity, Guid>>();
        var payment = await repo.FirstOrDefaultAsync(p => p.TradeNo == created.Data.Payment.TradeNo);
        payment!.PayableAmount.ShouldBe(75m);
        payment.CouponId.ShouldNotBeNull();
    }

    /// <summary>
    /// 范围不符的促销必须在开通时就被拒绝，而不是先建订阅再失败。
    /// </summary>
    [Fact]
    public async Task CreateSubscription_WithCouponScopedToAnotherPlan_IsRejected()
    {
        var plan = await SeedPlanAsync();
        await SeedAsync(new Promotion
        {
            PromotionCode = "OTHERPLAN",
            Name = "Other plan only",
            IsActive = true,
            IsPublic = true,
            StartTime = DateTime.UtcNow.AddDays(-1),
            DiscountType = DiscountType.Fixed,
            DiscountValue = 25m,
            Currency = "USD",
            Stackable = true,
            ApplyScope = ApplyScope.Plan,
            ScopeIdsJson = JsonSerializer.Serialize(new List<Guid> { Guid.NewGuid() }),
            UsedCount = 0
        });

        var created = await CreateAsync(new CreateSubscriptionDto
        {
            PlanId = plan.Id,
            ChannelCode = "Null",
            CouponCode = "OTHERPLAN"
        });

        created.Succeeded.ShouldBeFalse();
        created.Message.ShouldBe(ErrorCodes.CouponScopeMismatch);
    }

    /// <summary>
    /// 试用开通不产生首期支付，优惠券无处核销 —— 必须明确拒绝而不是静默忽略用户输入。
    /// </summary>
    [Fact]
    public async Task CreateSubscription_TrialWithCoupon_IsRejectedInsteadOfSilentlyDropped()
    {
        var plan = await SeedPlanAsync(allowTrial: true);
        await SeedAsync(new Promotion
        {
            PromotionCode = "TRIALCOUPON",
            Name = "Any",
            IsActive = true,
            IsPublic = true,
            StartTime = DateTime.UtcNow.AddDays(-1),
            DiscountType = DiscountType.Fixed,
            DiscountValue = 10m,
            Currency = "USD",
            Stackable = true,
            UsedCount = 0
        });

        var created = await CreateAsync(new CreateSubscriptionDto
        {
            PlanId = plan.Id,
            ChannelCode = "Null",
            EnableTrial = true,
            CouponCode = "TRIALCOUPON"
        });

        created.Succeeded.ShouldBeFalse();
        created.Message.ShouldBe(ErrorCodes.CouponNotApplicableToTrial);
    }

    /// <summary>
    /// 试用开通不产生支付单，返回结果里也就没有支付凭据。
    /// </summary>
    [Fact]
    public async Task CreateSubscription_Trial_RequiresNoPayment()
    {
        var plan = await SeedPlanAsync(allowTrial: true);

        var created = await CreateAsync(new CreateSubscriptionDto
        {
            PlanId = plan.Id,
            ChannelCode = "Null",
            EnableTrial = true
        });

        created.Succeeded.ShouldBeTrue();
        created.Data!.RequiresPayment.ShouldBeFalse();
        created.Data.Subscription.Status.ShouldBe(SubscriptionStatus.Trial);
    }

    /// <summary>
    /// 开通时传渠道 token 即一步完成绑卡，订阅立刻具备自动续费能力。
    /// </summary>
    [Fact]
    public async Task CreateSubscription_WithPaymentMethodToken_BindsAndIsRenewable()
    {
        var plan = await SeedPlanAsync();

        var created = await CreateAsync(new CreateSubscriptionDto
        {
            PlanId = plan.Id,
            ChannelCode = "Null",
            PaymentMethodToken = "pm_signup"
        });

        created.Succeeded.ShouldBeTrue();
        created.Data!.Subscription.HasPaymentMethod.ShouldBeTrue();

        var reloaded = await ReloadAsync<Subscription>(created.Data.Subscription.Id);
        reloaded!.PaymentMethodToken.ShouldBe("pm_signup");
        reloaded.StoredPaymentMethodId.ShouldNotBeNull();
    }

    /// <summary>
    /// 同一产品下只能有一条有效订阅；不同产品可以并存。
    /// </summary>
    [Fact]
    public async Task CreateSubscription_SameProductTwice_IsRejected_ButOtherProductIsAllowed()
    {
        var planA = await SeedPlanAsync();
        planA.ProductCode = "crm";
        var planB = await SeedPlanAsync();
        planB.ProductCode = "helpdesk";

        using (var scope = ServiceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PaymentTestDbContext>();
            foreach (var p in ctx.Set<SubscriptionPlan>().Where(p => p.Id == planA.Id || p.Id == planB.Id))
                p.ProductCode = p.Id == planA.Id ? "crm" : "helpdesk";
            await ctx.SaveChangesAsync();
        }

        (await CreateAsync(new CreateSubscriptionDto { PlanId = planA.Id, ChannelCode = "Null" }))
            .Succeeded.ShouldBeTrue();

        var duplicate = await CreateAsync(new CreateSubscriptionDto { PlanId = planA.Id, ChannelCode = "Null" });
        duplicate.Succeeded.ShouldBeFalse();
        duplicate.Message.ShouldBe(ErrorCodes.SubscriptionProductAlreadySubscribed);

        // 另一个产品不受影响
        (await CreateAsync(new CreateSubscriptionDto { PlanId = planB.Id, ChannelCode = "Null" }))
            .Succeeded.ShouldBeTrue();
    }
}
