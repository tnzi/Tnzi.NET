namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 客户自己的账单页开通订阅：计划列表带一期的税，放弃过一次收银台的管理者接着付完那张首期单，
/// 而不是被「已订阅」挡住或开出第二条订阅。
/// </summary>
public class SubscribeFromBillingPageTests : SubscriptionsIntegrationTestBase
{
    private bool _taxEnabled;
    private bool _taxIncluded;

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<TaxOptions>(o =>
        {
            o.Enabled = _taxEnabled;
            o.DefaultTaxRate = 13m;
            o.TaxIncluded = _taxIncluded;
            o.Name = "HST";
            o.RegistrationNumber = "123456789 RT0001";
        });
    }

    private async Task<SubscriptionPlan> SeedPlanAsync(decimal price = 100m)
    {
        var plan = new SubscriptionPlan
        {
            PlanCode = $"P{Guid.NewGuid():N}"[..12],
            PlanName = "Firm",
            ProductCode = "firm",
            Price = price,
            Currency = "CAD",
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            IsActive = true
        };
        await SeedAsync(plan);
        return plan;
    }

    private Task<Result<List<SubscriptionPlanDto>>> PlansAsync(bool includeTax) =>
        InScopeAsync<ISubscriptionService, Result<List<SubscriptionPlanDto>>>(svc => svc.GetSubscriptionPlansAsync(true, "firm", includeTax));

    private Task<Result<SubscriptionCreateResultDto>> CreateAsync(SubscriptionPlan plan) =>
        InScopeAsync<ISubscriptionService, Result<SubscriptionCreateResultDto>>(
            svc => svc.CreateSubscriptionAsync(new CreateSubscriptionDto { PlanId = plan.Id, ChannelCode = "Null" }));

    private Task<Result<SubscriptionPayNowResultDto>> PayNowAsync(Guid subscriptionId) =>
        InScopeAsync<ISubscriptionService, Result<SubscriptionPayNowResultDto>>(svc => svc.PayNowAsync(subscriptionId));

    [Fact]
    public async Task ThePlanList_SaysWhatTaxIsAddedToAPeriod()
    {
        _taxEnabled = true;
        await SeedPlanAsync();

        var plan = (await PlansAsync(includeTax: true)).Data.ShouldNotBeNull().ShouldHaveSingleItem();

        var tax = plan.Tax.ShouldNotBeNull();
        tax.TaxIncluded.ShouldBeFalse();
        tax.TaxAmount.ShouldBe(13m);
        tax.PayableAmount.ShouldBe(113m);
        var line = tax.Lines.ShouldHaveSingleItem();
        line.Name.ShouldBe("HST");
        line.Rate.ShouldBe(13m);
        line.RegistrationNumber.ShouldBe("123456789 RT0001");
    }

    [Fact]
    public async Task ThePlanList_SaysWhenThePriceAlreadyIncludesTax()
    {
        _taxEnabled = true;
        _taxIncluded = true;
        await SeedPlanAsync(113m);

        var tax = (await PlansAsync(includeTax: true)).Data!.Single().Tax.ShouldNotBeNull();

        tax.TaxIncluded.ShouldBeTrue();
        tax.PayableAmount.ShouldBe(113m, "the price is what is charged");
        tax.TaxAmount.ShouldBe(13m);
    }

    [Fact]
    public async Task WithoutTax_ThePlanSaysNothingIsAdded_AndTheAdminListCarriesNoPreview()
    {
        await SeedPlanAsync();

        var tax = (await PlansAsync(includeTax: true)).Data!.Single().Tax.ShouldNotBeNull();
        tax.TaxAmount.ShouldBe(0m);
        tax.PayableAmount.ShouldBe(100m);
        tax.Lines.ShouldBeEmpty();

        (await PlansAsync(includeTax: false)).Data!.Single().Tax.ShouldBeNull("the admin plan list is not a price quote");
    }

    [Fact]
    public async Task TheFirstPayment_ChargesWhatThePlanListQuoted()
    {
        _taxEnabled = true;
        var plan = await SeedPlanAsync();
        var quoted = (await PlansAsync(includeTax: true)).Data!.Single().Tax!.PayableAmount;

        var created = await CreateAsync(plan);

        created.Succeeded.ShouldBeTrue(created.Message);
        created.Data!.Payment.ShouldNotBeNull().Amount.ShouldBe(quoted);
    }

    [Fact]
    public async Task PayNowOnAPendingSubscription_HandsBackTheSameFirstPayment()
    {
        var plan = await SeedPlanAsync();
        var created = (await CreateAsync(plan)).Data!;
        var firstTradeNo = created.Payment!.TradeNo;

        var resumed = await PayNowAsync(created.Subscription.Id);

        resumed.Succeeded.ShouldBeTrue(resumed.Message);
        resumed.Data!.TradeNo.ShouldBe(firstTradeNo, "a second first payment could be paid twice");
        resumed.Data.PayParams.ShouldNotBeNull().ShouldContain(firstTradeNo);
        resumed.Data.Amount.ShouldBe(100m);
        resumed.Data.SettledWithoutPayment.ShouldBeFalse();

        var payments = await InScopeAsync<IRepository<PaymentEntity, Guid>, int>(repo =>
            repo.AsQueryable().CountAsync(p => p.BusinessOrderNo == created.Subscription.SubscriptionNo));
        payments.ShouldBe(1);
    }

    [Fact]
    public async Task AFirstPaymentThatCanNoLongerBePaid_IsRefused_AndSubscribingAgainWorks()
    {
        var plan = await SeedPlanAsync();
        var created = (await CreateAsync(plan)).Data!;
        // 首期单被渠道作废、但回流没能落地（回流落地时订阅已被收口，见 PendingFirstPaymentEndedTests）
        await InScopeAsync<IRepository<PaymentEntity, Guid>, int>(repo => repo.AsQueryable()
            .Where(p => p.TradeNo == created.Payment!.TradeNo)
            .ExecuteUpdateAsync(p => p.SetProperty(x => x.Status, PaymentStatus.Closed)));

        var resumed = await PayNowAsync(created.Subscription.Id);
        resumed.Succeeded.ShouldBeFalse();
        resumed.Code.ShouldBe(400);
        resumed.Message.ShouldBe(ErrorCodes.SubscriptionCannotPayNow);

        var again = await CreateAsync(plan);
        again.Succeeded.ShouldBeTrue(again.Message);
        (await ReloadAsync<Subscription>(created.Subscription.Id))!.Status.ShouldBe(SubscriptionStatus.Cancelled);
    }
}
