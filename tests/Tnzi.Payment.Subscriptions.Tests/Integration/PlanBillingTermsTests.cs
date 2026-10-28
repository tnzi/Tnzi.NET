namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 计划的计费条款（币种、周期）与存量订阅的快照必须对得上，否则续费扣的是错价。
/// </summary>
/// <remarks>
/// 续费按计划的现价扣，币种与周期却取订阅开通时的快照。管理员把一个有存量订户的计划从 10 USD / 月
/// 改成 1500 JPY / 月，下一次续费扣的是 1500 USD；改成 100 / 年，月付订户每个月被扣 100。
/// 金额下界取「快照价与计划现价的较低者」，两种错价都拦不住。
/// 改价本身（同币种同周期）照旧允许，续费按新价收：那是有意的设计。
/// </remarks>
public class PlanBillingTermsTests : SubscriptionsIntegrationTestBase
{
    private async Task<SubscriptionPlan> SeedPlanAsync(string code, decimal price = 10m, BillingCycleType cycleType = BillingCycleType.Month, int cycleValue = 1)
    {
        var plan = new SubscriptionPlan
        {
            PlanCode = code,
            PlanName = code,
            Price = price,
            Currency = "USD",
            CycleType = cycleType,
            CycleValue = cycleValue,
            IsActive = true
        };
        await SeedAsync(plan);
        return plan;
    }

    private async Task<Subscription> SeedSubscriptionAsync(SubscriptionPlan plan, string subscriptionNo, SubscriptionStatus status = SubscriptionStatus.Active, bool due = false)
    {
        var subscription = new Subscription
        {
            SubscriptionNo = subscriptionNo,
            UserId = TestHelper.DefaultTestUserId,
            PlanId = plan.Id,
            Status = status,
            CycleType = plan.CycleType,
            CycleValue = plan.CycleValue,
            StartTime = DateTime.UtcNow.AddMonths(-1),
            NextBillingTime = due ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddDays(10),
            OriginalPrice = plan.Price,
            PaidAmount = plan.Price,
            Currency = plan.Currency,
            AutoRenew = true,
            ChannelCode = "Null",
            PaymentMethodToken = "pm_test",
            ProviderCustomerId = "cus_test",
            LastBillingTradeNo = $"PAY-SEED-{subscriptionNo}"
        };
        await SeedAsync(subscription);
        return subscription;
    }

    private static SubscriptionPlanDto Edit(SubscriptionPlan plan, decimal? price = null, string? currency = null, BillingCycleType? cycleType = null, int? cycleValue = null, bool isActive = true) => new()
    {
        PlanCode = plan.PlanCode,
        PlanName = plan.PlanName,
        ProductCode = plan.ProductCode,
        Price = price ?? plan.Price,
        Currency = currency ?? plan.Currency,
        CycleType = cycleType ?? plan.CycleType,
        CycleValue = cycleValue ?? plan.CycleValue,
        IsActive = isActive
    };

    private Task<Result> UpdatePlanAsync(Guid planId, SubscriptionPlanDto dto) =>
        InScopeAsync<ISubscriptionService, Result>(svc => svc.UpdatePlanAsync(planId, dto));

    /// <summary>绕过管理端直接改库：修复之前就被改过条款的计划，或与开通并发的一次改计划。</summary>
    private async Task ForcePlanAsync(Guid planId, Action<SubscriptionPlan> change)
    {
        using var scope = ServiceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
        change(ctx.Set<SubscriptionPlan>().First(p => p.Id == planId));
        await ctx.SaveChangesAsync();
    }

    private async Task<PaymentEntity> SingleChargeAsync(string subscriptionNo)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<PaymentEntity, Guid>>();
        return await repo.AsQueryable().SingleAsync(p => p.BusinessOrderNo == subscriptionNo);
    }

    private Task<Result<int>> RenewAsync() =>
        InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.RenewExpiredSubscriptionsAsync());

    [Fact]
    public async Task ChangingTheCurrencyOfAPlanWithLiveSubscribers_IsRefused()
    {
        var plan = await SeedPlanAsync("TERMS-1");
        await SeedSubscriptionAsync(plan, "SUB-TERMS-1");

        var result = await UpdatePlanAsync(plan.Id, Edit(plan, price: 1500m, currency: "JPY"));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldBe(ErrorCodes.SubscriptionPlanHasActiveSubscriptions);
        var reloaded = await ReloadAsync<SubscriptionPlan>(plan.Id);
        reloaded!.Currency.ShouldBe("USD");
        reloaded.Price.ShouldBe(10m);
    }

    [Theory]
    [InlineData(BillingCycleType.Year, 1)]
    [InlineData(BillingCycleType.Month, 3)]
    public async Task ChangingTheCycleOfAPlanWithLiveSubscribers_IsRefused(BillingCycleType cycleType, int cycleValue)
    {
        var plan = await SeedPlanAsync($"TERMS-2-{cycleType}-{cycleValue}");
        await SeedSubscriptionAsync(plan, $"SUB-TERMS-2-{cycleType}-{cycleValue}", SubscriptionStatus.PastDue);

        var result = await UpdatePlanAsync(plan.Id, Edit(plan, price: 100m, cycleType: cycleType, cycleValue: cycleValue));

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.SubscriptionPlanHasActiveSubscriptions);
        var reloaded = await ReloadAsync<SubscriptionPlan>(plan.Id);
        reloaded!.CycleType.ShouldBe(BillingCycleType.Month);
        reloaded.CycleValue.ShouldBe(1);
    }

    [Fact]
    public async Task ChangingTheTermsOfAPlanWhoseSubscriptionsHaveAllEnded_IsAllowed()
    {
        var plan = await SeedPlanAsync("TERMS-3");
        await SeedSubscriptionAsync(plan, "SUB-TERMS-3", SubscriptionStatus.Expired);

        var result = await UpdatePlanAsync(plan.Id, Edit(plan, price: 1500m, currency: "JPY", cycleType: BillingCycleType.Year));

        result.Succeeded.ShouldBeTrue(result.Message);
        var reloaded = await ReloadAsync<SubscriptionPlan>(plan.Id);
        reloaded!.Currency.ShouldBe("JPY");
        reloaded.CycleType.ShouldBe(BillingCycleType.Year);
    }

    /// <summary>
    /// 编辑表单里的「启用」开关也是一条停用路径，与 DeletePlanAsync 同一条规则：有存量订阅时拒绝，
    /// 而且先于条款检查（同一个错误码，调用方据此说「不能停用」）。
    /// </summary>
    [Fact]
    public async Task SwitchingOffAPlanWithLiveSubscribersThroughAnEdit_IsRefused()
    {
        var plan = await SeedPlanAsync("ACTIVE-1");
        await SeedSubscriptionAsync(plan, "SUB-ACTIVE-1");

        var result = await UpdatePlanAsync(plan.Id, Edit(plan, isActive: false));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldBe(ErrorCodes.SubscriptionPlanHasActiveSubscriptions);
        (await ReloadAsync<SubscriptionPlan>(plan.Id))!.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task SwitchingOffAndBackOnAPlanWhoseSubscriptionsHaveAllEnded_IsAllowed()
    {
        var plan = await SeedPlanAsync("ACTIVE-2");
        await SeedSubscriptionAsync(plan, "SUB-ACTIVE-2", SubscriptionStatus.Expired);

        (await UpdatePlanAsync(plan.Id, Edit(plan, isActive: false))).Succeeded.ShouldBeTrue();
        (await ReloadAsync<SubscriptionPlan>(plan.Id))!.IsActive.ShouldBeFalse();

        (await UpdatePlanAsync(plan.Id, Edit(plan, isActive: true))).Succeeded.ShouldBeTrue();
        (await ReloadAsync<SubscriptionPlan>(plan.Id))!.IsActive.ShouldBeTrue();
    }

    /// <summary>已停用的计划带着存量订阅也能改价、重新启用：规则只拦「从启用到停用」那一下。</summary>
    [Fact]
    public async Task EditingAnInactivePlanWithLiveSubscribers_IsNotTreatedAsADeactivation()
    {
        var plan = await SeedPlanAsync("ACTIVE-3");
        await SeedSubscriptionAsync(plan, "SUB-ACTIVE-3");
        await ForcePlanAsync(plan.Id, p => p.IsActive = false);

        (await UpdatePlanAsync(plan.Id, Edit(plan, price: 12m, isActive: false))).Succeeded.ShouldBeTrue();
        (await UpdatePlanAsync(plan.Id, Edit(plan, price: 12m, isActive: true))).Succeeded.ShouldBeTrue();
        (await ReloadAsync<SubscriptionPlan>(plan.Id))!.IsActive.ShouldBeTrue();
    }

    /// <summary>改价（同币种同周期）照旧允许，续费按新价收（回归：这是有意的设计）。</summary>
    [Fact]
    public async Task APriceOnlyEdit_IsAllowedAndTheNextRenewalChargesTheNewPrice()
    {
        var plan = await SeedPlanAsync("TERMS-4");
        var subscription = await SeedSubscriptionAsync(plan, "SUB-TERMS-4", due: true);

        (await UpdatePlanAsync(plan.Id, Edit(plan, price: 12m))).Succeeded.ShouldBeTrue();
        await RenewAsync();

        var charge = await SingleChargeAsync("SUB-TERMS-4");
        charge.OriginalAmount.ShouldBe(12m);
        charge.Currency.ShouldBe("USD");
        (await ReloadAsync<Subscription>(subscription.Id))!.NextBillingTime!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddDays(25));
    }

    /// <summary>
    /// 计划的币种已经和订阅对不上（修复之前改过的计划）：续费按订阅自己的快照价与币种扣，
    /// 而不是把另一种币种下的数字配上订阅的币种（1500 JPY 的计划价扣成 1500 USD）。
    /// </summary>
    [Fact]
    public async Task ARenewalOnAPlanNowInAnotherCurrency_ChargesTheSubscriptionsOwnPrice()
    {
        var plan = await SeedPlanAsync("TERMS-5");
        await SeedSubscriptionAsync(plan, "SUB-TERMS-5", due: true);
        await ForcePlanAsync(plan.Id, p =>
        {
            p.Currency = "JPY";
            p.Price = 1500m;
        });

        await RenewAsync();

        var charge = await SingleChargeAsync("SUB-TERMS-5");
        charge.OriginalAmount.ShouldBe(10m);
        charge.Currency.ShouldBe("USD");
    }

    /// <summary>计划的周期已经和订阅对不上：月付订户不会每个月被扣年费。</summary>
    [Fact]
    public async Task ARenewalOnAPlanNowOnAnotherCycle_ChargesTheSubscriptionsOwnPrice()
    {
        var plan = await SeedPlanAsync("TERMS-6");
        var subscription = await SeedSubscriptionAsync(plan, "SUB-TERMS-6", due: true);
        await ForcePlanAsync(plan.Id, p =>
        {
            p.CycleType = BillingCycleType.Year;
            p.Price = 100m;
        });

        await RenewAsync();

        (await SingleChargeAsync("SUB-TERMS-6")).OriginalAmount.ShouldBe(10m);
        var renewed = await ReloadAsync<Subscription>(subscription.Id);
        renewed!.NextBillingTime!.Value.ShouldBeLessThan(DateTime.UtcNow.AddDays(40));
    }

    /// <summary>
    /// 到期结算的变更换到一个周期不同的计划：订阅随变更换上新计划的条款，续费按新计划的价收（回归）。
    /// 续费扫描读到的订阅快照里还是旧条款，拿它和新计划比会误判成「条款对不上」。
    /// </summary>
    [Fact]
    public async Task ADueChangeToAPlanOnAnotherCycle_IsChargedAtTheNewPlansPrice()
    {
        var monthly = await SeedPlanAsync("TERMS-7-M", 30m);
        var yearly = await SeedPlanAsync("TERMS-7-Y", 25m, BillingCycleType.Year);
        var subscription = await SeedSubscriptionAsync(monthly, "SUB-TERMS-7", due: true);
        await SeedAsync(new SubscriptionChange
        {
            SubscriptionId = subscription.Id,
            FromPlanId = monthly.Id,
            ToPlanId = yearly.Id,
            ChangeType = SubscriptionChangeType.Downgrade,
            EffectiveDate = DateTime.UtcNow.AddMinutes(-5),
            Status = SubscriptionChangeStatus.Pending
        });

        await RenewAsync();

        (await SingleChargeAsync("SUB-TERMS-7")).OriginalAmount.ShouldBe(25m);
        var renewed = await ReloadAsync<Subscription>(subscription.Id);
        renewed!.PlanId.ShouldBe(yearly.Id);
        renewed.CycleType.ShouldBe(BillingCycleType.Year);
        renewed.NextBillingTime!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddMonths(11));
    }
}
