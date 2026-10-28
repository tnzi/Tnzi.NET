namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 订阅收款带着它覆盖的服务期与付款用的卡（收据要印），开票方经描述器拿到「计划名 + 服务期」；
/// 首单资格按计费账户判（机构里换一位管理者开通仍是老客户）。
/// </summary>
public class SubscriptionReceiptAndHistoryTests : SubscriptionsIntegrationTestBase
{
    private static readonly Guid Owner = TestHelper.DefaultTestUserId;

    private async Task<Subscription> SeedDueAsync(DateTime nextBillingTime)
    {
        var plan = new SubscriptionPlan
        {
            PlanCode = $"P{Guid.NewGuid():N}"[..12],
            PlanName = "Firm",
            ProductCode = "firm",
            Price = 300m,
            Currency = "CAD",
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            IsActive = true
        };
        await SeedAsync(plan);

        var subscription = new Subscription
        {
            SubscriptionNo = $"SUB{Guid.NewGuid():N}"[..16],
            UserId = Owner,
            PlanId = plan.Id,
            ProductCode = "firm",
            Status = SubscriptionStatus.Active,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            StartTime = nextBillingTime.AddMonths(-1),
            NextBillingTime = nextBillingTime,
            OriginalPrice = 300m,
            Currency = "CAD",
            AutoRenew = true,
            ChannelCode = "Null",
            PaymentMethodToken = "pm_card",
            PaymentMethodBrand = "visa",
            PaymentMethodLast4 = "4242",
            LastBillingTradeNo = "PAY-SEED"
        };
        await SeedAsync(subscription);
        return subscription;
    }

    [Fact]
    public async Task ARenewalPayment_CarriesTheServicePeriodItPaysFor_AndTheCard()
    {
        var due = DateTime.UtcNow.AddMinutes(-5);
        var subscription = await SeedDueAsync(due);

        await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.RenewExpiredSubscriptionsAsync());

        var payment = await InScopeAsync<IRepository<PaymentEntity, Guid>, PaymentEntity?>(repo =>
            repo.AsQueryable().AsNoTracking().FirstOrDefaultAsync(p => p.BusinessOrderNo == subscription.SubscriptionNo && p.TradeNo != "PAY-SEED"));
        payment.ShouldNotBeNull();
        payment.PaymentMethodLast4.ShouldBe("4242");
        payment.PaymentMethodBrand.ShouldBe("visa");

        var meta = SubscriptionBillingMetadata.TryParse(payment.ExtraData).ShouldNotBeNull();
        meta.PeriodStart!.Value.ShouldBe(due, TimeSpan.FromSeconds(1), "a renewal pays for the period starting at the billing date");
        meta.PeriodEnd!.Value.ShouldBe(due.AddMonths(1), TimeSpan.FromSeconds(1));
        meta.PlanName.ShouldBe("Firm");

        var lines = await new SubscriptionPaymentLineDescriber().DescribeAsync(new PaymentLineContext
        {
            PaymentId = payment.Id,
            BusinessType = payment.BusinessType,
            BusinessOrderNo = payment.BusinessOrderNo,
            Description = payment.Description,
            ExtraData = payment.ExtraData,
            OriginalAmount = payment.OriginalAmount,
            Currency = payment.Currency
        });
        var line = lines.ShouldNotBeNull().ShouldHaveSingleItem();
        line.Description.ShouldBe("Firm");
        line.PeriodStart.ShouldBe(meta.PeriodStart);
        line.PeriodEnd.ShouldBe(meta.PeriodEnd);
        line.Amount.ShouldBe(300m);
    }

    [Fact]
    public async Task TheDescriber_LeavesOtherPaymentsAndOldSubscriptionPaymentsAlone()
    {
        var describer = new SubscriptionPaymentLineDescriber();

        (await describer.DescribeAsync(new PaymentLineContext { BusinessType = BusinessType.Order })).ShouldBeNull();
        // 早于引入服务期的订阅收款：没有期间，交给开票方按支付描述生成一行
        (await describer.DescribeAsync(new PaymentLineContext
        {
            BusinessType = BusinessType.Subscription,
            ExtraData = new SubscriptionBillingMetadata { Purpose = SubscriptionBillingPurpose.Renewal }.ToExtraData()
        })).ShouldBeNull();
    }

    [Fact]
    public async Task FirstSubscriptionHistory_IsAskedPerBillingAccount()
    {
        var firm = new BillingAccountRef(BillingAccountTypes.Installation, "default");
        var subscription = await SeedDueAsync(DateTime.UtcNow.AddDays(10));
        await InScopeAsync<IRepository<Subscription, Guid>, int>(repo => repo.AsQueryable()
            .Where(s => s.Id == subscription.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.BillingAccountType, firm.Type)
                .SetProperty(x => x.BillingAccountId, firm.Id)));

        (await InScopeAsync<ISubscriptionHistoryProbe, bool>(p => p.HasAnySubscriptionAsync(firm)))
            .ShouldBeTrue("another manager of the same firm is not a new customer");
        (await InScopeAsync<ISubscriptionHistoryProbe, bool>(p => p.HasAnySubscriptionAsync(BillingAccountRef.ForUser(Owner))))
            .ShouldBeFalse("the person who clicked has no subscription of their own");
    }
}
