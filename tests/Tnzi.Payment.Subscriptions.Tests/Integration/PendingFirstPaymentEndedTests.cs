namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 首期单以「没收到钱」结束（失败 / 过期）之后，那条 Pending 订阅不再占着「同产品唯一有效订阅」的名额。
/// </summary>
/// <remarks>
/// 此前首期失败或过期的回流对 Initial 什么都不做（注释写着「允许用户重试」），而全模块没有为 Pending 重建首期支付的入口：
/// 重试扣款要求 PastDue、恢复不收 Pending、父模块用户面拒绝建订阅单。于是用户在收银台放弃一次，
/// 再订同一产品就永远得到 400 <c>SUBSCRIPTION_PRODUCT_ALREADY_SUBSCRIBED</c>，计划也因这些僵尸 Pending 永远删不掉。
/// </remarks>
public class PendingFirstPaymentEndedTests : SubscriptionsIntegrationTestBase
{
    private async Task<SubscriptionPlan> SeedPlanAsync(string? productCode = null)
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

    private Task<Result<SubscriptionCreateResultDto>> CreateAsync(SubscriptionPlan plan) =>
        InScopeAsync<ISubscriptionService, Result<SubscriptionCreateResultDto>>(
            svc => svc.CreateSubscriptionAsync(new CreateSubscriptionDto { PlanId = plan.Id, ChannelCode = "Null" }));

    private Task<Result> ReportFirstPaymentAsync(string tradeNo, PaymentStatus status) =>
        InScopeAsync<IPaymentService, Result>(svc => svc.HandleCallbackAsync(new PaymentCallbackDto
        {
            ChannelCode = "Null",
            Parameters = new Dictionary<string, string>
            {
                ["trade_no"] = tradeNo,
                ["amount"] = "30",
                ["status"] = status.ToString(),
                ["event_id"] = Guid.NewGuid().ToString("N")
            }
        }));

    private async Task SetPaymentStatusAsync(string tradeNo, PaymentStatus status)
    {
        using var scope = ServiceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
        var payment = await ctx.Set<PaymentEntity>().FirstAsync(p => p.TradeNo == tradeNo);
        payment.Status = status;
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task AFailedFirstPayment_EndsThePendingSubscription()
    {
        var plan = await SeedPlanAsync();
        var created = (await CreateAsync(plan)).Data!;

        (await ReportFirstPaymentAsync(created.Payment!.TradeNo, PaymentStatus.Failed)).Succeeded.ShouldBeTrue();

        var ended = await ReloadAsync<Subscription>(created.Subscription.Id);
        ended!.Status.ShouldBe(SubscriptionStatus.Cancelled);
        ended.AutoRenew.ShouldBeFalse();
        ended.EndTime.ShouldNotBeNull();
        ended.CancelTime.ShouldNotBeNull();
        ended.NextBillingTime.ShouldBeNull();
        ended.CancelReason.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>★ 放弃收银台的那位用户：首期单过期之后，同一产品可以重新开通，计划也可以删除。</summary>
    [Fact]
    public async Task AfterTheFirstPaymentExpires_TheSameProductCanBeSubscribedAgain()
    {
        var plan = await SeedPlanAsync("pro");
        var created = (await CreateAsync(plan)).Data!;

        var expired = await InScopeAsync<ISubscriptionService, Result>(svc => svc.ApplyPaymentFailedAsync(new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.Initial,
            SubscriptionId = created.Subscription.Id,
            SubscriptionNo = created.Subscription.SubscriptionNo,
            PayerUserId = TestHelper.DefaultTestUserId,
            PaymentTradeNo = created.Payment!.TradeNo,
            FailReason = "Payment order expired"
        }));
        expired.Succeeded.ShouldBeTrue(expired.Message);

        (await ReloadAsync<Subscription>(created.Subscription.Id))!.Status.ShouldBe(SubscriptionStatus.Cancelled);

        var again = await CreateAsync(plan);
        again.Succeeded.ShouldBeTrue(again.Message);
        again.Data!.Subscription.Status.ShouldBe(SubscriptionStatus.Pending);
    }

    /// <summary>
    /// 渠道给的失败原因拼上前缀后收进 500 码元的列宽：截断点落在代理对中间时整个字符一起舍掉。
    /// </summary>
    /// <remarks>
    /// 孤立的高代理项会让严格 UTF-8 的驱动（Npgsql）拒绝这次写入：订阅收不了口，同一产品再也开通不了。
    /// SQLite 不检查，所以这里断言落进去的字符串。
    /// </remarks>
    [Fact]
    public async Task AFailReasonCutInsideASurrogatePair_IsStoredWithoutALoneSurrogate()
    {
        const string prefix = "First payment not completed. ";
        var plan = await SeedPlanAsync();
        var created = (await CreateAsync(plan)).Data!;
        // 第 500 个码元是 emoji 的前半个
        var failReason = new string('a', 499 - prefix.Length) + "\U0001F600tail";

        var failed = await InScopeAsync<ISubscriptionService, Result>(svc => svc.ApplyPaymentFailedAsync(new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.Initial,
            SubscriptionId = created.Subscription.Id,
            SubscriptionNo = created.Subscription.SubscriptionNo,
            PayerUserId = TestHelper.DefaultTestUserId,
            PaymentTradeNo = created.Payment!.TradeNo,
            FailReason = failReason
        }));
        failed.Succeeded.ShouldBeTrue(failed.Message);

        var ended = await ReloadAsync<Subscription>(created.Subscription.Id);
        ended!.Status.ShouldBe(SubscriptionStatus.Cancelled);
        ended.CancelReason.ShouldBe(prefix + new string('a', 499 - prefix.Length));
    }

    /// <summary>从没开始过的订阅被这样收口之后不能借恢复变成在跑的订阅：那是白送一期。</summary>
    [Fact]
    public async Task ASubscriptionEndedByAnUnpaidFirstPayment_CannotBeResumed()
    {
        var plan = await SeedPlanAsync();
        var created = (await CreateAsync(plan)).Data!;
        (await ReportFirstPaymentAsync(created.Payment!.TradeNo, PaymentStatus.Cancelled)).Succeeded.ShouldBeTrue();

        var resumed = await InScopeAsync<ISubscriptionService, Result>(svc => svc.ResumeSubscriptionAsync(created.Subscription.Id));

        resumed.Succeeded.ShouldBeFalse();
        resumed.Message.ShouldBe(ErrorCodes.SubscriptionCannotResume);
        (await ReloadAsync<Subscription>(created.Subscription.Id))!.Status.ShouldBe(SubscriptionStatus.Cancelled);
    }

    /// <summary>
    /// 修复之前落下的僵尸 Pending（首期单早已过期，回流什么都没做）：再订同一产品时把它收口，而不是永远答「已订阅」。
    /// </summary>
    [Fact]
    public async Task APendingWhoseFirstPaymentAlreadyEnded_DoesNotBlockANewSubscription()
    {
        var plan = await SeedPlanAsync("pro");
        var stale = (await CreateAsync(plan)).Data!;
        await SetPaymentStatusAsync(stale.Payment!.TradeNo, PaymentStatus.Expired);

        var again = await CreateAsync(plan);

        again.Succeeded.ShouldBeTrue(again.Message);
        (await ReloadAsync<Subscription>(stale.Subscription.Id))!.Status.ShouldBe(SubscriptionStatus.Cancelled);
    }

    /// <summary>首期单还能付（或已经付掉、激活在路上）的 Pending 照旧占着名额：那是一条活的开通。</summary>
    [Theory]
    [InlineData(PaymentStatus.Processing)]
    [InlineData(PaymentStatus.Succeeded)]
    public async Task APendingWhoseFirstPaymentIsStillLive_StillCountsAsSubscribed(PaymentStatus firstPaymentStatus)
    {
        var plan = await SeedPlanAsync("pro");
        var live = (await CreateAsync(plan)).Data!;
        await SetPaymentStatusAsync(live.Payment!.TradeNo, firstPaymentStatus);

        var again = await CreateAsync(plan);

        again.Succeeded.ShouldBeFalse();
        again.Message.ShouldBe(ErrorCodes.SubscriptionProductAlreadySubscribed);
        (await ReloadAsync<Subscription>(live.Subscription.Id))!.Status.ShouldBe(SubscriptionStatus.Pending);
    }

    /// <summary>已经激活的订阅收到一笔迟到的首期失败：什么都不改。</summary>
    [Fact]
    public async Task AFirstPaymentFailureAfterActivation_ChangesNothing()
    {
        var plan = await SeedPlanAsync();
        var created = (await CreateAsync(plan)).Data!;
        (await ReportFirstPaymentAsync(created.Payment!.TradeNo, PaymentStatus.Succeeded)).Succeeded.ShouldBeTrue();
        (await ReloadAsync<Subscription>(created.Subscription.Id))!.Status.ShouldBe(SubscriptionStatus.Active);

        await InScopeAsync<ISubscriptionService, Result>(svc => svc.ApplyPaymentFailedAsync(new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.Initial,
            SubscriptionId = created.Subscription.Id,
            SubscriptionNo = created.Subscription.SubscriptionNo,
            PayerUserId = TestHelper.DefaultTestUserId,
            PaymentTradeNo = "PAY-LATE",
            FailReason = "late"
        }));

        (await ReloadAsync<Subscription>(created.Subscription.Id))!.Status.ShouldBe(SubscriptionStatus.Active);
    }
}
