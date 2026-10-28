using System.Collections.Concurrent;
using Tnzi.EventBus;

namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 订阅的支付单以「没收到钱」结束（关单 / 过期）时，订阅状态机收口，而且只收口它自己的那一部分。
/// </summary>
/// <remarks>
/// <para>
/// 关单此前不发终态事件：首期单在收银台被关掉，订阅一直停在 Pending，占着「同产品唯一有效订阅」的名额，
/// 计划删除守卫与租户扫描也照旧把它算作有效订阅。关单现在与失败、过期一样发失败事件。
/// </para>
/// <para>
/// 这条事件也会回到订阅模块自己发起的关单上（取消订阅关首期单、取消变更或周期结束关补差单），在默认的
/// 同步事件总线上它就在关单调用里跑。补差单的失败回流此前把读出来的整行订阅写回并清掉计费锁：
/// 锁若正被一次在途的续费持有，另一个实例就能在续费落地之前再扣一次。
/// </para>
/// <para>
/// 以及线下与 PayPal 渠道上的订阅：首期单不带支付方式，此前按信用卡建单被这两个渠道拒绝，订阅随之被删。
/// </para>
/// </remarks>
public class SubscriptionPaymentEndedUnpaidTests : SubscriptionsIntegrationTestBase
{
    private readonly CancelledEventRecorder _cancellations = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddSingleton(_cancellations);
        services.AddScoped<IEventHandler<SubscriptionCancelledEvent>>(sp => sp.GetRequiredService<CancelledEventRecorder>());
    }

    private async Task<SubscriptionPlan> SeedPlanAsync(string code, decimal price)
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
            IsActive = true
        };
        await SeedAsync(plan);
        return plan;
    }

    private async Task<SubscriptionCreateResultDto> CreatePendingAsync(SubscriptionPlan plan, string channelCode = "Null")
    {
        var created = await InScopeAsync<ISubscriptionService, Result<SubscriptionCreateResultDto>>(
            svc => svc.CreateSubscriptionAsync(new CreateSubscriptionDto { PlanId = plan.Id, ChannelCode = channelCode }));
        created.Succeeded.ShouldBeTrue(created.Message);
        return created.Data!;
    }

    private async Task<PaymentEntity> LoadPaymentAsync(string tradeNo)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<PaymentEntity, Guid>>();
        return (await repo.FirstOrDefaultAsync(p => p.TradeNo == tradeNo))!;
    }

    private async Task MutateAsync<TEntity>(Guid id, Action<TEntity> change) where TEntity : class, Tnzi.Domain.Entities.IEntity<Guid>
    {
        using var scope = ServiceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
        change(await ctx.Set<TEntity>().FirstAsync(e => e.Id == id));
        await ctx.SaveChangesAsync();
    }

    /// <summary>付款人在收银台关掉首期单（用户面的关单端点）：这条订阅再也激活不了，随即终结。</summary>
    [Fact]
    public async Task ClosingTheFirstPaymentAtCheckout_EndsThePendingSubscription()
    {
        var plan = await SeedPlanAsync("PRO-PC1", 30m);
        var created = await CreatePendingAsync(plan);

        var closed = await InScopeAsync<IPaymentService, Result>(
            svc => svc.ClosePaymentAsync(created.Payment!.TradeNo, "changed my mind", TestHelper.DefaultTestUserId));

        closed.Succeeded.ShouldBeTrue(closed.Message);
        var ended = await ReloadAsync<Subscription>(created.Subscription.Id);
        ended!.Status.ShouldBe(SubscriptionStatus.Cancelled);
        ended.NextBillingTime.ShouldBeNull();
        _cancellations.For(created.Subscription.SubscriptionNo).ShouldHaveSingleItem();
    }

    /// <summary>
    /// 取消一条 Pending 订阅会先关它的首期单，那张单的失败回流就在关单调用里跑：取消仍只宣布一次，带的是订阅者自己的理由。
    /// </summary>
    [Fact]
    public async Task CancellingAPendingSubscription_AnnouncesTheCancellationOnceWithTheSubscribersReason()
    {
        var plan = await SeedPlanAsync("PRO-PC2", 30m);
        var created = await CreatePendingAsync(plan);

        var cancelled = await InScopeAsync<ISubscriptionService, Result>(svc => svc.CancelSubscriptionAsync(
            created.Subscription.Id, new CancelSubscriptionDto { Reason = "too expensive", Immediate = true }));

        cancelled.Succeeded.ShouldBeTrue(cancelled.Message);
        (await LoadPaymentAsync(created.Payment!.TradeNo)).Status.ShouldBe(PaymentStatus.Closed);
        var reloaded = await ReloadAsync<Subscription>(created.Subscription.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.Cancelled);
        reloaded.CancelReason.ShouldBe("too expensive");
        _cancellations.For(created.Subscription.SubscriptionNo).ShouldHaveSingleItem().CancelReason.ShouldBe("too expensive");
    }

    /// <summary>
    /// 补差单以「没收到钱」结束：变更取消，订阅行一列不动。尤其不碰计费锁：它可能正被一次在途的续费持有，
    /// 放掉它，另一个实例就会在续费落地之前对同一期再扣一次。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AProrationPaymentEndingUnpaid_CancelsTheChangeAndLeavesTheSubscriptionAlone(bool byExpiry)
    {
        var basic = await SeedPlanAsync($"BASIC-PC3{byExpiry}", 10m);
        var pro = await SeedPlanAsync($"PRO-PC3{byExpiry}", 30m);
        var subscription = new Subscription
        {
            SubscriptionNo = $"SUB-PC3-{byExpiry}",
            UserId = TestHelper.DefaultTestUserId,
            PlanId = basic.Id,
            ProductCode = basic.ProductCode,
            Status = SubscriptionStatus.Active,
            CycleType = basic.CycleType,
            CycleValue = basic.CycleValue,
            StartTime = DateTime.UtcNow.AddDays(-15),
            NextBillingTime = DateTime.UtcNow.AddDays(15),
            OriginalPrice = basic.Price,
            Currency = basic.Currency,
            AutoRenew = true,
            ChannelCode = "Null"
        };
        await SeedAsync(subscription);

        var change = await InScopeAsync<ISubscriptionService, Result<SubscriptionChangeDto>>(svc => svc.ChangeSubscriptionPlanAsync(
            subscription.Id, new ChangeSubscriptionPlanDto { NewPlanId = pro.Id, EffectiveImmediately = true }));
        change.Succeeded.ShouldBeTrue(change.Message);
        change.Data!.Status.ShouldBe(SubscriptionChangeStatus.AwaitingPayment);
        var prorationTradeNo = change.Data.Payment!.TradeNo;

        // 一次续费正在途中：它持有计费锁
        var heldUntil = DateTime.UtcNow.AddMinutes(10);
        await MutateAsync<Subscription>(subscription.Id, s => s.BillingLockedUntil = heldUntil);

        if (byExpiry)
        {
            var payment = await LoadPaymentAsync(prorationTradeNo);
            await MutateAsync<PaymentEntity>(payment.Id, p => p.ExpireTime = DateTime.UtcNow.AddMinutes(-1));
            (await InScopeAsync<IPaymentService, Result<int>>(svc => svc.CloseExpiredPaymentsAsync())).Data.ShouldBe(1);
        }
        else
        {
            (await InScopeAsync<IPaymentService, Result>(svc => svc.ClosePaymentAsync(prorationTradeNo, "admin closed"))).Succeeded.ShouldBeTrue();
        }

        (await ReloadAsync<SubscriptionChange>(change.Data.Id))!.Status.ShouldBe(SubscriptionChangeStatus.Cancelled);
        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.Active);
        reloaded.PlanId.ShouldBe(basic.Id);
        reloaded.BillingLockedUntil.ShouldNotBeNull();
        reloaded.BillingLockedUntil!.Value.ShouldBe(heldUntil, TimeSpan.FromSeconds(1));
    }

    /// <summary>线下渠道（出厂默认渠道）上开通：首期单按渠道收的方式建，而不是按信用卡建、被拒、连订阅一起删掉。</summary>
    [Fact]
    public async Task ASubscriptionOnTheOfflineChannel_CanBeOpened()
    {
        var plan = await SeedPlanAsync("PRO-PC4", 30m);

        var created = await CreatePendingAsync(plan, PaymentConstants.OfflineChannelCode);

        created.Subscription.Status.ShouldBe(SubscriptionStatus.Pending);
        var firstPayment = await LoadPaymentAsync(created.Payment!.TradeNo);
        firstPayment.Status.ShouldBe(PaymentStatus.Pending);
        firstPayment.ChannelCode.ShouldBe(PaymentConstants.OfflineChannelCode);
        firstPayment.PaymentMethod.ShouldBeOneOf(PaymentMethod.BankTransfer, PaymentMethod.Offline);
    }

    /// <summary>记下收到的订阅取消事件。</summary>
    public sealed class CancelledEventRecorder : IEventHandler<SubscriptionCancelledEvent>
    {
        private readonly ConcurrentQueue<SubscriptionCancelledEvent> _received = new();

        public IReadOnlyList<SubscriptionCancelledEvent> For(string subscriptionNo)
            => _received.Where(e => e.SubscriptionNo == subscriptionNo).ToList();

        public Task HandleAsync(SubscriptionCancelledEvent eventData, CancellationToken cancellationToken = default)
        {
            _received.Enqueue(eventData);
            return Task.CompletedTask;
        }
    }
}
