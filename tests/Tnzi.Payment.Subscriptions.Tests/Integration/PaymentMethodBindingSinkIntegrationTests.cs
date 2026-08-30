
namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 绑卡 → 订阅的同步链路集成测试，跑的是拆分后的真实路径：
/// 父模块的 <c>PaymentMethodService</c> 只广播 <see cref="IStoredPaymentMethodBindingSink"/>，
/// 落到订阅行上的那段 SQL 住在本模块的 <see cref="SubscriptionBindingSink"/> 里。
/// </summary>
/// <remarks>
/// <para>
/// 这四条用例拆分前住在父测试项目的 <c>PaymentMethodIntegrationTests</c>（那时服务自己持有
/// <c>IRepository&lt;Subscription&gt;</c>）。搬过来之后它们证明的东西<b>更强</b>了：
/// 不只是「绑卡会同步订阅」，而是「经过一层扩展点之后，绑卡仍然会同步订阅」——
/// 也就是这条缝合线真的接上了，而不只是编译得过。
/// </para>
/// <para>
/// 与它们互补的是父测试项目的 <c>SubscriptionsPackageAbsenceTests</c>：那边证明**没有**接收方时
/// 绑卡与解绑照常成功、受影响条数为 0。两边合起来才是完整的「缺席 = 少一项能力」。
/// </para>
/// </remarks>
public class PaymentMethodBindingSinkIntegrationTests : SubscriptionsIntegrationTestBase
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

    private Task<Result<StoredPaymentMethodDto>> BindAsync(string token, bool setAsDefault = true) =>
        InScopeAsync<IPaymentMethodService, Result<StoredPaymentMethodDto>>(
            svc => svc.BindAsync(UserId, new BindPaymentMethodDto
            {
                PaymentMethodToken = token,
                ChannelCode = "Null",
                SetAsDefault = setAsDefault
            }));

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

    /// <summary>
    /// 绑定默认卡后，用户已有的、尚未绑卡的订阅要同步拿到这张卡，
    /// 否则"我明明绑了卡"却仍然续不上费。
    /// </summary>
    [Fact]
    public async Task Bind_SyncsTokenToSubscriptionsWithoutPaymentMethod()
    {
        var subscription = await SeedSubscriptionAsync("SUB-SYNC-1");

        var bound = await BindAsync("pm_sync");
        bound.Succeeded.ShouldBeTrue();

        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.PaymentMethodToken.ShouldBe("pm_sync");
        reloaded.StoredPaymentMethodId.ShouldBe(bound.Data!.Id);
        reloaded.PaymentMethodLast4.ShouldBe("4242");
    }

    /// <summary>
    /// 解绑要同时清掉订阅上的快照，否则后台会拿着已失效的 token 反复扣款失败。
    /// </summary>
    [Fact]
    public async Task Remove_ClearsSubscriptionBinding()
    {
        var subscription = await SeedSubscriptionAsync("SUB-SYNC-2");

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

    /// <summary>
    /// 调用方事务回滚时，「置卡失效」与「清订阅快照」必须一起被撤销。
    /// </summary>
    /// <remarks>
    /// ★ 守的是 <c>RemoveAsync</c> 里那句 <c>EnsureTransactionStartedAsync</c> 与
    /// <see cref="IStoredPaymentMethodBindingSink"/> 的事务契约（实现方不得自开事务）合起来的效果：
    /// 少任何一半，「清订阅快照」都会逃逸出事务永久落库，而「置失效」随回滚撤销 ——
    /// 订阅没了卡，支付方式却还显示可用。
    /// 拆包把这条从「同一个文件里的约定」变成了「跨程序集的约定」，因此更需要一条实测。
    /// </remarks>
    [Fact]
    public async Task Remove_InsideCallerTransaction_RollbackKeepsTheSubscriptionBinding()
    {
        var subscription = await SeedSubscriptionAsync("SUB-RB-1");
        var bound = await BindAsync("pm_remove_rb");
        bound.Succeeded.ShouldBeTrue();

        using (var scope = ServiceProvider.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var svc = scope.ServiceProvider.GetRequiredService<IPaymentMethodService>();

            manager.EnableTransaction();
            (await svc.RemoveAsync(UserId, bound.Data!.Id)).Succeeded.ShouldBeTrue();
            await manager.RollbackTransactionAsync();
        }

        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.PaymentMethodToken.ShouldBe("pm_remove_rb");
        var methods = await InScopeAsync<IPaymentMethodService, Result<List<StoredPaymentMethodDto>>>(
            svc => svc.GetUserMethodsAsync(UserId));
        methods.Data!.Single(m => m.Id == bound.Data!.Id).IsDefault.ShouldBeTrue();
    }

    /// <summary>
    /// 付款人在渠道那边撤销授权（PayPal 撤销 / Stripe 删卡）后，订阅上的快照必须跟着清掉。
    /// </summary>
    /// <remarks>
    /// 不清快照，后台会拿着一个已经作废的凭据反复扣款失败。这条走的是完整的
    /// 「回调控制器 → PaymentService → PaymentMethodService → 扩展点 → 订阅行」链路。
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
        reloaded!.PaymentMethodToken.ShouldBeNull();
        reloaded.StoredPaymentMethodId.ShouldBeNull();

        var methods = await InScopeAsync<IPaymentMethodService, Result<List<StoredPaymentMethodDto>>>(
            svc => svc.GetUserMethodsAsync(UserId));
        methods.Data!.ShouldBeEmpty();
    }
}
