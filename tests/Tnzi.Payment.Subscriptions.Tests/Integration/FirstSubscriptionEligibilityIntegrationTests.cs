namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 「能不能用首次订阅优惠」的预检与真正的核销守卫必须回答同一个问题。
/// </summary>
/// <remarks>
/// <para>
/// ★★ 此前是两份判据：预检（<c>GET /promotions/first-subscription-check</c>）查的是
/// 「这个用户<b>用没用过</b>标了 FirstSubscriptionOnly 的券」，而核销守卫（<c>ValidateCouponAsync</c>）
/// 查的是「这个用户<b>有没有订阅记录</b>」。老订户没用过首单券 → 预检说能用、收银台展示折扣、
/// 输码却被 400 拒绝；反过来用过一张首单券但订阅从没建成的人 → 预检把入口藏掉，而核销本会放行。
/// </para>
/// <para>
/// 这个测试项目是唯一同时引用折扣包与续费包的地方（见 <c>SubscriptionSeamRedLineTests</c>），
/// 也就是唯一能把探针真的接上、拿两条路径互相对账的地方。
/// </para>
/// </remarks>
public class FirstSubscriptionEligibilityIntegrationTests : SubscriptionsIntegrationTestBase
{
    private const string FirstOrderCode = "FIRST20";

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        // 基类只按父模块的 ICouponService 注册了 CouponService；预检端点走的是券包契约。
        services.AddScoped<ICouponWalletService>(sp => (CouponService)sp.GetRequiredService<ICouponService>());
    }

    /// <summary>★★ 老订户：预检为 false，核销拒绝 —— 两边一致，收银台不会先承诺再拒绝。</summary>
    [Fact]
    public async Task AnExistingSubscriber_IsRefusedByBothThePrecheckAndTheValidator()
    {
        var userId = Guid.NewGuid();
        var plan = await NewPlanAsync();
        await SeedAsync(SubscriptionFor(userId, plan));
        await SeedFirstOrderPromotionAsync();

        var precheck = await InScopeAsync<ICouponWalletService, Result<bool>>(s => s.CanUseFirstSubscriptionDiscountAsync(userId));
        var validation = await InScopeAsync<IPromotionService, Result<CouponValidationResultDto>>(s => s.ValidateCouponAsync(Context(userId)));

        precheck.Succeeded.ShouldBeTrue(precheck.Message);
        precheck.Data.ShouldBeFalse("老订户的预检答了「可用」，而核销会以 COUPON_FIRST_SUBSCRIPTION_ONLY 拒绝他");
        validation.Data!.IsValid.ShouldBeFalse();
        validation.Data.ErrorMessage.ShouldBe(ErrorCodes.CouponFirstSubscriptionOnly);
    }

    /// <summary>
    /// ★ 用过一张首单券、但订阅从没建成的用户：核销会放行，预检也必须答「可用」——
    /// 此前预检按「用过首单券」把入口藏掉，一次本可以成交的折扣被界面自己吞了。
    /// </summary>
    [Fact]
    public async Task ACustomerWithoutAnySubscription_IsAcceptedByBoth_EvenAfterUsingAnotherFirstOrderCoupon()
    {
        var userId = Guid.NewGuid();
        var earlier = await SeedFirstOrderPromotionAsync(code: "WELCOME");
        await SeedAsync(new CouponUsage
        {
            CouponId = earlier.Id,
            UserId = userId,
            BusinessOrderNo = "ORD-OLD",
            DiscountAmount = 20m
        });
        await SeedFirstOrderPromotionAsync();

        var precheck = await InScopeAsync<ICouponWalletService, Result<bool>>(s => s.CanUseFirstSubscriptionDiscountAsync(userId));
        var validation = await InScopeAsync<IPromotionService, Result<CouponValidationResultDto>>(s => s.ValidateCouponAsync(Context(userId)));

        validation.Data!.IsValid.ShouldBeTrue(validation.Data.ErrorMessage);
        precheck.Data.ShouldBeTrue("核销会放行，预检却把首单折扣的入口藏掉了");
    }

    /// <summary>
    /// ★★ 新用户第一次订阅、输入首单券：经开通链路真的核销得掉。
    /// 此前订阅先落库再建首期支付，核销时的探针看到的正是刚插入的这条 Pending 订阅，
    /// 于是每个新用户都被 400 <c>COUPON_FIRST_SUBSCRIPTION_ONLY</c> 拒绝、订阅被删，而预检与试算都答「可用」。
    /// </summary>
    [Fact]
    public async Task ANewCustomer_CanSubscribeWithAFirstOrderCoupon()
    {
        var plan = await NewPlanAsync();
        await SeedFirstOrderPromotionAsync();

        var created = await InScopeAsync<ISubscriptionService, Result<SubscriptionCreateResultDto>>(s => s.CreateSubscriptionAsync(new CreateSubscriptionDto
        {
            PlanId = plan.Id,
            ChannelCode = "Null",
            CouponCode = FirstOrderCode
        }));

        created.Succeeded.ShouldBeTrue(created.Message);
        created.Data!.Payment!.DiscountAmount.ShouldBe(6m);
        created.Data.Payment.Amount.ShouldBe(24m);
        created.Data.Subscription.Status.ShouldBe(SubscriptionStatus.Pending);
    }

    /// <summary>
    /// 只留下过一条从没开始的订阅（开通后没付首期就取消了）的人不是老订户：他从没成为过订户。
    /// </summary>
    [Fact]
    public async Task ACustomerWhoseOnlySubscriptionNeverStarted_IsAcceptedByBoth()
    {
        var userId = Guid.NewGuid();
        var plan = await NewPlanAsync();
        var abandoned = SubscriptionFor(userId, plan);
        abandoned.Status = SubscriptionStatus.Cancelled;
        abandoned.NextBillingTime = null;
        abandoned.CancelTime = DateTime.UtcNow.AddDays(-1);
        await SeedAsync(abandoned);
        await SeedFirstOrderPromotionAsync();

        var precheck = await InScopeAsync<ICouponWalletService, Result<bool>>(s => s.CanUseFirstSubscriptionDiscountAsync(userId));
        var validation = await InScopeAsync<IPromotionService, Result<CouponValidationResultDto>>(s => s.ValidateCouponAsync(Context(userId)));

        validation.Data!.IsValid.ShouldBeTrue(validation.Data.ErrorMessage);
        precheck.Data.ShouldBeTrue();
    }

    /// <summary>退订过再回来的人不能再拿一次首单折扣：付过款的已取消订阅照样算历史。</summary>
    [Fact]
    public async Task AFormerSubscriberWhoPaid_IsStillRefused()
    {
        var userId = Guid.NewGuid();
        var plan = await NewPlanAsync();
        var former = SubscriptionFor(userId, plan);
        former.Status = SubscriptionStatus.Cancelled;
        former.LastBillingTradeNo = "PAY-PAST-1";
        former.PaidAmount = 30m;
        former.CancelTime = DateTime.UtcNow.AddDays(-1);
        await SeedAsync(former);
        await SeedFirstOrderPromotionAsync();

        var precheck = await InScopeAsync<ICouponWalletService, Result<bool>>(s => s.CanUseFirstSubscriptionDiscountAsync(userId));
        var validation = await InScopeAsync<IPromotionService, Result<CouponValidationResultDto>>(s => s.ValidateCouponAsync(Context(userId)));

        validation.Data!.IsValid.ShouldBeFalse();
        validation.Data.ErrorMessage.ShouldBe(ErrorCodes.CouponFirstSubscriptionOnly);
        precheck.Data.ShouldBeFalse();
    }

    /// <summary>开过试用也是订户：试用结束后取消的人同样不再是新客。</summary>
    [Fact]
    public async Task AFormerTrialUser_IsStillRefused()
    {
        var userId = Guid.NewGuid();
        var plan = await NewPlanAsync();
        var former = SubscriptionFor(userId, plan);
        former.Status = SubscriptionStatus.Expired;
        former.TrialStartTime = DateTime.UtcNow.AddDays(-30);
        former.TrialEndTime = DateTime.UtcNow.AddDays(-16);
        await SeedAsync(former);
        await SeedFirstOrderPromotionAsync();

        var validation = await InScopeAsync<IPromotionService, Result<CouponValidationResultDto>>(s => s.ValidateCouponAsync(Context(userId)));

        validation.Data!.IsValid.ShouldBeFalse();
        validation.Data.ErrorMessage.ShouldBe(ErrorCodes.CouponFirstSubscriptionOnly);
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    private async Task<SubscriptionPlan> NewPlanAsync()
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
        return plan;
    }

    private static Subscription SubscriptionFor(Guid userId, SubscriptionPlan plan) => new()
    {
        SubscriptionNo = $"SUB{Guid.NewGuid():N}",
        UserId = userId,
        PlanId = plan.Id,
        Status = SubscriptionStatus.Active,
        CycleType = BillingCycleType.Month,
        CycleValue = 1,
        StartTime = DateTime.UtcNow.AddMonths(-1),
        NextBillingTime = DateTime.UtcNow.AddDays(5),
        OriginalPrice = 30m,
        Currency = "USD",
        ChannelCode = PaymentConstants.OfflineChannelCode
    };

    private async Task<Promotion> SeedFirstOrderPromotionAsync(string code = FirstOrderCode)
    {
        var promotion = new Promotion
        {
            PromotionCode = code,
            Name = "First order",
            IsActive = true,
            IsPublic = true,
            StartTime = DateTime.UtcNow.AddDays(-1),
            DiscountType = DiscountType.Percentage,
            DiscountValue = 20m,
            Currency = "USD",
            ProductType = ProductType.All,
            ApplyScope = ApplyScope.Global,
            FirstSubscriptionOnly = true
        };
        await SeedAsync(promotion);
        return promotion;
    }

    private static CouponApplyContext Context(Guid userId) => new()
    {
        CouponCode = FirstOrderCode,
        UserId = userId,
        BusinessOrderNo = $"ORD-{Guid.NewGuid():N}",
        OrderAmount = 100m,
        Currency = "USD",
        ProductType = ProductType.All
    };
}
