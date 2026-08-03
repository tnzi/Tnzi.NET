using Tnzi.Payment.Dtos;
using Tnzi.Payment.Entities;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Services;
using Tnzi.Results;
using Tnzi.TestBase;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 绑卡链路集成测试。
/// </summary>
/// <remarks>
/// 这条链路此前整体缺失：Subscription 上的 PaymentMethodToken / ProviderCustomerId 全仓只读不写，
/// 导致后台续费、试用转正、升级补差在开箱状态下必然走"无支付方式"分支降级 PastDue。
/// </remarks>
public class PaymentMethodIntegrationTests : PaymentIntegrationTestBase
{
    private static readonly Guid UserId = TestHelper.DefaultTestUserId;

    private async Task<SubscriptionPlan> SeedPlanAsync()
    {
        var plan = new SubscriptionPlan
        {
            PlanCode = $"PLAN-{Guid.NewGuid():N}"[..16],
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

    private Task<Result<StoredPaymentMethodDto>> BindAsync(string token, bool setAsDefault = true) =>
        InScopeAsync<IPaymentMethodService, Result<StoredPaymentMethodDto>>(
            svc => svc.BindAsync(UserId, new BindPaymentMethodDto
            {
                PaymentMethodToken = token,
                ChannelCode = "Null",
                SetAsDefault = setAsDefault
            }));

    [Fact]
    public async Task CreateSetupSession_ReturnsClientSecret()
    {
        var session = await InScopeAsync<IPaymentMethodService, Result<SetupSessionDto>>(
            svc => svc.CreateSetupSessionAsync(UserId, new CreateSetupSessionDto { ChannelCode = "Null" }));

        session.Succeeded.ShouldBeTrue();
        session.Data!.ClientSecret.ShouldNotBeNullOrWhiteSpace();
        session.Data.ChannelCode.ShouldBe("Null");
    }

    [Fact]
    public async Task Bind_PersistsMethodAndMarksFirstAsDefault()
    {
        var bound = await BindAsync("pm_test_1", setAsDefault: false);

        bound.Succeeded.ShouldBeTrue();
        // 首个支付方式必须自动成为默认，否则后台扣款找不到可用的卡
        bound.Data!.IsDefault.ShouldBeTrue();
        bound.Data.Last4.ShouldBe("4242");

        var methods = await InScopeAsync<IPaymentMethodService, Result<List<StoredPaymentMethodDto>>>(
            svc => svc.GetUserMethodsAsync(UserId));
        methods.Data!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Bind_SameTokenTwice_UpdatesInsteadOfDuplicating()
    {
        var first = await BindAsync("pm_test_dup");
        var second = await BindAsync("pm_test_dup");

        first.Succeeded.ShouldBeTrue();
        second.Succeeded.ShouldBeTrue();
        second.Data!.Id.ShouldBe(first.Data!.Id);

        var methods = await InScopeAsync<IPaymentMethodService, Result<List<StoredPaymentMethodDto>>>(
            svc => svc.GetUserMethodsAsync(UserId));
        methods.Data!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SetDefault_MovesDefaultFlagToSelectedMethod()
    {
        var first = await BindAsync("pm_a");
        var second = await BindAsync("pm_b", setAsDefault: false);

        // 第二张卡显式不设默认时，默认仍是第一张
        second.Data!.IsDefault.ShouldBeFalse();

        var changed = await InScopeAsync<IPaymentMethodService, Result>(
            svc => svc.SetDefaultAsync(UserId, second.Data.Id));
        changed.Succeeded.ShouldBeTrue();

        var methods = await InScopeAsync<IPaymentMethodService, Result<List<StoredPaymentMethodDto>>>(
            svc => svc.GetUserMethodsAsync(UserId));

        methods.Data!.Single(m => m.Id == second.Data.Id).IsDefault.ShouldBeTrue();
        methods.Data!.Single(m => m.Id == first.Data!.Id).IsDefault.ShouldBeFalse();
    }

    /// <summary>
    /// 绑定默认卡后，用户已有的、尚未绑卡的订阅要同步拿到这张卡，
    /// 否则"我明明绑了卡"却仍然续不上费。
    /// </summary>
    [Fact]
    public async Task Bind_SyncsTokenToSubscriptionsWithoutPaymentMethod()
    {
        var subscription = new Subscription
        {
            SubscriptionNo = "SUB-SYNC-1",
            UserId = UserId,
            PlanId = (await SeedPlanAsync()).Id,
            Status = SubscriptionStatus.Active,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            StartTime = DateTime.UtcNow,
            NextBillingTime = DateTime.UtcNow.AddDays(30),
            Currency = "USD",
            ChannelCode = "Null",
            AutoRenew = true
        };
        await SeedAsync(subscription);

        var bound = await BindAsync("pm_sync");
        bound.Succeeded.ShouldBeTrue();

        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.PaymentMethodToken.ShouldBe("pm_sync");
        reloaded.StoredPaymentMethodId.ShouldBe(bound.Data!.Id);
        reloaded.PaymentMethodLast4.ShouldBe("4242");
    }

    /// <summary>
    /// 解绑要同时清掉订阅上的快照，否则后台会拿着已失效的 token 反复扣款失败
    /// </summary>
    [Fact]
    public async Task Remove_ClearsSubscriptionBinding()
    {
        var subscription = new Subscription
        {
            SubscriptionNo = "SUB-SYNC-2",
            UserId = UserId,
            PlanId = (await SeedPlanAsync()).Id,
            Status = SubscriptionStatus.Active,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            StartTime = DateTime.UtcNow,
            NextBillingTime = DateTime.UtcNow.AddDays(30),
            Currency = "USD",
            ChannelCode = "Null",
            AutoRenew = true
        };
        await SeedAsync(subscription);

        var bound = await BindAsync("pm_remove");
        bound.Succeeded.ShouldBeTrue();

        var removed = await InScopeAsync<IPaymentMethodService, Result>(
            svc => svc.RemoveAsync(UserId, bound.Data!.Id));
        removed.Succeeded.ShouldBeTrue();

        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.PaymentMethodToken.ShouldBeNull();
        reloaded.StoredPaymentMethodId.ShouldBeNull();

        var methods = await InScopeAsync<IPaymentMethodService, Result<List<StoredPaymentMethodDto>>>(
            svc => svc.GetUserMethodsAsync(UserId));
        methods.Data!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Bind_WithEmptyToken_IsRejected()
    {
        var bound = await BindAsync(string.Empty);

        bound.Succeeded.ShouldBeFalse();
        bound.Message.ShouldBe(ErrorCodes.PaymentMethodNotFound);
    }

    /// <summary>
    /// 付款人在渠道那边撤销授权（PayPal 撤销 / Stripe 删卡）后，本地必须跟着失效。
    /// </summary>
    /// <remarks>
    /// 不接这条 webhook 也不会立刻出事——下次续费扣款失败照样降级 PastDue 并催款——
    /// 但那要等到下一个计费周期。用户是在渠道那边操作的，多半没意识到自己顺手关掉了这里的自动续费，
    /// 而"续费失败"这个信号迟一个周期到，对订阅业务就是一个周期的收入。
    /// </remarks>
    [Fact]
    public async Task RevocationCallback_DeactivatesMethodAndClearsSubscriptionBinding()
    {
        var subscription = await SeedSubscriptionAsync("SUB-REVOKE-1");

        var bound = await BindAsync("pm_revoked");
        bound.Succeeded.ShouldBeTrue();

        var handled = await SendRevocationCallbackAsync("pm_revoked", "evt-revoke-1");
        handled.Succeeded.ShouldBeTrue();

        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        // 不清快照的话，后台会拿着一个已经作废的凭据反复扣款失败
        reloaded!.PaymentMethodToken.ShouldBeNull();
        reloaded.StoredPaymentMethodId.ShouldBeNull();

        var methods = await InScopeAsync<IPaymentMethodService, Result<List<StoredPaymentMethodDto>>>(
            svc => svc.GetUserMethodsAsync(UserId));
        methods.Data!.ShouldBeEmpty();
    }

    /// <summary>
    /// 渠道会重投同一事件，这条路径必须是幂等的。
    /// </summary>
    [Fact]
    public async Task RevocationCallback_IsIdempotentAcrossRedeliveries()
    {
        var bound = await BindAsync("pm_revoked_twice");
        bound.Succeeded.ShouldBeTrue();

        // 用不同的事件ID绕开去重缓存，直击"记录已失效"这条兜底路径
        (await SendRevocationCallbackAsync("pm_revoked_twice", "evt-a")).Succeeded.ShouldBeTrue();
        (await SendRevocationCallbackAsync("pm_revoked_twice", "evt-b")).Succeeded.ShouldBeTrue();

        var stored = await ReloadAsync<StoredPaymentMethod>(bound.Data!.Id);
        stored!.IsActive.ShouldBeFalse();
        stored.IsDefault.ShouldBeFalse();
    }

    /// <summary>
    /// 撤销事件里的凭据不属于本系统时，回 2xx 结束——回失败只会让渠道无休止重投。
    /// </summary>
    [Fact]
    public async Task RevocationCallback_ForUnknownToken_IsAccepted()
    {
        var handled = await SendRevocationCallbackAsync("pm_never_bound_here", "evt-unknown");

        handled.Succeeded.ShouldBeTrue();
    }

    private async Task<Subscription> SeedSubscriptionAsync(string subscriptionNo)
    {
        var subscription = new Subscription
        {
            SubscriptionNo = subscriptionNo,
            UserId = UserId,
            PlanId = (await SeedPlanAsync()).Id,
            Status = SubscriptionStatus.Active,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            StartTime = DateTime.UtcNow,
            NextBillingTime = DateTime.UtcNow.AddDays(30),
            Currency = "USD",
            ChannelCode = "Null",
            AutoRenew = true
        };
        await SeedAsync(subscription);
        return subscription;
    }

    private Task<Result> SendRevocationCallbackAsync(string token, string eventId) =>
        InScopeAsync<IPaymentService, Result>(svc => svc.HandleCallbackAsync(new PaymentCallbackDto
        {
            ChannelCode = "Null",
            Parameters = new Dictionary<string, string>
            {
                ["revoked_token"] = token,
                ["event_id"] = eventId
            }
        }));
}
