namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 待生效（非立即）计划变更的结算链路。
/// </summary>
/// <remarks>
/// 此前这条链路只有前半截：<c>ChangeSubscriptionPlanAsync</c> 插一条
/// <c>Status=Pending, EffectiveDate=NextBillingTime</c> 的变更记录，而全模块没有任何代码读
/// <c>EffectiveDate</c> 判到期，五条后台扫描一条都不碰变更表。后果有两层：
/// 降级到期后仍按旧价扣款（用户按新价的心理预期与账单不符），
/// 而「已有待生效变更」的守卫又让该订阅此后所有变更请求 400 —— 一次降级把这条订阅的
/// 变更能力永久锁死，唯一出口是一个用户界面上没有入口的取消端点。
/// </remarks>
public class SubscriptionPlanChangeIntegrationTests : SubscriptionsIntegrationTestBase
{
    private async Task<SubscriptionPlan> SeedPlanAsync(string code, decimal price, int cycleValue = 1)
    {
        var plan = new SubscriptionPlan
        {
            PlanCode = code,
            PlanName = code,
            ProductCode = "SUITE",
            Price = price,
            Currency = "USD",
            CycleType = BillingCycleType.Month,
            CycleValue = cycleValue,
            IsActive = true
        };
        await SeedAsync(plan);
        return plan;
    }

    private async Task<Subscription> SeedSubscriptionAsync(
        SubscriptionPlan plan,
        string subscriptionNo,
        DateTime? nextBilling,
        SubscriptionStatus status = SubscriptionStatus.Active,
        bool autoRenew = true,
        string? paymentMethodToken = "pm_test")
    {
        var subscription = new Subscription
        {
            SubscriptionNo = subscriptionNo,
            // 请求内建的支付单带的是 CurrentUser.Id，而状态机按付款人 == 订阅主校验，
            // 所以订阅主必须就是测试基类里的默认用户
            UserId = TestHelper.DefaultTestUserId,
            PlanId = plan.Id,
            ProductCode = plan.ProductCode,
            Status = status,
            CycleType = plan.CycleType,
            CycleValue = plan.CycleValue,
            StartTime = DateTime.UtcNow.AddMonths(-1),
            NextBillingTime = nextBilling,
            OriginalPrice = plan.Price,
            Currency = plan.Currency,
            AutoRenew = autoRenew,
            ChannelCode = "Null",
            PaymentMethodToken = paymentMethodToken,
            ProviderCustomerId = "cus_test"
        };
        await SeedAsync(subscription);
        return subscription;
    }

    private Task<Result<SubscriptionChangeDto>> RequestChangeAsync(Guid subscriptionId, Guid newPlanId, bool effectiveImmediately = true) =>
        InScopeAsync<ISubscriptionService, Result<SubscriptionChangeDto>>(
            svc => svc.ChangeSubscriptionPlanAsync(subscriptionId, new ChangeSubscriptionPlanDto
            {
                NewPlanId = newPlanId,
                EffectiveImmediately = effectiveImmediately
            }));

    private async Task ExpirePaymentAsync(string businessOrderNo)
    {
        using (var scope = ServiceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
            var payment = ctx.Set<PaymentEntity>().First(p => p.BusinessOrderNo == businessOrderNo);
            payment.ExpireTime = DateTime.UtcNow.AddMinutes(-1);
            await ctx.SaveChangesAsync();
        }

        await InScopeAsync<IPaymentService, Result<int>>(svc => svc.CloseExpiredPaymentsAsync());
    }

    private Task<Result<int>> RenewAsync() =>
        InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.RenewExpiredSubscriptionsAsync());

    private Task<Result<int>> ApplyDueChangesAsync() =>
        InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ApplyDuePlanChangesAsync());

    private async Task<SubscriptionChange?> ReloadChangeAsync(Guid id) => await ReloadAsync<SubscriptionChange>(id);

    private async Task<PaymentEntity?> ReloadPaymentByOrderNoAsync(string businessOrderNo)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<PaymentEntity, Guid>>();
        return await repo.FirstOrDefaultAsync(p => p.BusinessOrderNo == businessOrderNo);
    }

    /// <summary>
    /// 降级到期 → 续费扫描必须**先**结算变更再扣款。顺序反过来，用户在自己已经降级的
    /// 那一期仍被按旧价收钱，而账单与订阅详情页此后显示的是新计划。
    /// </summary>
    [Fact]
    public async Task DueDowngrade_IsAppliedBeforeTheRenewalCharge()
    {
        var pro = await SeedPlanAsync("PRO", 30m);
        var basic = await SeedPlanAsync("BASIC", 10m);
        var sub = await SeedSubscriptionAsync(pro, "SUB-DOWN1", DateTime.UtcNow.AddDays(-1));

        var change = await RequestChangeAsync(sub.Id, basic.Id);
        change.Succeeded.ShouldBeTrue();
        change.Data!.Status.ShouldBe(SubscriptionChangeStatus.Pending);

        var renewed = await RenewAsync();
        renewed.Succeeded.ShouldBeTrue();
        renewed.Data.ShouldBe(1);

        var reloaded = await ReloadAsync<Subscription>(sub.Id);
        reloaded!.PlanId.ShouldBe(basic.Id);
        reloaded.OriginalPrice.ShouldBe(10m);

        (await ReloadChangeAsync(change.Data.Id))!.Status.ShouldBe(SubscriptionChangeStatus.Applied);

        var payment = await ReloadPaymentByOrderNoAsync("SUB-DOWN1");
        payment.ShouldNotBeNull();
        payment!.Status.ShouldBe(PaymentStatus.Succeeded);
        payment.PayableAmount.ShouldBe(10m);
    }

    /// <summary>
    /// 结算变更时**不能**顺手推进 NextBillingTime：那一刻的计费时钟由续费本身推进，
    /// 两处都拨等于一次降级换来两个周期的免费服务。
    /// </summary>
    [Fact]
    public async Task DueDowngrade_AppliedByRenewal_AdvancesTheBillingClockExactlyOneCycle()
    {
        var pro = await SeedPlanAsync("PRO2", 30m);
        var basic = await SeedPlanAsync("BASIC2", 10m);
        var periodEnd = DateTime.UtcNow.AddHours(-1);
        var sub = await SeedSubscriptionAsync(pro, "SUB-DOWN2", periodEnd);

        await RequestChangeAsync(sub.Id, basic.Id);
        await RenewAsync();

        var reloaded = await ReloadAsync<Subscription>(sub.Id);
        // 一个月周期：新的计费时间应落在 periodEnd 之后一个月上下，而不是两个月
        reloaded!.NextBillingTime!.Value.ShouldBeLessThan(DateTime.UtcNow.AddDays(40));
        reloaded.NextBillingTime.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddDays(20));
    }

    /// <summary>
    /// 结算之后该订阅必须重新可以提出变更。此前守卫看到一条永远处于 Pending 的记录，
    /// 于是这条订阅的所有后续变更请求恒 400。
    /// </summary>
    [Fact]
    public async Task AfterTheDueChangeIsApplied_FurtherChangesAreAcceptedAgain()
    {
        var pro = await SeedPlanAsync("PRO3", 30m);
        var basic = await SeedPlanAsync("BASIC3", 10m);
        var mid = await SeedPlanAsync("MID3", 20m);
        var sub = await SeedSubscriptionAsync(pro, "SUB-DOWN3", DateTime.UtcNow.AddDays(-1));

        await RequestChangeAsync(sub.Id, basic.Id);
        await RenewAsync();

        var second = await RequestChangeAsync(sub.Id, mid.Id);
        second.Succeeded.ShouldBeTrue();
        second.Data!.FromPlanId.ShouldBe(basic.Id);
    }

    /// <summary>
    /// 独立扫描覆盖不参与续费的订阅（关掉自动续费、暂停中、逾期）——
    /// 它们同样约定了「周期结束时降级」，不该因为没走续费路径就永远停在 Pending。
    /// </summary>
    [Fact]
    public async Task DueChange_OnASubscriptionThatDoesNotRenew_IsStillApplied()
    {
        var pro = await SeedPlanAsync("PRO4", 30m);
        var basic = await SeedPlanAsync("BASIC4", 10m);
        var periodEnd = DateTime.UtcNow.AddHours(-1);
        var sub = await SeedSubscriptionAsync(pro, "SUB-DOWN4", periodEnd, autoRenew: false);

        var change = await RequestChangeAsync(sub.Id, basic.Id);
        change.Succeeded.ShouldBeTrue();

        var applied = await ApplyDueChangesAsync();
        applied.Succeeded.ShouldBeTrue();
        applied.Data.ShouldBe(1);

        var reloaded = await ReloadAsync<Subscription>(sub.Id);
        reloaded!.PlanId.ShouldBe(basic.Id);
        // 计费时钟原样不动：这里没有发生任何一次收款
        reloaded.NextBillingTime!.Value.ShouldBe(periodEnd, TimeSpan.FromSeconds(1));
        (await ReloadChangeAsync(change.Data!.Id))!.Status.ShouldBe(SubscriptionChangeStatus.Applied);
    }

    /// <summary>未到期的变更不能被提前结算。</summary>
    [Fact]
    public async Task ChangeThatIsNotDueYet_IsLeftPending()
    {
        var pro = await SeedPlanAsync("PRO5", 30m);
        var basic = await SeedPlanAsync("BASIC5", 10m);
        var sub = await SeedSubscriptionAsync(pro, "SUB-DOWN5", DateTime.UtcNow.AddDays(10));

        var change = await RequestChangeAsync(sub.Id, basic.Id);
        change.Succeeded.ShouldBeTrue();

        var applied = await ApplyDueChangesAsync();
        applied.Data.ShouldBe(0);

        (await ReloadAsync<Subscription>(sub.Id))!.PlanId.ShouldBe(pro.Id);
        (await ReloadChangeAsync(change.Data!.Id))!.Status.ShouldBe(SubscriptionChangeStatus.Pending);
    }

    /// <summary>
    /// 已终止的订阅不能被一条旧的待生效变更改写 —— 那会给一个已取消的订阅换上新计划，
    /// 并让它重新看起来像一份活订阅。
    /// </summary>
    [Fact]
    public async Task DueChange_OnACancelledSubscription_IsCancelledNotApplied()
    {
        var pro = await SeedPlanAsync("PRO6", 30m);
        var basic = await SeedPlanAsync("BASIC6", 10m);
        var sub = await SeedSubscriptionAsync(pro, "SUB-DOWN6", DateTime.UtcNow.AddDays(-1));

        var change = await RequestChangeAsync(sub.Id, basic.Id);
        change.Succeeded.ShouldBeTrue();

        await InScopeAsync<ISubscriptionService, Result>(svc =>
            svc.CancelSubscriptionAsync(sub.Id, new CancelSubscriptionDto { Reason = "test", Immediate = true }));

        var applied = await ApplyDueChangesAsync();
        applied.Data.ShouldBe(0);

        (await ReloadAsync<Subscription>(sub.Id))!.PlanId.ShouldBe(pro.Id);
        (await ReloadChangeAsync(change.Data!.Id))!.Status.ShouldBe(SubscriptionChangeStatus.Cancelled);
    }

    /// <summary>
    /// 按比例金额在周期长度算不出来时的兜底：此前返回**新计划全价**，
    /// 于是一次降级会向用户收取新计划的整整一期费用 —— 方向是反的。
    /// 兜底应当等价于「整个周期都还剩着」，即两个计划的差额（降级为负数=信用）。
    /// </summary>
    [Fact]
    public async Task ProrationFallback_ForADegeneratePeriod_KeepsTheDowngradeDirection()
    {
        var pro = await SeedPlanAsync("PRO7", 30m, cycleValue: 0);
        var basic = await SeedPlanAsync("BASIC7", 10m, cycleValue: 0);
        var sub = await SeedSubscriptionAsync(pro, "SUB-DOWN7", DateTime.UtcNow.AddDays(5));

        var preview = await InScopeAsync<ISubscriptionService, Result<SubscriptionChangeDto>>(
            svc => svc.GetPlanChangePreviewAsync(sub.Id, basic.Id));

        preview.Succeeded.ShouldBeTrue();
        preview.Data!.ChangeType.ShouldBe(SubscriptionChangeType.Downgrade);
        preview.Data.ProratedAmount.ShouldBe(-20m);
    }

    // ───────────── 立即升级：等补差款的变更不是「到期变更」 ─────────────

    /// <summary>
    /// 未绑卡的立即升级只能生成一张待支付单回传；在用户付款之前，第六条扫描不得把它当成
    /// 「到期变更」直接应用 —— 那等于补差价一分没收就免费升级，而 30 分钟后支付单过期时
    /// 变更已经是 Applied，过期处理器什么也不做。
    /// </summary>
    [Fact]
    public async Task ImmediateUpgradeAwaitingPayment_IsNotAppliedByTheDueChangeScan()
    {
        var basic = await SeedPlanAsync("BASIC-AW1", 10m);
        var pro = await SeedPlanAsync("PRO-AW1", 30m);
        var sub = await SeedSubscriptionAsync(basic, "SUB-AW1", DateTime.UtcNow.AddDays(15), paymentMethodToken: null);

        var change = await RequestChangeAsync(sub.Id, pro.Id);
        change.Succeeded.ShouldBeTrue();
        change.Data!.Status.ShouldBe(SubscriptionChangeStatus.AwaitingPayment);
        change.Data.Payment.ShouldNotBeNull();

        var applied = await ApplyDueChangesAsync();
        applied.Data.ShouldBe(0);

        (await ReloadAsync<Subscription>(sub.Id))!.PlanId.ShouldBe(basic.Id);
        (await ReloadChangeAsync(change.Data.Id))!.Status.ShouldBe(SubscriptionChangeStatus.AwaitingPayment);
    }

    /// <summary>
    /// 续费扫描在扣款前结算到期变更，同样不得把等补差款的升级结算掉。
    /// 等钱的变更只会在周期中间产生（到期那一刻补差为 0），所以先建好变更，再把周期拨到已到期。
    /// </summary>
    [Fact]
    public async Task ImmediateUpgradeAwaitingPayment_IsNotAppliedByTheRenewalScan()
    {
        var basic = await SeedPlanAsync("BASIC-AW2", 10m);
        var pro = await SeedPlanAsync("PRO-AW2", 30m);
        var sub = await SeedSubscriptionAsync(basic, "SUB-AW2", DateTime.UtcNow.AddDays(15), paymentMethodToken: null);

        var change = await RequestChangeAsync(sub.Id, pro.Id);
        change.Succeeded.ShouldBeTrue();
        change.Data!.Status.ShouldBe(SubscriptionChangeStatus.AwaitingPayment);

        // 用户没付款，时间走到了周期末
        using (var scope = ServiceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
            var entity = ctx.Set<Subscription>().First(s => s.Id == sub.Id);
            entity.NextBillingTime = DateTime.UtcNow.AddMinutes(-1);
            await ctx.SaveChangesAsync();
        }

        await RenewAsync();

        var reloaded = await ReloadAsync<Subscription>(sub.Id);
        reloaded!.PlanId.ShouldBe(basic.Id);
        reloaded.OriginalPrice.ShouldBe(10m);
        (await ReloadChangeAsync(change.Data!.Id))!.Status.ShouldBe(SubscriptionChangeStatus.AwaitingPayment);
    }

    /// <summary>
    /// 绑了卡但渠道不能无人值守扣款：ChargeOffSessionAsync 在建单之前就返回失败，一条支付事件都没有。
    /// 变更不能悬在一个永远等不到回流的状态里 —— 当场取消并把失败告诉调用方。
    /// </summary>
    [Fact]
    public async Task ImmediateUpgrade_WhenTheChannelCannotChargeOffSession_DoesNotLeaveAChangeBehind()
    {
        var basic = await SeedPlanAsync("BASIC-AW3", 10m);
        var pro = await SeedPlanAsync("PRO-AW3", 30m);
        var sub = await SeedSubscriptionAsync(basic, "SUB-AW3", DateTime.UtcNow.AddDays(15));
        using (var scope = ServiceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
            var entity = ctx.Set<Subscription>().First(s => s.Id == sub.Id);
            entity.ChannelCode = "Offline";
            await ctx.SaveChangesAsync();
        }

        var change = await RequestChangeAsync(sub.Id, pro.Id);
        change.Succeeded.ShouldBeFalse();
        change.Code.ShouldBe(400);
        change.Message.ShouldBe(ErrorCodes.PaymentOffSessionNotSupported);

        using var readScope = ServiceProvider.CreateScope();
        var changes = readScope.ServiceProvider.GetRequiredService<IRepository<SubscriptionChange, Guid>>();
        var leftover = await changes.ToListAsync(c => c.SubscriptionId == sub.Id);
        leftover.ShouldAllBe(c => c.Status == SubscriptionChangeStatus.Cancelled);
        (await ReloadAsync<Subscription>(sub.Id))!.PlanId.ShouldBe(basic.Id);
    }

    /// <summary>待支付的补差单过期 → 变更取消、计划不动。</summary>
    [Fact]
    public async Task ProrationPaymentExpired_CancelsTheAwaitingChange()
    {
        var basic = await SeedPlanAsync("BASIC-AW4", 10m);
        var pro = await SeedPlanAsync("PRO-AW4", 30m);
        var sub = await SeedSubscriptionAsync(basic, "SUB-AW4", DateTime.UtcNow.AddDays(15), paymentMethodToken: null);

        var change = await RequestChangeAsync(sub.Id, pro.Id);
        change.Succeeded.ShouldBeTrue();

        await ExpirePaymentAsync("SUB-AW4");

        (await ReloadChangeAsync(change.Data!.Id))!.Status.ShouldBe(SubscriptionChangeStatus.Cancelled);
        (await ReloadAsync<Subscription>(sub.Id))!.PlanId.ShouldBe(basic.Id);

        // 取消之后该订阅必须重新可以提出变更
        (await RequestChangeAsync(sub.Id, pro.Id)).Succeeded.ShouldBeTrue();
    }

    /// <summary>正向：绑了卡的立即升级 off-session 扣到补差款 → 变更生效。</summary>
    [Fact]
    public async Task ImmediateUpgrade_WithASavedPaymentMethod_ChargesTheProrationAndApplies()
    {
        var basic = await SeedPlanAsync("BASIC-AW5", 10m);
        var pro = await SeedPlanAsync("PRO-AW5", 30m);
        var sub = await SeedSubscriptionAsync(basic, "SUB-AW5", DateTime.UtcNow.AddDays(15));

        var change = await RequestChangeAsync(sub.Id, pro.Id);
        change.Succeeded.ShouldBeTrue();
        change.Data!.ProratedAmount.ShouldBeGreaterThan(0m);

        (await ReloadChangeAsync(change.Data.Id))!.Status.ShouldBe(SubscriptionChangeStatus.Applied);
        var reloaded = await ReloadAsync<Subscription>(sub.Id);
        reloaded!.PlanId.ShouldBe(pro.Id);
        reloaded.OriginalPrice.ShouldBe(30m);

        var payment = await ReloadPaymentByOrderNoAsync("SUB-AW5");
        payment!.Status.ShouldBe(PaymentStatus.Succeeded);
        payment.PayableAmount.ShouldBe(change.Data.ProratedAmount);
    }

    /// <summary>等补差款的变更与待生效的变更一样，都算「已有待生效变更」。</summary>
    [Fact]
    public async Task WhileAChangeAwaitsPayment_AnotherChangeIsRefused()
    {
        var basic = await SeedPlanAsync("BASIC-AW6", 10m);
        var pro = await SeedPlanAsync("PRO-AW6", 30m);
        var mid = await SeedPlanAsync("MID-AW6", 20m);
        var sub = await SeedSubscriptionAsync(basic, "SUB-AW6", DateTime.UtcNow.AddDays(15), paymentMethodToken: null);

        (await RequestChangeAsync(sub.Id, pro.Id)).Succeeded.ShouldBeTrue();

        var second = await RequestChangeAsync(sub.Id, mid.Id);
        second.Succeeded.ShouldBeFalse();
        second.Message.ShouldBe(ErrorCodes.SubscriptionChangePending);
    }

    /// <summary>用户在付款前反悔：等补差款的变更可以取消。</summary>
    [Fact]
    public async Task AnAwaitingPaymentChange_CanBeCancelledByTheUser()
    {
        var basic = await SeedPlanAsync("BASIC-AW7", 10m);
        var pro = await SeedPlanAsync("PRO-AW7", 30m);
        var sub = await SeedSubscriptionAsync(basic, "SUB-AW7", DateTime.UtcNow.AddDays(15), paymentMethodToken: null);

        var change = await RequestChangeAsync(sub.Id, pro.Id);
        var cancelled = await InScopeAsync<ISubscriptionService, Result>(
            svc => svc.CancelPendingChangeAsync(change.Data!.Id));

        cancelled.Succeeded.ShouldBeTrue();
        (await ReloadChangeAsync(change.Data!.Id))!.Status.ShouldBe(SubscriptionChangeStatus.Cancelled);
    }

    // ───────────── 计费模型：立即升级保留计费锚点 ─────────────

    /// <summary>
    /// 补差公式按「剩余比例 × 差价」只覆盖本期剩下的那一段，因此升级之后计费时钟必须原地不动：
    /// 此前付费回流把 NextBillingTime 重置成「现在 + 整周期」，用户付 diff×r 却拿到从现在起
    /// 完整的一个新周期，每次立即升级都少收 新价×(1−r)。
    /// </summary>
    [Fact]
    public async Task ImmediateUpgrade_KeepsTheBillingAnchor()
    {
        var basic = await SeedPlanAsync("BASIC-AN1", 10m);
        var pro = await SeedPlanAsync("PRO-AN1", 30m);
        var anchor = DateTime.UtcNow.AddDays(15);
        var sub = await SeedSubscriptionAsync(basic, "SUB-AN1", anchor);

        var change = await RequestChangeAsync(sub.Id, pro.Id);
        change.Succeeded.ShouldBeTrue();

        var reloaded = await ReloadAsync<Subscription>(sub.Id);
        reloaded!.PlanId.ShouldBe(pro.Id);
        reloaded.NextBillingTime!.Value.ShouldBe(anchor, TimeSpan.FromSeconds(1));
        (await ReloadChangeAsync(change.Data!.Id))!.Status.ShouldBe(SubscriptionChangeStatus.Applied);
    }

    /// <summary>
    /// 周期末尾补差四舍五入到 0：此前免费升级还顺带把计费时钟推后一整个周期，
    /// 本该发生的续费扣款被整个跳过。锚点保留之后，续费扫描照常在到期那一刻按新价扣款。
    /// </summary>
    [Fact]
    public async Task ImmediateUpgrade_AtPeriodEnd_DoesNotSkipTheRenewalCharge()
    {
        var basic = await SeedPlanAsync("BASIC-AN2", 10m);
        var pro = await SeedPlanAsync("PRO-AN2", 30m);
        var sub = await SeedSubscriptionAsync(basic, "SUB-AN2", DateTime.UtcNow.AddMinutes(-1));

        var change = await RequestChangeAsync(sub.Id, pro.Id);
        change.Succeeded.ShouldBeTrue();
        change.Data!.ProratedAmount.ShouldBe(0m);
        change.Data.Status.ShouldBe(SubscriptionChangeStatus.Applied);

        var afterChange = await ReloadAsync<Subscription>(sub.Id);
        afterChange!.PlanId.ShouldBe(pro.Id);
        afterChange.NextBillingTime!.Value.ShouldBeLessThan(DateTime.UtcNow);

        (await RenewAsync()).Data.ShouldBe(1);

        var payment = await ReloadPaymentByOrderNoAsync("SUB-AN2");
        payment.ShouldNotBeNull();
        payment!.Status.ShouldBe(PaymentStatus.Succeeded);
        payment.PayableAmount.ShouldBe(30m);
        (await ReloadAsync<Subscription>(sub.Id))!.NextBillingTime!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddDays(20));
    }

    /// <summary>
    /// 后台已经抢到这条订阅（扣款在途）时，变更计划答 409 —— 与取消同一把锁、同一个条件。
    /// 否则同一秒里续费按旧价扣、升级把计划换掉，用户以旧价拿到新计划一整期。
    /// </summary>
    [Fact]
    public async Task ChangePlan_RefusesWhileBillingIsClaimed()
    {
        var basic = await SeedPlanAsync("BASIC-AN3", 10m);
        var pro = await SeedPlanAsync("PRO-AN3", 30m);
        var sub = await SeedSubscriptionAsync(basic, "SUB-AN3", DateTime.UtcNow.AddDays(15));
        using (var scope = ServiceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
            var entity = ctx.Set<Subscription>().First(s => s.Id == sub.Id);
            entity.BillingLockedUntil = DateTime.UtcNow.AddMinutes(5);
            await ctx.SaveChangesAsync();
        }

        var change = await RequestChangeAsync(sub.Id, pro.Id);
        change.Succeeded.ShouldBeFalse();
        change.Code.ShouldBe(409);
        change.Message.ShouldBe(ErrorCodes.SubscriptionBillingInProgress);
    }

    /// <summary>变更处理完成后必须释放计费锁，否则后台扫描要白等一个锁窗口。</summary>
    [Fact]
    public async Task ChangePlan_ReleasesTheBillingLockWhenDone()
    {
        var basic = await SeedPlanAsync("BASIC-AN4", 10m);
        var pro = await SeedPlanAsync("PRO-AN4", 30m);
        var sub = await SeedSubscriptionAsync(basic, "SUB-AN4", DateTime.UtcNow.AddDays(15), paymentMethodToken: null);

        (await RequestChangeAsync(sub.Id, pro.Id)).Succeeded.ShouldBeTrue();

        (await ReloadAsync<Subscription>(sub.Id))!.BillingLockedUntil.ShouldBeNull();
    }

    /// <summary>
    /// 试用期内升级不收补差（试用期本来一分钱没付，补差是在向用户收一段他没买过的东西），
    /// 计划立即换、试用截止不动，转正那一刻按新价收。
    /// </summary>
    [Fact]
    public async Task UpgradeDuringTrial_IsFreeAndKeepsTheTrialEnd()
    {
        var basic = await SeedPlanAsync("BASIC-AN5", 10m);
        var pro = await SeedPlanAsync("PRO-AN5", 30m);
        var trialEnd = DateTime.UtcNow.AddDays(7);
        var sub = await SeedSubscriptionAsync(basic, "SUB-AN5", trialEnd, status: SubscriptionStatus.Trial);
        using (var scope = ServiceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
            var entity = ctx.Set<Subscription>().First(s => s.Id == sub.Id);
            entity.TrialEndTime = trialEnd;
            await ctx.SaveChangesAsync();
        }

        var change = await RequestChangeAsync(sub.Id, pro.Id);
        change.Succeeded.ShouldBeTrue();
        change.Data!.ProratedAmount.ShouldBe(0m);
        change.Data.Status.ShouldBe(SubscriptionChangeStatus.Applied);

        var reloaded = await ReloadAsync<Subscription>(sub.Id);
        reloaded!.PlanId.ShouldBe(pro.Id);
        reloaded.Status.ShouldBe(SubscriptionStatus.Trial);
        reloaded.NextBillingTime!.Value.ShouldBe(trialEnd, TimeSpan.FromSeconds(1));
        (await ReloadPaymentByOrderNoAsync("SUB-AN5")).ShouldBeNull();
    }

    /// <summary>
    /// 试用期内提出的降级到期日就是试用截止日。转正扫描必须像续费扫描一样**先**结算变更再扣款，
    /// 否则第一个付费周期跑在新计划上、却按旧计划的价格收 —— 降级多收、约定升级少收，
    /// 而事后第六条扫描把计划换掉，账单与订阅详情页对不上且没有任何一条日志能看出来。
    /// </summary>
    [Fact]
    public async Task TrialConversion_AppliesTheDuePlanChangeBeforeCharging()
    {
        var pro = await SeedPlanAsync("PRO-TC1", 30m);
        var basic = await SeedPlanAsync("BASIC-TC1", 10m);
        var trialEnd = DateTime.UtcNow.AddHours(-1);
        var sub = await SeedSubscriptionAsync(pro, "SUB-TC1", trialEnd, status: SubscriptionStatus.Trial);
        using (var scope = ServiceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
            var entity = ctx.Set<Subscription>().First(s => s.Id == sub.Id);
            entity.TrialEndTime = trialEnd;
            await ctx.SaveChangesAsync();
        }

        // 降级不立即生效：Pending，EffectiveDate = NextBillingTime = 试用截止日
        var change = await RequestChangeAsync(sub.Id, basic.Id);
        change.Succeeded.ShouldBeTrue();
        change.Data!.Status.ShouldBe(SubscriptionChangeStatus.Pending);

        var converted = await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ConvertDueTrialsAsync());
        converted.Succeeded.ShouldBeTrue();
        converted.Data.ShouldBe(1);

        var reloaded = await ReloadAsync<Subscription>(sub.Id);
        reloaded!.PlanId.ShouldBe(basic.Id);
        reloaded.OriginalPrice.ShouldBe(10m);
        reloaded.Status.ShouldBe(SubscriptionStatus.Active);
        (await ReloadChangeAsync(change.Data.Id))!.Status.ShouldBe(SubscriptionChangeStatus.Applied);

        var payment = await ReloadPaymentByOrderNoAsync("SUB-TC1");
        payment.ShouldNotBeNull();
        payment!.Status.ShouldBe(PaymentStatus.Succeeded);
        payment.PayableAmount.ShouldBe(10m);
        reloaded.PaidAmount.ShouldBe(10m);
    }
}
