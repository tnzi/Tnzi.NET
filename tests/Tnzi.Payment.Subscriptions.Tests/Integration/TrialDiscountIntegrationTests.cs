namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 试用折扣（<c>SubscriptionPlan.TrialDiscount</c>，开通试用时快照进 <c>Subscription.DiscountAmount</c>）
/// 必须在转正扣款上真的生效。
/// </summary>
/// <remarks>
/// 此前转正扫描与 PastDue 重试都按 <c>plan.Price</c> 全额扣款；而回流侧的金额下界却是
/// <c>listPrice − DiscountAmount</c>，全额付款高于下界照常通过 —— 框架知道该打折，只是扣款侧从没用过它。
/// 每一次带折扣的转正都多收一个折扣额，而订阅 DTO 还向用户回显 <c>discountAmount</c>。
/// 折扣抵满全价时不能再去调渠道（渠道拒绝 0 元单，订阅会卡在 Trial 被无限重扫），直接免费转正。
/// </remarks>
public class TrialDiscountIntegrationTests : SubscriptionsIntegrationTestBase
{
    private async Task<SubscriptionPlan> SeedPlanAsync(string code, decimal price, decimal? trialDiscount = null)
    {
        var plan = new SubscriptionPlan
        {
            PlanCode = code,
            PlanName = code,
            ProductCode = "SUITE",
            Price = price,
            Currency = "USD",
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            IsActive = true,
            AllowTrial = true,
            TrialDiscount = trialDiscount
        };
        await SeedAsync(plan);
        return plan;
    }

    private async Task<Subscription> SeedDueTrialAsync(SubscriptionPlan plan, string subscriptionNo, decimal discountAmount, string? paymentMethodToken = "pm_test")
    {
        var trialEnd = DateTime.UtcNow.AddHours(-1);
        var subscription = new Subscription
        {
            SubscriptionNo = subscriptionNo,
            UserId = TestHelper.DefaultTestUserId,
            PlanId = plan.Id,
            ProductCode = plan.ProductCode,
            Status = SubscriptionStatus.Trial,
            CycleType = plan.CycleType,
            CycleValue = plan.CycleValue,
            StartTime = DateTime.UtcNow.AddDays(-14),
            TrialStartTime = DateTime.UtcNow.AddDays(-14),
            TrialEndTime = trialEnd,
            NextBillingTime = trialEnd,
            OriginalPrice = plan.Price,
            DiscountAmount = discountAmount,
            Currency = plan.Currency,
            AutoRenew = true,
            ChannelCode = "Null",
            PaymentMethodToken = paymentMethodToken,
            ProviderCustomerId = "cus_test"
        };
        await SeedAsync(subscription);
        return subscription;
    }

    private Task<Result<int>> ConvertTrialsAsync() =>
        InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ConvertDueTrialsAsync());

    private async Task<PaymentEntity?> ReloadPaymentByOrderNoAsync(string businessOrderNo)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<PaymentEntity, Guid>>();
        return await repo.FirstOrDefaultAsync(p => p.BusinessOrderNo == businessOrderNo);
    }

    private async Task BindPaymentMethodAsync(Guid subscriptionId, string token)
    {
        using var scope = ServiceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
        var entity = ctx.Set<Subscription>().First(s => s.Id == subscriptionId);
        entity.PaymentMethodToken = token;
        entity.BillingLockedUntil = null;
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task TrialConversion_ChargesThePlanPriceMinusTheTrialDiscount()
    {
        var plan = await SeedPlanAsync("PRO-TD1", 30m, trialDiscount: 5m);
        var sub = await SeedDueTrialAsync(plan, "SUB-TD1", discountAmount: 5m);

        var converted = await ConvertTrialsAsync();

        converted.Succeeded.ShouldBeTrue();
        converted.Data.ShouldBe(1);

        var payment = await ReloadPaymentByOrderNoAsync("SUB-TD1");
        payment.ShouldNotBeNull();
        payment!.Status.ShouldBe(PaymentStatus.Succeeded);
        payment.PayableAmount.ShouldBe(25m);

        var reloaded = await ReloadAsync<Subscription>(sub.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.Active);
        reloaded.PaidAmount.ShouldBe(25m);
    }

    [Fact]
    public async Task TrialConversion_WithAFullTrialDiscount_ActivatesWithoutACharge()
    {
        var plan = await SeedPlanAsync("PRO-TD2", 30m, trialDiscount: 30m);
        var sub = await SeedDueTrialAsync(plan, "SUB-TD2", discountAmount: 30m);

        var converted = await ConvertTrialsAsync();

        converted.Succeeded.ShouldBeTrue();
        converted.Data.ShouldBe(1);

        // 没有支付单：渠道拒绝 0 元单，绕过它直接转正。
        (await ReloadPaymentByOrderNoAsync("SUB-TD2")).ShouldBeNull();

        var reloaded = await ReloadAsync<Subscription>(sub.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.Active);
        reloaded.TrialConvertedTime.ShouldNotBeNull();
        reloaded.PaidAmount.ShouldBe(0m);
        reloaded.NextBillingTime!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddDays(20));
        reloaded.BillingLockedUntil.ShouldBeNull();
        reloaded.RenewalRetryCount.ShouldBe(0);
    }

    [Fact]
    public async Task TrialConversion_WithAFullTrialDiscount_IsNotRescannedAsDue()
    {
        var plan = await SeedPlanAsync("PRO-TD3", 30m, trialDiscount: 30m);
        await SeedDueTrialAsync(plan, "SUB-TD3", discountAmount: 30m);

        (await ConvertTrialsAsync()).Data.ShouldBe(1);
        (await ConvertTrialsAsync()).Data.ShouldBe(0);
    }

    [Fact]
    public async Task RetryBilling_ForAPastDueTrial_AppliesTheTrialDiscount()
    {
        var plan = await SeedPlanAsync("PRO-TD4", 30m, trialDiscount: 5m);
        var sub = await SeedDueTrialAsync(plan, "SUB-TD4", discountAmount: 5m, paymentMethodToken: null);

        await ConvertTrialsAsync();
        (await ReloadAsync<Subscription>(sub.Id))!.Status.ShouldBe(SubscriptionStatus.PastDue);

        await BindPaymentMethodAsync(sub.Id, "pm_recovered");
        var retried = await InScopeAsync<ISubscriptionService, Result>(svc => svc.RetryBillingAsync(sub.Id));
        retried.Succeeded.ShouldBeTrue();

        var payment = await ReloadPaymentByOrderNoAsync("SUB-TD4");
        payment.ShouldNotBeNull();
        payment!.PayableAmount.ShouldBe(25m);

        var reloaded = await ReloadAsync<Subscription>(sub.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.Active);
        reloaded.PaidAmount.ShouldBe(25m);
    }

    [Fact]
    public async Task RetryBilling_ForAPastDueTrialWhoseDiscountCoversThePrice_ConvertsWithoutACard()
    {
        // 修复之前落到 PastDue 的试用（当时按全价扣、没绑卡就失败），折扣其实抵满全价：
        // 转正一分钱都不收，却被「没有支付方式」的守卫挡在门外，只能等宽限期把它过期。
        var plan = await SeedPlanAsync("PRO-TD6", 30m, trialDiscount: 30m);
        var sub = await SeedDueTrialAsync(plan, "SUB-TD6", discountAmount: 30m, paymentMethodToken: null);
        using (var scope = ServiceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
            var entity = ctx.Set<Subscription>().First(s => s.Id == sub.Id);
            entity.Status = SubscriptionStatus.PastDue;
            entity.PastDueSince = DateTime.UtcNow.AddHours(-1);
            entity.RenewalRetryCount = 1;
            await ctx.SaveChangesAsync();
        }

        var retried = await InScopeAsync<ISubscriptionService, Result>(svc => svc.RetryBillingAsync(sub.Id));

        retried.Succeeded.ShouldBeTrue();
        (await ReloadPaymentByOrderNoAsync("SUB-TD6")).ShouldBeNull();
        var reloaded = await ReloadAsync<Subscription>(sub.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.Active);
        reloaded.TrialConvertedTime.ShouldNotBeNull();
        reloaded.PaidAmount.ShouldBe(0m);
    }

    [Fact]
    public async Task RetryBilling_ForAPastDueTrialThatStillOwesMoney_StillDemandsACard()
    {
        var plan = await SeedPlanAsync("PRO-TD7", 30m, trialDiscount: 5m);
        var sub = await SeedDueTrialAsync(plan, "SUB-TD7", discountAmount: 5m, paymentMethodToken: null);
        await ConvertTrialsAsync();
        (await ReloadAsync<Subscription>(sub.Id))!.Status.ShouldBe(SubscriptionStatus.PastDue);

        var retried = await InScopeAsync<ISubscriptionService, Result>(svc => svc.RetryBillingAsync(sub.Id));

        retried.Succeeded.ShouldBeFalse();
        retried.Message.ShouldBe(ErrorCodes.SubscriptionPaymentMethodMissing);
    }

    [Fact]
    public async Task TrialConversion_AfterADuePlanChange_StillAppliesTheDiscount()
    {
        // 折扣是给这次试用的承诺，不是给旧计划的：到期变更换了计划，折扣照旧从新计划的价格上减。
        var pro = await SeedPlanAsync("PRO-TD5", 30m, trialDiscount: 5m);
        var basic = await SeedPlanAsync("BASIC-TD5", 10m);
        var sub = await SeedDueTrialAsync(pro, "SUB-TD5", discountAmount: 5m);

        var change = await InScopeAsync<ISubscriptionService, Result<SubscriptionChangeDto>>(
            svc => svc.ChangeSubscriptionPlanAsync(sub.Id, new ChangeSubscriptionPlanDto { NewPlanId = basic.Id, EffectiveImmediately = false }));
        change.Succeeded.ShouldBeTrue();
        change.Data!.Status.ShouldBe(SubscriptionChangeStatus.Pending);

        (await ConvertTrialsAsync()).Data.ShouldBe(1);

        var reloaded = await ReloadAsync<Subscription>(sub.Id);
        reloaded!.PlanId.ShouldBe(basic.Id);
        reloaded.Status.ShouldBe(SubscriptionStatus.Active);
        reloaded.PaidAmount.ShouldBe(5m);

        var payment = await ReloadPaymentByOrderNoAsync("SUB-TD5");
        payment!.PayableAmount.ShouldBe(5m);
    }

    [Fact]
    public async Task CreatePlan_RefusesATrialDiscountAbovePrice()
    {
        var created = await InScopeAsync<ISubscriptionService, Result<SubscriptionPlanDto>>(
            svc => svc.CreatePlanAsync(new SubscriptionPlanDto
            {
                PlanCode = "PRO-TD6",
                PlanName = "Pro",
                ProductCode = "SUITE",
                Price = 30m,
                Currency = "USD",
                CycleType = BillingCycleType.Month,
                CycleValue = 1,
                IsActive = true,
                AllowTrial = true,
                TrialDiscount = 31m
            }));

        created.Succeeded.ShouldBeFalse();
        created.Code.ShouldBe(400);
    }

    [Fact]
    public async Task UpdatePlan_RefusesANegativeTrialDiscount()
    {
        var plan = await SeedPlanAsync("PRO-TD7", 30m);

        var updated = await InScopeAsync<ISubscriptionService, Result>(
            svc => svc.UpdatePlanAsync(plan.Id, new SubscriptionPlanDto
            {
                PlanCode = plan.PlanCode,
                PlanName = plan.PlanName,
                ProductCode = plan.ProductCode,
                Price = 30m,
                Currency = "USD",
                CycleType = BillingCycleType.Month,
                CycleValue = 1,
                IsActive = true,
                AllowTrial = true,
                TrialDiscount = -1m
            }));

        updated.Succeeded.ShouldBeFalse();
        updated.Code.ShouldBe(400);
        (await ReloadAsync<SubscriptionPlan>(plan.Id))!.TrialDiscount.ShouldBeNull();
    }
}
