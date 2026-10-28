using Tnzi.Notification.Dtos;
using Tnzi.Notification.Services;

namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 订阅状态机上几条只在边缘出现的守卫：迟到的扣款失败、恢复撞上同产品的另一条有效订阅、换绑另一个渠道的支付方式。
/// </summary>
/// <remarks>
/// 三条的共同点是「当场答成功或 500，问题在之后才冒出来」：一封发给已取消用户的催款信、
/// 一个只能读作服务器故障的 500、一张下一次续费才会被渠道拒掉的卡。
/// </remarks>
public class SubscriptionLifecycleGuardTests : SubscriptionsIntegrationTestBase
{
    private readonly List<CreateNotificationRequest> _emails = [];

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var notifications = new Mock<INotificationService>();
        notifications
            .Setup(n => n.CreateAndSendAsync(It.IsAny<CreateNotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateNotificationRequest, CancellationToken>((request, _) => _emails.Add(request))
            .ReturnsAsync(Result.Success(new NotificationInfo()));
        services.AddScoped(_ => notifications.Object);
    }

    private async Task<SubscriptionPlan> SeedPlanAsync(string? productCode = "SUITE")
    {
        var plan = new SubscriptionPlan
        {
            PlanCode = $"P{Guid.NewGuid():N}"[..12],
            PlanName = "Pro",
            ProductCode = productCode,
            Price = 30m,
            Currency = "USD",
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            IsActive = true
        };
        await SeedAsync(plan);
        return plan;
    }

    private async Task<Subscription> SeedSubscriptionAsync(SubscriptionPlan plan, Action<Subscription>? shape = null)
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
            NextBillingTime = DateTime.UtcNow.AddDays(10),
            OriginalPrice = plan.Price,
            PaidAmount = plan.Price,
            Currency = "USD",
            AutoRenew = true,
            ChannelCode = "Null",
            PaymentMethodToken = "pm_test",
            LastBillingTradeNo = "PAY-SEED-" + Guid.NewGuid().ToString("N")
        };
        shape?.Invoke(subscription);
        await SeedAsync(subscription);
        return subscription;
    }

    // ── 迟到的扣款失败 ──────────────────────────────────────────────────────

    /// <summary>
    /// ★ 续费扣款在渠道侧悬着时订阅已经结束（被取消或过期），那笔扣款随后失败：订阅留在原地，不计重试、不发催款信。
    /// 此前失败回流没有终态守卫，把它改回 PastDue 并请用户「更新支付方式」。
    /// </summary>
    [Theory]
    [InlineData(SubscriptionStatus.Cancelled, SubscriptionBillingPurpose.Renewal)]
    [InlineData(SubscriptionStatus.Expired, SubscriptionBillingPurpose.Renewal)]
    [InlineData(SubscriptionStatus.Cancelled, SubscriptionBillingPurpose.TrialConversion)]
    public async Task ALateBillingFailure_LeavesAnEndedSubscriptionAlone(SubscriptionStatus endedAs, SubscriptionBillingPurpose purpose)
    {
        var plan = await SeedPlanAsync();
        var subscription = await SeedSubscriptionAsync(plan, s =>
        {
            s.Status = endedAs;
            s.AutoRenew = false;
            s.EndTime = DateTime.UtcNow.AddMinutes(-5);
            s.CancelTime = endedAs == SubscriptionStatus.Cancelled ? DateTime.UtcNow.AddMinutes(-5) : null;
        });

        var applied = await InScopeAsync<ISubscriptionService, Result>(svc => svc.ApplyPaymentFailedAsync(new SubscriptionPaymentContext
        {
            Purpose = purpose,
            SubscriptionId = subscription.Id,
            SubscriptionNo = subscription.SubscriptionNo,
            PayerUserId = subscription.UserId,
            PaymentTradeNo = "PAY-LATE-" + Guid.NewGuid().ToString("N"),
            FailReason = "card_declined"
        }));

        applied.Succeeded.ShouldBeTrue(applied.Message);
        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.Status.ShouldBe(endedAs);
        reloaded.RenewalRetryCount.ShouldBe(0);
        reloaded.PastDueSince.ShouldBeNull();
        _emails.ShouldBeEmpty();
    }

    /// <summary>回归：仍在跑的订阅收到续费失败，照旧落 PastDue 并发催款信。</summary>
    [Fact]
    public async Task ABillingFailure_OnARunningSubscription_StillStartsDunning()
    {
        var plan = await SeedPlanAsync();
        var subscription = await SeedSubscriptionAsync(plan);

        var applied = await InScopeAsync<ISubscriptionService, Result>(svc => svc.ApplyPaymentFailedAsync(new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.Renewal,
            SubscriptionId = subscription.Id,
            SubscriptionNo = subscription.SubscriptionNo,
            PayerUserId = subscription.UserId,
            FailReason = "card_declined"
        }));

        applied.Succeeded.ShouldBeTrue(applied.Message);
        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.PastDue);
        reloaded.RenewalRetryCount.ShouldBe(1);
        _emails.ShouldHaveSingleItem();
    }

    // ── 恢复撞上同产品的另一条有效订阅 ─────────────────────────────────────────

    /// <summary>
    /// ★ 用户取消了 A、又开通了同一产品的 B，再去恢复 A：答 400（与开通判重同一个错误码），A 原样留在 Cancelled、计费锁放掉。
    /// 此前直接写回，撞上「同一用户同一产品只有一条有效订阅」的唯一索引，用户拿到 500。
    /// </summary>
    [Theory]
    [InlineData("SUITE", ErrorCodes.SubscriptionProductAlreadySubscribed)]
    [InlineData(null, ErrorCodes.SubscriptionAlreadyActive)]
    public async Task ResumingACancelledSubscription_AfterReopeningTheSameProduct_IsRefused(string? productCode, string expectedCode)
    {
        var plan = await SeedPlanAsync(productCode);
        var cancelled = await SeedSubscriptionAsync(plan, s =>
        {
            s.Status = SubscriptionStatus.Cancelled;
            s.AutoRenew = false;
            s.CancelTime = DateTime.UtcNow.AddDays(-1);
            s.EndTime = DateTime.UtcNow.AddDays(-1);
        });
        await SeedSubscriptionAsync(plan);

        var resumed = await InScopeAsync<ISubscriptionService, Result>(svc => svc.ResumeSubscriptionAsync(cancelled.Id));

        resumed.Succeeded.ShouldBeFalse();
        resumed.Code.ShouldBe(400);
        resumed.Message.ShouldBe(expectedCode);
        var reloaded = await ReloadAsync<Subscription>(cancelled.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.Cancelled);
        reloaded.BillingLockedUntil.ShouldBeNull();
    }

    /// <summary>回归：同产品没有别的有效订阅时，已取消的订阅照常恢复。</summary>
    [Fact]
    public async Task ResumingACancelledSubscription_WithoutAnotherLiveOne_Succeeds()
    {
        var plan = await SeedPlanAsync();
        var cancelled = await SeedSubscriptionAsync(plan, s =>
        {
            s.Status = SubscriptionStatus.Cancelled;
            s.AutoRenew = false;
            s.CancelTime = DateTime.UtcNow.AddDays(-1);
            s.EndTime = DateTime.UtcNow.AddDays(-1);
        });
        // 同一产品下一条已经结束的旧订阅不算
        await SeedSubscriptionAsync(plan, s => s.Status = SubscriptionStatus.Expired);

        var resumed = await InScopeAsync<ISubscriptionService, Result>(svc => svc.ResumeSubscriptionAsync(cancelled.Id));

        resumed.Succeeded.ShouldBeTrue(resumed.Message);
        (await ReloadAsync<Subscription>(cancelled.Id))!.Status.ShouldBe(SubscriptionStatus.Active);
    }

    // ── 换绑另一个渠道的支付方式 ─────────────────────────────────────────────

    private async Task<StoredPaymentMethod> SeedPaymentMethodAsync(string channelCode, string token)
    {
        var method = new StoredPaymentMethod
        {
            UserId = TestHelper.DefaultTestUserId,
            ChannelCode = channelCode,
            Token = token,
            ProviderCustomerId = "cus_" + token,
            MethodType = PaymentMethod.CreditCard,
            Brand = "visa",
            Last4 = "4242",
            IsActive = true
        };
        await SeedAsync(method);
        return method;
    }

    /// <summary>
    /// ★ 订阅在一个渠道上，用户换绑了自己在另一个渠道上保存的支付方式：按「不存在」拒绝，订阅上的支付方式不变。
    /// 此前照收、答 200，续费按订阅的渠道去扣另一个渠道的凭据，下一次续费必然失败。
    /// </summary>
    [Fact]
    public async Task AttachingAPaymentMethodFromAnotherChannel_IsRefused()
    {
        var plan = await SeedPlanAsync();
        var subscription = await SeedSubscriptionAsync(plan);
        var otherChannel = await SeedPaymentMethodAsync(PaymentConstants.PayPalChannelCode, "ba_other_channel");

        var updated = await InScopeAsync<ISubscriptionService, Result<SubscriptionDto>>(svc => svc.UpdatePaymentMethodAsync(
            subscription.Id, new AttachPaymentMethodDto { PaymentMethodId = otherChannel.Id }));

        updated.Succeeded.ShouldBeFalse();
        updated.Message.ShouldBe(ErrorCodes.PaymentMethodNotFound);
        (await ReloadAsync<Subscription>(subscription.Id))!.PaymentMethodToken.ShouldBe("pm_test");
    }

    /// <summary>回归：同一渠道上的支付方式照常换绑（渠道代码的大小写不影响判定）。</summary>
    [Fact]
    public async Task AttachingAPaymentMethodFromTheSameChannel_Succeeds()
    {
        var plan = await SeedPlanAsync();
        var subscription = await SeedSubscriptionAsync(plan);
        var sameChannel = await SeedPaymentMethodAsync("null", "pm_same_channel");

        var updated = await InScopeAsync<ISubscriptionService, Result<SubscriptionDto>>(svc => svc.UpdatePaymentMethodAsync(
            subscription.Id, new AttachPaymentMethodDto { PaymentMethodId = sameChannel.Id }));

        updated.Succeeded.ShouldBeTrue(updated.Message);
        (await ReloadAsync<Subscription>(subscription.Id))!.PaymentMethodToken.ShouldBe("pm_same_channel");
    }

    /// <summary>开通时显式指定另一个渠道上的支付方式，同样在建任何东西之前被拒。</summary>
    [Fact]
    public async Task OpeningASubscriptionWithAPaymentMethodFromAnotherChannel_IsRefused()
    {
        var plan = await SeedPlanAsync("OTHER-PRODUCT");
        var otherChannel = await SeedPaymentMethodAsync(PaymentConstants.PayPalChannelCode, "ba_open_other");

        var created = await InScopeAsync<ISubscriptionService, Result<SubscriptionCreateResultDto>>(svc => svc.CreateSubscriptionAsync(
            new CreateSubscriptionDto { PlanId = plan.Id, ChannelCode = "Null", PaymentMethodId = otherChannel.Id }));

        created.Succeeded.ShouldBeFalse();
        created.Message.ShouldBe(ErrorCodes.PaymentMethodNotFound);
    }

    /// <summary>开通时请求的渠道代码大小写随意：订阅上存的是渠道自己的写法，按它找默认卡才找得到。</summary>
    [Fact]
    public async Task OpeningASubscription_StoresTheChannelsOwnSpelling()
    {
        var plan = await SeedPlanAsync("CASE-PRODUCT");

        var created = await InScopeAsync<ISubscriptionService, Result<SubscriptionCreateResultDto>>(svc => svc.CreateSubscriptionAsync(
            new CreateSubscriptionDto { PlanId = plan.Id, ChannelCode = "null" }));

        created.Succeeded.ShouldBeTrue(created.Message);
        (await ReloadAsync<Subscription>(created.Data!.Subscription.Id))!.ChannelCode.ShouldBe(PaymentConstants.NullChannelCode);
    }
}
