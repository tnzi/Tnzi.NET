using Tnzi.Notification.Dtos;
using Tnzi.Notification.Services;

namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 续费提醒里写的金额与续费实际扣的是同一个数。
/// </summary>
/// <remarks>
/// 续费先结算到期的待生效变更，再按生效计划的现价扣。提醒此前直接写订阅开通时的快照价：
/// 运营把计划从 29 调到 39，邮件写「将以 29 USD 续费」而 7 天后扣 39；排好的降级也被写成旧计划的价。
/// </remarks>
public class SubscriptionReminderAmountTests : SubscriptionsIntegrationTestBase
{
    private readonly List<CreateNotificationRequest> _emails = [];

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<SubscriptionOptions>(o => o.AutoRenewalReminderDays = 7);

        var notifications = new Mock<INotificationService>();
        notifications
            .Setup(n => n.CreateAndSendAsync(It.IsAny<CreateNotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateNotificationRequest, CancellationToken>((request, _) => _emails.Add(request))
            .ReturnsAsync(Result.Success(new NotificationInfo()));
        services.AddScoped(_ => notifications.Object);
    }

    private async Task<SubscriptionPlan> SeedPlanAsync(string name, decimal price)
    {
        var plan = new SubscriptionPlan
        {
            PlanCode = $"{name}{Guid.NewGuid():N}"[..12],
            PlanName = name,
            ProductCode = "SUITE",
            Price = price,
            Currency = "USD",
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            IsActive = true
        };
        await SeedAsync(plan);
        return plan;
    }

    private async Task<Subscription> SeedSubscriptionAsync(SubscriptionPlan plan, decimal snapshotPrice)
    {
        var subscription = new Subscription
        {
            SubscriptionNo = $"SUB{Guid.NewGuid():N}",
            UserId = TestHelper.DefaultTestUserId,
            CustomerEmail = "payer@example.com",
            PlanId = plan.Id,
            ProductCode = plan.ProductCode,
            Status = SubscriptionStatus.Active,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            StartTime = DateTime.UtcNow.AddMonths(-1),
            NextBillingTime = DateTime.UtcNow.AddDays(3),
            OriginalPrice = snapshotPrice,
            PaidAmount = snapshotPrice,
            Currency = "USD",
            AutoRenew = true,
            ChannelCode = "Null",
            PaymentMethodToken = "pm_test",
            LastBillingTradeNo = "PAY-SEED-" + Guid.NewGuid().ToString("N")
        };
        await SeedAsync(subscription);
        return subscription;
    }

    /// <summary>★ 运营调价之后：提醒写的是续费会扣的计划现价，而不是开通时的快照价。</summary>
    [Fact]
    public async Task AfterAPriceIncrease_TheReminderStatesTheNewPrice()
    {
        var plan = await SeedPlanAsync("Pro", 39m);
        await SeedSubscriptionAsync(plan, snapshotPrice: 29m);

        (await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.SendRenewalRemindersAsync())).Data.ShouldBe(1);

        var body = _emails.ShouldHaveSingleItem().Content;
        body.ShouldContain("for 39.00 USD");
        body.ShouldNotContain("29.00 USD");
    }

    /// <summary>★ 续费那一刻到期的降级：提醒写的是降级之后那个计划的名字与价格。</summary>
    [Fact]
    public async Task WithADowngradeDueAtRenewal_TheReminderStatesTheTargetPlan()
    {
        var pro = await SeedPlanAsync("Pro", 30m);
        var basic = await SeedPlanAsync("Basic", 10m);
        var subscription = await SeedSubscriptionAsync(pro, snapshotPrice: 30m);
        await SeedAsync(new SubscriptionChange
        {
            SubscriptionId = subscription.Id,
            FromPlanId = pro.Id,
            ToPlanId = basic.Id,
            ChangeType = SubscriptionChangeType.Downgrade,
            EffectiveDate = subscription.NextBillingTime!.Value,
            Status = SubscriptionChangeStatus.Pending
        });

        (await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.SendRenewalRemindersAsync())).Data.ShouldBe(1);

        var body = _emails.ShouldHaveSingleItem().Content;
        body.ShouldContain("Your Basic renews");
        body.ShouldContain("for 10.00 USD");
    }
}
