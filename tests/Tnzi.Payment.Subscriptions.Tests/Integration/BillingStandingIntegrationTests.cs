using System.Text.Json;
using Tnzi.Caching;
using Tnzi.EventBus;
using Tnzi.Json;

namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 账户境况的读取面（<c>GET subscriptions/standing</c> 背后的判定）与门禁在真实订阅表上的行为：
/// 暂停时拦、补缴回流之后立刻放行（经境况变化事件清缓存，不等 TTL）。
/// </summary>
public class BillingStandingIntegrationTests : SubscriptionsIntegrationTestBase
{
    private static readonly Guid Owner = TestHelper.DefaultTestUserId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<SubscriptionOptions>(o => o.GracePeriodEndAction = GracePeriodEndAction.Suspend);
        // 缓存开着且很长：恢复之所以立刻生效，只能是因为事件清掉了它
        services.Configure<SubscriptionAccessGateOptions>(o =>
        {
            o.Enabled = true;
            o.CacheSeconds = 600;
        });
        services.AddMemoryCache();
        services.Configure<CachingOptions>(_ => { });
        services.AddSingleton<ICache, MemoryCacheService>();
        services.AddSingleton<SubscriptionStandingCache>();
        services.AddScoped<ISubscriptionAccessGate, SubscriptionAccessGate>();
        services.AddScoped<IEventHandler<BillingAccountStandingChangedEvent>, BillingStandingCacheInvalidationHandler>();
    }

    private async Task<Subscription> SeedAsync(SubscriptionStatus status, DateTime? pastDueSince, Guid? userId = null)
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
            UserId = userId ?? Owner,
            PlanId = plan.Id,
            ProductCode = "firm",
            Status = status,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            StartTime = DateTime.UtcNow.AddMonths(-2),
            NextBillingTime = DateTime.UtcNow.AddDays(-5),
            OriginalPrice = 300m,
            Currency = "CAD",
            AutoRenew = true,
            ChannelCode = "Null",
            PaymentMethodToken = "pm_old_card",
            PastDueSince = pastDueSince,
            RenewalRetryCount = pastDueSince == null ? 0 : 3,
            SuspendedAt = status == SubscriptionStatus.Suspended ? DateTime.UtcNow.AddDays(-1) : null,
            LastBillingTradeNo = "PAY-SEED"
        };
        await SeedAsync(subscription);
        return subscription;
    }

    private Task<BillingStandingDto> StandingAsync(Guid userId) =>
        InScopeAsync<ISubscriptionAccessGate, BillingStandingDto>(gate => gate.GetStandingAsync(userId));

    private Task<SubscriptionAccessDecision> EvaluateAsync(Guid userId) =>
        InScopeAsync<ISubscriptionAccessGate, SubscriptionAccessDecision>(gate => gate.EvaluateAsync(userId));

    [Fact]
    public async Task PastDue_ReportsWarning_WithTheGraceDeadlineAndTheAmountDue()
    {
        var since = DateTime.UtcNow.AddDays(-1);
        var subscription = await SeedAsync(SubscriptionStatus.PastDue, since);

        var standing = await StandingAsync(Owner);

        standing.Standing.ShouldBe(BillingStanding.Warning);
        standing.CanView.ShouldBeTrue("the owner of a personal account sees its billing");
        standing.CanManage.ShouldBeTrue();
        standing.GateEnabled.ShouldBeTrue();
        standing.IsExempt.ShouldBeFalse();
        standing.SubscriptionId.ShouldBe(subscription.Id);
        standing.PlanName.ShouldBe("Firm");
        standing.Status.ShouldBe(SubscriptionStatus.PastDue);
        standing.GraceDeadline!.Value.ShouldBe(since.AddDays(3), TimeSpan.FromSeconds(1));
        standing.AmountDue.ShouldBe(300m);
        standing.Currency.ShouldBe("CAD");
    }

    [Fact]
    public async Task Suspended_ReportsWhenServiceStopped_AndNoGraceDeadline()
    {
        await SeedAsync(SubscriptionStatus.Suspended, DateTime.UtcNow.AddDays(-4));

        var standing = await StandingAsync(Owner);

        standing.Standing.ShouldBe(BillingStanding.Suspended);
        standing.SuspendedAt.ShouldNotBeNull();
        standing.GraceDeadline.ShouldBeNull("the grace period is already over");
    }

    [Fact]
    public async Task AGoodAccount_CarriesNoActionDetails()
    {
        await SeedAsync(SubscriptionStatus.Active, pastDueSince: null);

        var standing = await StandingAsync(Owner);

        standing.Standing.ShouldBe(BillingStanding.Good);
        standing.SubscriptionId.ShouldBeNull();
        standing.AmountDue.ShouldBeNull();
    }

    [Fact]
    public async Task SuspendedAccount_IsGated_UntilThePaymentLands_ThenImmediatelyAllowed()
    {
        var subscription = await SeedAsync(SubscriptionStatus.Suspended, DateTime.UtcNow.AddDays(-4));

        var before = await EvaluateAsync(Owner);
        before.Allowed.ShouldBeFalse();
        before.CanManage.ShouldBeTrue();

        var checkout = await InScopeAsync<ISubscriptionService, Result<SubscriptionPayNowResultDto>>(svc => svc.PayNowAsync(subscription.Id, Owner));
        checkout.Succeeded.ShouldBeTrue(checkout.Message);
        (await EvaluateAsync(Owner)).Allowed.ShouldBeFalse("a checkout that is not paid yet does not lift the suspension");

        var paid = await InScopeAsync<IPaymentService, Result>(svc => svc.HandleCallbackAsync(new PaymentCallbackDto
        {
            ChannelCode = "Null",
            Parameters = new Dictionary<string, string>
            {
                ["trade_no"] = checkout.Data!.TradeNo!,
                ["amount"] = "300",
                ["currency"] = "CAD",
                ["event_id"] = $"evt_{Guid.NewGuid():N}"
            }
        }));
        paid.Succeeded.ShouldBeTrue(paid.Message);

        (await EvaluateAsync(Owner)).Allowed.ShouldBeTrue("the standing event cleared the 10-minute cache");
    }

    [Fact]
    public async Task ReadingTheStanding_RefreshesTheGateCache()
    {
        var subscription = await SeedAsync(SubscriptionStatus.Suspended, DateTime.UtcNow.AddDays(-4));
        (await EvaluateAsync(Owner)).Allowed.ShouldBeFalse();

        // 订阅表被别的路径改好了，而那条路径没有发事件（数据修复、人工改状态）
        await InScopeAsync<IRepository<Subscription, Guid>, int>(repo => repo.AsQueryable()
            .Where(s => s.Id == subscription.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SubscriptionStatus.Active)));
        (await EvaluateAsync(Owner)).Allowed.ShouldBeFalse("still the cached answer");

        (await StandingAsync(Owner)).Standing.ShouldBe(BillingStanding.Good);

        (await EvaluateAsync(Owner)).Allowed.ShouldBeTrue("the shell saw Good, so the gate must agree at the same moment");
    }

    [Fact]
    public async Task SomeoneElsesSuspension_DoesNotGateMe()
    {
        await SeedAsync(SubscriptionStatus.Suspended, DateTime.UtcNow.AddDays(-4), userId: Guid.NewGuid());

        (await EvaluateAsync(Owner)).Allowed.ShouldBeTrue();
        (await StandingAsync(Owner)).Standing.ShouldBe(BillingStanding.None);
    }

    [Fact]
    public async Task SuspendedDetails_SerialiseTheWayTheShellReadsThem()
    {
        var details = JsonSerializer.Serialize(
            new SubscriptionSuspendedDetails { Standing = BillingStanding.Suspended, CanManage = true }, TnziJsonDefaults.Options);

        details.ShouldBe("{\"standing\":\"Suspended\",\"canManage\":true}");
    }
}
