namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 等补差款的立即升级（<c>AwaitingPayment</c>）与它那张待支付单的生命周期必须绑在一起。
/// </summary>
/// <remarks>
/// 补差金额是对**当前周期剩余段**算的，周期一结束它就失效。此前无绑卡路径上有一扇窗：
/// 用户请求升级（建一张 30 分钟的待付单）→ 窗口内绑卡 → 续费扫描按旧价收下一期、变更纹丝不动 →
/// 用户再付掉那张单 → 回流只看状态是 AwaitingPayment 就换上新计划、锚点保留，于是新计划整整一期按旧价收。
/// 另一半：变更行没有支付流水号，用户取消变更之后待付单照旧敞着，付了就成孤儿付款只留一行 Warning。
/// 现在变更行记住自己的 <c>PaymentTradeNo</c>：续费 / 试用转正成功即取消仍在等钱的变更并关单，
/// 用户取消变更同样关单；迟到的补差款回流找不到 AwaitingPayment 的变更，什么都不生效。
/// </remarks>
public class AwaitingPaymentChangeLifecycleTests : SubscriptionsIntegrationTestBase
{
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

    private async Task<Subscription> SeedSubscriptionWithoutCardAsync(SubscriptionPlan plan, string subscriptionNo)
    {
        var subscription = new Subscription
        {
            SubscriptionNo = subscriptionNo,
            UserId = TestHelper.DefaultTestUserId,
            PlanId = plan.Id,
            ProductCode = plan.ProductCode,
            Status = SubscriptionStatus.Active,
            CycleType = plan.CycleType,
            CycleValue = plan.CycleValue,
            StartTime = DateTime.UtcNow.AddDays(-15),
            NextBillingTime = DateTime.UtcNow.AddDays(15),
            OriginalPrice = plan.Price,
            Currency = plan.Currency,
            AutoRenew = true,
            ChannelCode = "Null",
            PaymentMethodToken = null,
            ProviderCustomerId = "cus_test"
        };
        await SeedAsync(subscription);
        return subscription;
    }

    private Task<Result<SubscriptionChangeDto>> RequestImmediateUpgradeAsync(Guid subscriptionId, Guid newPlanId) =>
        InScopeAsync<ISubscriptionService, Result<SubscriptionChangeDto>>(
            svc => svc.ChangeSubscriptionPlanAsync(subscriptionId, new ChangeSubscriptionPlanDto
            {
                NewPlanId = newPlanId,
                EffectiveImmediately = true
            }));

    /// <summary>窗口内绑卡，并把周期拨到已到期。</summary>
    private async Task BindCardAndExpirePeriodAsync(Guid subscriptionId)
    {
        using var scope = ServiceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
        var entity = ctx.Set<Subscription>().First(s => s.Id == subscriptionId);
        entity.PaymentMethodToken = "pm_bound_later";
        entity.NextBillingTime = DateTime.UtcNow.AddMinutes(-1);
        await ctx.SaveChangesAsync();
    }

    private Task<Result<int>> RenewAsync() =>
        InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.RenewExpiredSubscriptionsAsync());

    private async Task<PaymentEntity?> ReloadPaymentByTradeNoAsync(string tradeNo)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<PaymentEntity, Guid>>();
        return await repo.FirstOrDefaultAsync(p => p.TradeNo == tradeNo);
    }

    /// <summary>模拟那张待付单迟到的完成回流（渠道回调 at-least-once，关单之前已在途的付款照样会到）。</summary>
    private Task<Result> ReplayProrationPaymentAsync(Subscription subscription, SubscriptionChangeDto change) =>
        InScopeAsync<ISubscriptionService, Result>(svc => svc.ApplyPaymentCompletedAsync(new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.Proration,
            SubscriptionId = subscription.Id,
            SubscriptionNo = subscription.SubscriptionNo,
            ChangeId = change.Id,
            PayerUserId = subscription.UserId,
            PaymentTradeNo = change.Payment!.TradeNo,
            Amount = change.ProratedAmount,
            Currency = subscription.Currency
        }));

    [Fact]
    public async Task AnAwaitingPaymentChange_RemembersItsPaymentTradeNo()
    {
        var basic = await SeedPlanAsync("BASIC-LC1", 10m);
        var pro = await SeedPlanAsync("PRO-LC1", 30m);
        var sub = await SeedSubscriptionWithoutCardAsync(basic, "SUB-LC1");

        var change = await RequestImmediateUpgradeAsync(sub.Id, pro.Id);

        change.Succeeded.ShouldBeTrue();
        change.Data!.Status.ShouldBe(SubscriptionChangeStatus.AwaitingPayment);
        change.Data.Payment.ShouldNotBeNull();
        change.Data.PaymentTradeNo.ShouldBe(change.Data.Payment!.TradeNo);
        (await ReloadAsync<SubscriptionChange>(change.Data.Id))!.PaymentTradeNo.ShouldBe(change.Data.Payment.TradeNo);
    }

    [Fact]
    public async Task RenewalWhileAnUpgradeAwaitsPayment_CancelsTheChangeAndClosesItsPayment()
    {
        var basic = await SeedPlanAsync("BASIC-LC2", 10m);
        var pro = await SeedPlanAsync("PRO-LC2", 30m);
        var sub = await SeedSubscriptionWithoutCardAsync(basic, "SUB-LC2");

        var change = await RequestImmediateUpgradeAsync(sub.Id, pro.Id);
        change.Data!.Status.ShouldBe(SubscriptionChangeStatus.AwaitingPayment);

        await BindCardAndExpirePeriodAsync(sub.Id);
        (await RenewAsync()).Data.ShouldBe(1);

        var reloaded = await ReloadAsync<Subscription>(sub.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.Active);
        reloaded.PlanId.ShouldBe(basic.Id);
        reloaded.NextBillingTime!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddDays(20));

        // 补差是对上一周期剩余段算的，周期一结束它就失效：变更取消、待付单关闭。
        (await ReloadAsync<SubscriptionChange>(change.Data.Id))!.Status.ShouldBe(SubscriptionChangeStatus.Cancelled);
        (await ReloadPaymentByTradeNoAsync(change.Data.Payment!.TradeNo))!.Status.ShouldBe(PaymentStatus.Closed);
    }

    [Fact]
    public async Task LateProrationPayment_AfterRenewal_DoesNotApplyThePlan()
    {
        var basic = await SeedPlanAsync("BASIC-LC3", 10m);
        var pro = await SeedPlanAsync("PRO-LC3", 30m);
        var sub = await SeedSubscriptionWithoutCardAsync(basic, "SUB-LC3");

        var change = await RequestImmediateUpgradeAsync(sub.Id, pro.Id);
        await BindCardAndExpirePeriodAsync(sub.Id);
        (await RenewAsync()).Data.ShouldBe(1);
        var afterRenewal = await ReloadAsync<Subscription>(sub.Id);

        var replayed = await ReplayProrationPaymentAsync(sub, change.Data!);

        replayed.Succeeded.ShouldBeTrue();
        var reloaded = await ReloadAsync<Subscription>(sub.Id);
        reloaded!.PlanId.ShouldBe(basic.Id);
        reloaded.OriginalPrice.ShouldBe(10m);
        reloaded.NextBillingTime.ShouldBe(afterRenewal!.NextBillingTime);
        (await ReloadAsync<SubscriptionChange>(change.Data!.Id))!.Status.ShouldBe(SubscriptionChangeStatus.Cancelled);
    }

    private Task<Result> CancelSubscriptionAsync(Guid subscriptionId, bool immediate) =>
        InScopeAsync<ISubscriptionService, Result>(
            svc => svc.CancelSubscriptionAsync(subscriptionId, new CancelSubscriptionDto { Reason = "test", Immediate = immediate }));

    /// <summary>把订阅拨成「关了自动续费且周期已过」，让逾期扫描把它过期。</summary>
    private async Task MarkPeriodEndedWithoutRenewalAsync(Guid subscriptionId)
    {
        using var scope = ServiceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
        var entity = ctx.Set<Subscription>().First(s => s.Id == subscriptionId);
        entity.Status = SubscriptionStatus.PendingRenewal;
        entity.AutoRenew = false;
        entity.NextBillingTime = DateTime.UtcNow.AddMinutes(-1);
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task ImmediateCancel_WhileAnUpgradeAwaitsPayment_CancelsTheChangeAndClosesItsPayment()
    {
        // 周期由取消结束与由续费结束是同一件事：补差是对这个周期剩余段算的，周期没了它就失效。
        // 留着它，用户取消订阅之后再付那张单，钱收到了却撞上终态守卫 —— 正是本批修复要消灭的孤儿付款。
        var basic = await SeedPlanAsync("BASIC-LC6", 10m);
        var pro = await SeedPlanAsync("PRO-LC6", 30m);
        var sub = await SeedSubscriptionWithoutCardAsync(basic, "SUB-LC6");

        var change = await RequestImmediateUpgradeAsync(sub.Id, pro.Id);
        change.Data!.Status.ShouldBe(SubscriptionChangeStatus.AwaitingPayment);

        (await CancelSubscriptionAsync(sub.Id, immediate: true)).Succeeded.ShouldBeTrue();

        (await ReloadAsync<Subscription>(sub.Id))!.Status.ShouldBe(SubscriptionStatus.Cancelled);
        (await ReloadAsync<SubscriptionChange>(change.Data.Id))!.Status.ShouldBe(SubscriptionChangeStatus.Cancelled);
        (await ReloadPaymentByTradeNoAsync(change.Data.Payment!.TradeNo))!.Status.ShouldBe(PaymentStatus.Closed);
    }

    [Fact]
    public async Task CancelAtPeriodEnd_KeepsTheAwaitingChangePayableUntilThePeriodEnds()
    {
        // 到期后取消：当前周期仍归用户所有，补差覆盖的正是这段剩余期，付了就该生效；
        // 周期真的结束时由过期扫描收口（下一条用例）。
        var basic = await SeedPlanAsync("BASIC-LC7", 10m);
        var pro = await SeedPlanAsync("PRO-LC7", 30m);
        var sub = await SeedSubscriptionWithoutCardAsync(basic, "SUB-LC7");

        var change = await RequestImmediateUpgradeAsync(sub.Id, pro.Id);

        (await CancelSubscriptionAsync(sub.Id, immediate: false)).Succeeded.ShouldBeTrue();

        (await ReloadAsync<Subscription>(sub.Id))!.Status.ShouldBe(SubscriptionStatus.PendingRenewal);
        (await ReloadAsync<SubscriptionChange>(change.Data!.Id))!.Status.ShouldBe(SubscriptionChangeStatus.AwaitingPayment);
        (await ReloadPaymentByTradeNoAsync(change.Data.Payment!.TradeNo))!.Status.ShouldBe(PaymentStatus.Processing);
    }

    [Fact]
    public async Task Expiry_WhileAnUpgradeAwaitsPayment_CancelsTheChangeAndClosesItsPayment()
    {
        var basic = await SeedPlanAsync("BASIC-LC8", 10m);
        var pro = await SeedPlanAsync("PRO-LC8", 30m);
        var sub = await SeedSubscriptionWithoutCardAsync(basic, "SUB-LC8");

        var change = await RequestImmediateUpgradeAsync(sub.Id, pro.Id);
        await MarkPeriodEndedWithoutRenewalAsync(sub.Id);

        (await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ExpireOverdueSubscriptionsAsync())).Data.ShouldBe(1);

        (await ReloadAsync<Subscription>(sub.Id))!.Status.ShouldBe(SubscriptionStatus.Expired);
        (await ReloadAsync<SubscriptionChange>(change.Data!.Id))!.Status.ShouldBe(SubscriptionChangeStatus.Cancelled);
        (await ReloadPaymentByTradeNoAsync(change.Data.Payment!.TradeNo))!.Status.ShouldBe(PaymentStatus.Closed);
    }

    [Fact]
    public async Task CancelPendingChange_ClosesThePendingProrationPayment()
    {
        var basic = await SeedPlanAsync("BASIC-LC4", 10m);
        var pro = await SeedPlanAsync("PRO-LC4", 30m);
        var sub = await SeedSubscriptionWithoutCardAsync(basic, "SUB-LC4");

        var change = await RequestImmediateUpgradeAsync(sub.Id, pro.Id);
        var cancelled = await InScopeAsync<ISubscriptionService, Result>(svc => svc.CancelPendingChangeAsync(change.Data!.Id));

        cancelled.Succeeded.ShouldBeTrue();
        (await ReloadAsync<SubscriptionChange>(change.Data!.Id))!.Status.ShouldBe(SubscriptionChangeStatus.Cancelled);
        (await ReloadPaymentByTradeNoAsync(change.Data.Payment!.TradeNo))!.Status.ShouldBe(PaymentStatus.Closed);
    }

    [Fact]
    public async Task CancelPendingChange_WhenThePaymentIsAlreadyPaid_StillCancelsAndKeepsThePayment()
    {
        // 关单用 CAS，已经付掉（或正在付）的单关不掉；取消本身照样成立，那笔钱按孤儿付款告警处理。
        var basic = await SeedPlanAsync("BASIC-LC5", 10m);
        var pro = await SeedPlanAsync("PRO-LC5", 30m);
        var sub = await SeedSubscriptionWithoutCardAsync(basic, "SUB-LC5");

        var change = await RequestImmediateUpgradeAsync(sub.Id, pro.Id);
        using (var scope = ServiceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
            var payment = ctx.Set<PaymentEntity>().First(p => p.TradeNo == change.Data!.Payment!.TradeNo);
            payment.Status = PaymentStatus.Succeeded;
            await ctx.SaveChangesAsync();
        }

        var cancelled = await InScopeAsync<ISubscriptionService, Result>(svc => svc.CancelPendingChangeAsync(change.Data!.Id));

        cancelled.Succeeded.ShouldBeTrue();
        (await ReloadAsync<SubscriptionChange>(change.Data!.Id))!.Status.ShouldBe(SubscriptionChangeStatus.Cancelled);
        (await ReloadPaymentByTradeNoAsync(change.Data.Payment!.TradeNo))!.Status.ShouldBe(PaymentStatus.Succeeded);
    }
}
