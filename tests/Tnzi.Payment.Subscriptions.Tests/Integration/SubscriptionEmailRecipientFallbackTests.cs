using Tnzi.Notification.Dtos;
using Tnzi.Notification.Services;
using Tnzi.Security.Claims;

namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 续费提醒与催款信的收件人：订阅上的邮箱快照为空（开通时令牌不带邮箱声明）、账户也没填联系人时，
/// 寄给开通人账号上的邮箱，而不是一封都不发。
/// </summary>
public class SubscriptionEmailRecipientFallbackTests : SubscriptionsIntegrationTestBase
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

        var emails = new Mock<IUserEmailProvider>();
        emails
            .Setup(e => e.GetEmailsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                ids.Where(id => id == TestHelper.DefaultTestUserId).ToDictionary(id => id, _ => "manager@firm.example"));
        services.AddScoped(_ => emails.Object);
    }

    [Fact]
    public async Task ARenewalReminder_ReachesTheManagerWhenTheSubscriptionHasNoEmailSnapshot()
    {
        var plan = new SubscriptionPlan
        {
            PlanCode = $"P{Guid.NewGuid():N}"[..12],
            PlanName = "Firm",
            ProductCode = "firm",
            Price = 100m,
            Currency = "CAD",
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            IsActive = true
        };
        await SeedAsync(plan);
        await SeedAsync(new Subscription
        {
            SubscriptionNo = $"SUB{Guid.NewGuid():N}",
            UserId = TestHelper.DefaultTestUserId,
            CustomerEmail = null,
            PlanId = plan.Id,
            ProductCode = plan.ProductCode,
            Status = SubscriptionStatus.Active,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            StartTime = DateTime.UtcNow.AddMonths(-1),
            NextBillingTime = DateTime.UtcNow.AddDays(3),
            OriginalPrice = 100m,
            Currency = "CAD",
            AutoRenew = true,
            ChannelCode = "Null",
            PaymentMethodToken = "pm_test",
            LastBillingTradeNo = "PAY-SEED-" + Guid.NewGuid().ToString("N")
        });

        (await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.SendRenewalRemindersAsync())).Data.ShouldBe(1);

        _emails.ShouldHaveSingleItem().Recipients.ShouldHaveSingleItem().Address.ShouldBe("manager@firm.example");
    }
}
