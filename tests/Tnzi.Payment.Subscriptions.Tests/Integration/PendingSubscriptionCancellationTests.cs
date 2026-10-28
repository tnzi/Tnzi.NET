using Microsoft.Extensions.Logging;

namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 还没付过首期的订阅（Pending）被取消：取消即终结，首期待付单一并关掉，之后既不能恢复、也不能靠自动续费开关复活。
/// </summary>
/// <remarks>
/// <para>
/// 此前非试用开通在首付之前就写好 <c>NextBillingTime = 现在 + 一个周期</c>；取消（立即）保留它，
/// 恢复看到「已付到」在未来，于是不扣款直接回 <c>Active</c>，一分钱没收，白得一个完整周期，
/// 过期后再开一条、再取消、再恢复即可无限循环。「到期后取消」把 Pending 置成 PendingRenewal，
/// 打开自动续费同样直接变 Active。
/// </para>
/// <para>
/// 同一处的另一半：取消从不关闭首期待付单，用户开着收银台在另一个标签页取消、再回来付掉，
/// 钱收到了而订阅仍是 Cancelled，状态机对 Initial 一声不吭，运营拿不到任何退款线索。
/// </para>
/// </remarks>
public class PendingSubscriptionCancellationTests : SubscriptionsIntegrationTestBase
{
    private readonly CapturingLoggerFactory _logs = new();
    private readonly CapturedChannel _capturedChannel = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddSingleton<ILoggerFactory>(_logs);

        // 一个作废不了、而渠道侧已经收到钱的渠道：关单会把这笔记成功而不是关掉
        services.AddScoped<IPaymentProvider>(_ => _capturedChannel);
        services.Configure<PaymentOptions>(o =>
            o.Channels[CapturedChannel.Code] = new ChannelOptions { Enabled = true, Currency = "USD" });
    }

    private async Task<SubscriptionPlan> SeedPlanAsync()
    {
        var plan = new SubscriptionPlan
        {
            PlanCode = $"P{Guid.NewGuid():N}"[..12],
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

    private async Task<SubscriptionCreateResultDto> CreatePendingAsync(string channelCode = "Null")
    {
        var plan = await SeedPlanAsync();
        var created = await InScopeAsync<ISubscriptionService, Result<SubscriptionCreateResultDto>>(
            svc => svc.CreateSubscriptionAsync(new CreateSubscriptionDto { PlanId = plan.Id, ChannelCode = channelCode }));
        created.Succeeded.ShouldBeTrue(created.Message);
        created.Data!.Subscription.Status.ShouldBe(SubscriptionStatus.Pending);
        return created.Data;
    }

    private Task<Result> CancelAsync(Guid id, bool immediate) =>
        InScopeAsync<ISubscriptionService, Result>(
            svc => svc.CancelSubscriptionAsync(id, new CancelSubscriptionDto { Reason = "test", Immediate = immediate }));

    private async Task<PaymentEntity> LoadPaymentAsync(string tradeNo)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<PaymentEntity, Guid>>();
        return (await repo.FirstOrDefaultAsync(p => p.TradeNo == tradeNo))!;
    }

    private Task<Result> PayAsync(string tradeNo) =>
        InScopeAsync<IPaymentService, Result>(svc => svc.HandleCallbackAsync(new PaymentCallbackDto
        {
            ChannelCode = "Null",
            Parameters = new Dictionary<string, string>
            {
                ["trade_no"] = tradeNo,
                ["amount"] = "30",
                ["event_id"] = Guid.NewGuid().ToString("N")
            }
        }));

    /// <summary>「已付到何时」只能由付款写下：开通那一刻什么都没付，它就是空的；首付回流后才从付款时刻起算一个周期。</summary>
    [Fact]
    public async Task ANewSubscription_HasNoPaidThroughDateUntilItsFirstPaymentArrives()
    {
        var created = await CreatePendingAsync();

        (await ReloadAsync<Subscription>(created.Subscription.Id))!.NextBillingTime.ShouldBeNull();

        (await PayAsync(created.Payment!.TradeNo)).Succeeded.ShouldBeTrue();

        var activated = await ReloadAsync<Subscription>(created.Subscription.Id);
        activated!.Status.ShouldBe(SubscriptionStatus.Active);
        activated.NextBillingTime!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddDays(27));
    }

    [Fact]
    public async Task CancellingAPendingSubscription_ClosesItsFirstPayment()
    {
        var created = await CreatePendingAsync();

        (await CancelAsync(created.Subscription.Id, immediate: true)).Succeeded.ShouldBeTrue();

        (await ReloadAsync<Subscription>(created.Subscription.Id))!.Status.ShouldBe(SubscriptionStatus.Cancelled);
        (await LoadPaymentAsync(created.Payment!.TradeNo)).Status.ShouldBe(PaymentStatus.Closed);
    }

    /// <summary>没有「本期」可等：到期后取消一个从没付过款的订阅，同样当场终结。</summary>
    [Fact]
    public async Task ADeferredCancelOfAPendingSubscription_EndsItImmediately()
    {
        var created = await CreatePendingAsync();

        (await CancelAsync(created.Subscription.Id, immediate: false)).Succeeded.ShouldBeTrue();

        var reloaded = await ReloadAsync<Subscription>(created.Subscription.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.Cancelled);
        reloaded.AutoRenew.ShouldBeFalse();
        reloaded.NextBillingTime.ShouldBeNull();
        (await LoadPaymentAsync(created.Payment!.TradeNo)).Status.ShouldBe(PaymentStatus.Closed);
    }

    [Fact]
    public async Task ResumingASubscriptionThatWasNeverPaid_IsRefused()
    {
        var created = await CreatePendingAsync();
        (await CancelAsync(created.Subscription.Id, immediate: true)).Succeeded.ShouldBeTrue();

        var resumed = await InScopeAsync<ISubscriptionService, Result>(svc => svc.ResumeSubscriptionAsync(created.Subscription.Id));

        resumed.Succeeded.ShouldBeFalse();
        resumed.Message.ShouldBe(ErrorCodes.SubscriptionCannotResume);
        (await ReloadAsync<Subscription>(created.Subscription.Id))!.Status.ShouldBe(SubscriptionStatus.Cancelled);
    }

    /// <summary>
    /// 修复前已经落库的形状：一条从没付过款、却停在 PendingRenewal 的订阅（到期后取消了一个 Pending）。
    /// 打开自动续费不能把它变成 Active。
    /// </summary>
    [Fact]
    public async Task TurningAutoRenewBackOn_ForASubscriptionThatWasNeverPaid_DoesNotActivateIt()
    {
        var plan = await SeedPlanAsync();
        var legacy = new Subscription
        {
            SubscriptionNo = $"SUB{Guid.NewGuid():N}",
            UserId = TestHelper.DefaultTestUserId,
            PlanId = plan.Id,
            Status = SubscriptionStatus.PendingRenewal,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            StartTime = DateTime.UtcNow,
            NextBillingTime = DateTime.UtcNow.AddMonths(1),
            OriginalPrice = 30m,
            Currency = "USD",
            AutoRenew = false,
            ChannelCode = "Null"
        };
        await SeedAsync(legacy);

        var toggled = await InScopeAsync<ISubscriptionService, Result>(svc => svc.UpdateAutoRenewAsync(legacy.Id, true));

        toggled.Succeeded.ShouldBeFalse();
        toggled.Message.ShouldBe(ErrorCodes.SubscriptionCannotResume);
        (await ReloadAsync<Subscription>(legacy.Id))!.Status.ShouldBe(SubscriptionStatus.PendingRenewal);

        var resumed = await InScopeAsync<ISubscriptionService, Result>(svc => svc.ResumeSubscriptionAsync(legacy.Id));
        resumed.Succeeded.ShouldBeFalse();
        (await ReloadAsync<Subscription>(legacy.Id))!.Status.ShouldBe(SubscriptionStatus.PendingRenewal);
    }

    /// <summary>首期的钱在取消之后才到（渠道作废不了 / 与取消同时发生）：订阅不复活，但必须留下孤儿付款告警。</summary>
    [Fact]
    public async Task AFirstPaymentArrivingAfterCancellation_IsReportedAsAnOrphanPayment()
    {
        var created = await CreatePendingAsync();
        (await CancelAsync(created.Subscription.Id, immediate: true)).Succeeded.ShouldBeTrue();

        var applied = await InScopeAsync<ISubscriptionService, Result>(svc => svc.ApplyPaymentCompletedAsync(new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.Initial,
            SubscriptionId = created.Subscription.Id,
            SubscriptionNo = created.Subscription.SubscriptionNo,
            PayerUserId = TestHelper.DefaultTestUserId,
            PaymentTradeNo = created.Payment!.TradeNo,
            Amount = 30m,
            Currency = "USD"
        }));

        applied.Succeeded.ShouldBeTrue();
        (await ReloadAsync<Subscription>(created.Subscription.Id))!.Status.ShouldBe(SubscriptionStatus.Cancelled);
        _logs.Logged(LogLevel.Warning, "orphan payment").ShouldBeTrue();
    }

    /// <summary>
    /// 首期的钱在渠道侧已经收到（作废被拒、查到已付）：关单把它记成功、订阅随之激活，取消必须被拒，
    /// 而不是留下一条已取消的订阅外加一笔要人工退的钱。
    /// </summary>
    [Fact]
    public async Task CancellingWhileTheFirstPaymentHasAlreadyBeenCaptured_IsRefused()
    {
        var created = await CreatePendingAsync(CapturedChannel.Code);

        var cancelled = await CancelAsync(created.Subscription.Id, immediate: true);

        cancelled.Succeeded.ShouldBeFalse();
        cancelled.Code.ShouldBe(409);
        (await LoadPaymentAsync(created.Payment!.TradeNo)).Status.ShouldBe(PaymentStatus.Succeeded);
        var reloaded = await ReloadAsync<Subscription>(created.Subscription.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.Active);
        reloaded.BillingLockedUntil.ShouldBeNull();
    }

    /// <summary>作废不了、查询答「已付」的渠道（金额取支付单的应付额）。</summary>
    private sealed class CapturedChannel : IPaymentProvider
    {
        public const string Code = "Captured";

        public string ChannelCode => Code;
        public string ChannelName => "Captured (test)";
        public bool SupportsPaymentCancellation => true;
        public bool IsSupported(PaymentMethod method) => true;

        public Task<Result> CancelPaymentAsync(string tradeNo) =>
            Task.FromResult(Result.Failure("already captured", 400));

        public Task<Result<PaymentProviderOrderResult>> CreatePaymentAsync(PaymentProviderCreateDto input) =>
            Task.FromResult(Result.Success(new PaymentProviderOrderResult
            {
                TradeNo = input.TradeNo,
                ExternalTradeNo = "cap_" + input.TradeNo,
                ExpireTime = input.ExpireTime
            }));

        public Task<Result<PaymentProviderQueryResult>> SyncOrderAsync(string tradeNo) =>
            Task.FromResult(Result.Success(new PaymentProviderQueryResult
            {
                TradeNo = tradeNo,
                ExternalTradeNo = tradeNo,
                Status = PaymentStatus.Succeeded,
                Amount = 0,
                PaidTime = DateTime.UtcNow
            }));

        public Task<Result<PaymentProviderCallbackResult>> HandleCallbackAsync(IDictionary<string, string> parameters) =>
            throw new NotSupportedException();

        public Task<bool> VerifySignatureAsync(IDictionary<string, string> parameters) => Task.FromResult(true);

        public Task<Result<PaymentProviderRefundResult>> RefundAsync(PaymentProviderRefundDto input) =>
            throw new NotSupportedException();

        public Task<Result<PaymentProviderRefundQueryResult>> QueryRefundAsync(string refundNo) =>
            throw new NotSupportedException();

        public Task<Result<PaymentParamsDto>> GetPaymentParamsAsync(string tradeNo) =>
            Task.FromResult(Result.Success(new PaymentParamsDto { TradeNo = tradeNo }));
    }
}
