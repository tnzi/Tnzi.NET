using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.EventBus;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Events;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Options;
using Tnzi.Payment.Providers;
using Tnzi.Payment.Services;
using Tnzi.Results;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 付款人确认之后还要商户再做一步才收得到钱的渠道（PayPal 批准后的 capture）：
/// 服务层只在「付款人要付」的路径上让渠道收款，关单 / 过期 / 已结束的支付只读渠道状态。
/// </summary>
/// <remarks>
/// 此前服务层没有任何「收款」这一步：PayPal 的批准回调落进中间态分支被忽略，手动同步只读，
/// 订单到期作废，付款人以为付了，商户一分钱没收到。
/// </remarks>
public class PaymentFinalizationTests : PaymentIntegrationTestBase
{
    private readonly ScriptedChannelProvider _channel = new();
    private readonly PaymentCloseTerminalEventTests.FailedEventRecorder _failedEvents = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<PaymentOptions>(o =>
            o.Channels[ScriptedChannelProvider.Code] = new ChannelOptions { Enabled = true, Currency = "USD" });
        services.AddScoped<IPaymentProvider>(_ => _channel);

        services.AddSingleton(_failedEvents);
        services.AddScoped<IEventHandler<PaymentFailedEvent>>(sp => sp.GetRequiredService<PaymentCloseTerminalEventTests.FailedEventRecorder>());
    }

    private async Task SeedPaymentAsync(string tradeNo, PaymentStatus status = PaymentStatus.Processing)
    {
        await SeedAsync(new PaymentEntity
        {
            PaidAmount = status == PaymentStatus.Succeeded ? 100m : 0m,
            TradeNo = tradeNo,
            ExternalTradeNo = "scr_" + tradeNo,
            BusinessOrderNo = "ORDER-" + tradeNo,
            BusinessType = BusinessType.Order,
            OriginalAmount = 100m,
            PayableAmount = 100m,
            Currency = "USD",
            Status = status,
            ChannelCode = ScriptedChannelProvider.Code,
            PaymentMethod = PaymentMethod.PayPal,
            ExpireTime = DateTime.UtcNow.AddMinutes(30)
        });
    }

    private async Task<PaymentEntity> LoadAsync(string tradeNo)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<Tnzi.Domain.Repositories.IRepository<PaymentEntity, Guid>>();
        return (await repo.FirstOrDefaultAsync(p => p.TradeNo == tradeNo))!;
    }

    private Task<Result> SyncAsync(string tradeNo) =>
        InScopeAsync<IPaymentService, Result>(svc => svc.SyncOrderAsync(tradeNo));

    private Task<Result> CallbackAsync(string tradeNo, string eventId) =>
        InScopeAsync<IPaymentService, Result>(svc => svc.HandleCallbackAsync(new PaymentCallbackDto
        {
            ChannelCode = ScriptedChannelProvider.Code,
            Parameters = new Dictionary<string, string> { ["trade_no"] = tradeNo, ["event_id"] = eventId }
        }));

    // ---- 付款人回到站点时的同步 ----

    [Fact]
    public async Task SyncingAnOpenPayment_CollectsItAndRecordsTheChannelsReference()
    {
        await SeedPaymentAsync("FN-1");

        (await SyncAsync("FN-1")).Succeeded.ShouldBeTrue();

        _channel.FinalizedTradeNos.ShouldBe(["scr_FN-1"]);
        var payment = await LoadAsync("FN-1");
        payment.Status.ShouldBe(PaymentStatus.Succeeded);
        payment.ExternalTradeNo.ShouldBe("cap_scr_FN-1");
    }

    /// <summary>★ 已经结束的支付只读渠道状态：替一张已关掉的单收钱，等于向一个刚取消的人扣款。</summary>
    [Theory]
    [InlineData(PaymentStatus.Closed)]
    [InlineData(PaymentStatus.Expired)]
    [InlineData(PaymentStatus.Succeeded)]
    public async Task SyncingAPaymentThatHasEnded_NeverCollects(PaymentStatus ended)
    {
        await SeedPaymentAsync("FN-2", ended);

        await SyncAsync("FN-2");

        _channel.FinalizedTradeNos.ShouldBeEmpty();
    }

    /// <summary>
    /// 渠道报取消（后台作废了意图，作废回调还没到或根本没订阅）：与失败走同一条收口路径，
    /// 本地记 <c>Cancelled</c> 并发出失败事件（券也在这条路径上归还），而不是只改一个状态。
    /// </summary>
    [Fact]
    public async Task SyncingAPaymentTheChannelCancelled_EndsItThroughTheFailurePath()
    {
        _channel.FinalizedStatus = PaymentStatus.Cancelled;
        await SeedPaymentAsync("FN-11");

        (await SyncAsync("FN-11")).Succeeded.ShouldBeTrue();

        (await LoadAsync("FN-11")).Status.ShouldBe(PaymentStatus.Cancelled);
        _failedEvents.For("FN-11").ShouldHaveSingleItem();
    }

    /// <summary>
    /// 同步只落中间态：渠道报了别的终态（自定义渠道的 Closed / Expired / Refunded），直接写进去同样绕开还券与事件，
    /// 支付保持未结束，由回调或过期清扫按正规路径收口。渠道参考号照常记下。
    /// </summary>
    [Theory]
    [InlineData(PaymentStatus.Closed)]
    [InlineData(PaymentStatus.Expired)]
    [InlineData(PaymentStatus.Refunded)]
    public async Task ASyncReportingAnotherEndState_LeavesThePaymentOpen(PaymentStatus reported)
    {
        _channel.FinalizedStatus = reported;
        await SeedPaymentAsync("FN-12");

        (await SyncAsync("FN-12")).Succeeded.ShouldBeTrue();

        var payment = await LoadAsync("FN-12");
        payment.Status.ShouldBe(PaymentStatus.Processing);
        payment.ExternalTradeNo.ShouldBe("cap_scr_FN-12");
        _failedEvents.For("FN-12").ShouldBeEmpty();
    }

    /// <summary>同步收下来的钱同样核币种：渠道报了一个别的币种，不记成功。</summary>
    [Fact]
    public async Task ACollectionInAnotherCurrency_IsNotRecordedAsPaid()
    {
        _channel.FinalizedCurrency = "JPY";
        await SeedPaymentAsync("FN-3");

        await SyncAsync("FN-3");

        (await LoadAsync("FN-3")).Status.ShouldBe(PaymentStatus.Processing);
    }

    // ---- 关单 / 过期只读 ----

    [Fact]
    public async Task ClosingAnOpenPayment_NeverCollects()
    {
        _channel.SupportsPaymentCancellation = false;
        _channel.ChannelStatus = PaymentStatus.Processing;
        await SeedPaymentAsync("FN-4");

        (await InScopeAsync<IPaymentService, Result>(svc => svc.ClosePaymentAsync("FN-4", "test"))).Succeeded.ShouldBeTrue();

        _channel.FinalizedTradeNos.ShouldBeEmpty();
        (await LoadAsync("FN-4")).Status.ShouldBe(PaymentStatus.Closed);
    }

    // ---- 「付款人已批准」的回调 ----

    /// <summary>★ 付款人批准了、渠道在等商户收款：回调当场收款并推进，不再被当成中间态忽略。</summary>
    [Fact]
    public async Task AnApprovalCallback_CollectsThePayment()
    {
        _channel.CallbackStatus = PaymentStatus.Processing;
        _channel.CallbackRequiresFinalization = true;
        await SeedPaymentAsync("FN-5");

        (await CallbackAsync("FN-5", "evt-fn-5")).Succeeded.ShouldBeTrue();

        _channel.FinalizedTradeNos.ShouldBe(["scr_FN-5"]);
        (await LoadAsync("FN-5")).Status.ShouldBe(PaymentStatus.Succeeded);
    }

    [Fact]
    public async Task AnApprovalCallback_ForAPaymentThatHasEnded_NeverCollects()
    {
        _channel.CallbackStatus = PaymentStatus.Processing;
        _channel.CallbackRequiresFinalization = true;
        await SeedPaymentAsync("FN-6", PaymentStatus.Closed);

        (await CallbackAsync("FN-6", "evt-fn-6")).Succeeded.ShouldBeTrue();

        _channel.FinalizedTradeNos.ShouldBeEmpty();
        (await LoadAsync("FN-6")).Status.ShouldBe(PaymentStatus.Closed);
    }

    /// <summary>收款请求没走通：答 5xx 且不记已处理，渠道重投的同一条回调照常收款。</summary>
    [Fact]
    public async Task AnApprovalCallback_WhoseCollectionFails_IsRedeliverable()
    {
        _channel.CallbackStatus = PaymentStatus.Processing;
        _channel.CallbackRequiresFinalization = true;
        _channel.FinalizedStatus = null;
        await SeedPaymentAsync("FN-7");

        var first = await CallbackAsync("FN-7", "evt-fn-7");

        first.Succeeded.ShouldBeFalse();
        first.Code.ShouldBe(502);
        (await LoadAsync("FN-7")).Status.ShouldBe(PaymentStatus.Processing);

        _channel.FinalizedStatus = PaymentStatus.Succeeded;
        (await CallbackAsync("FN-7", "evt-fn-7")).Succeeded.ShouldBeTrue();
        (await LoadAsync("FN-7")).Status.ShouldBe(PaymentStatus.Succeeded);
    }

    // ---- 同步只写它该写的那几列 ----

    private async Task UpdatePaymentAsync(string tradeNo, Func<IQueryable<PaymentEntity>, Task> change)
    {
        using var scope = ServiceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<PaymentTestDbContext>();
        await change(ctx.Set<PaymentEntity>().Where(p => p.TradeNo == tradeNo));
    }

    /// <summary>
    /// ★ 已收款的支付经同步改正渠道参考号（PayPal：订单 id 改成 capture id）时只写那一列：
    /// 渠道往返期间退款在这一行上预留的额度不能被写回旧值，否则额度守卫失效、可以超退。
    /// </summary>
    [Fact]
    public async Task CorrectingTheReferenceOfAPaidPayment_KeepsARefundReservationMadeMeanwhile()
    {
        await SeedPaymentAsync("FN-9", PaymentStatus.Succeeded);
        _channel.ChannelStatus = PaymentStatus.Succeeded;
        _channel.SyncedExternalTradeNo = "cap_FN-9";
        _channel.DuringSync = () => UpdatePaymentAsync("FN-9", q =>
            q.ExecuteUpdateAsync(s => s.SetProperty(p => p.ReservedRefundAmount, 40m)));

        (await SyncAsync("FN-9")).Succeeded.ShouldBeTrue();

        var payment = await LoadAsync("FN-9");
        payment.ExternalTradeNo.ShouldBe("cap_FN-9");
        payment.ReservedRefundAmount.ShouldBe(40m);
    }

    /// <summary>同步回报处理中、记下新参考号的那一刻，回调已经把它推进成成功：不能被写回处理中。</summary>
    [Fact]
    public async Task AnInFlightSync_DoesNotRollBackAPaymentThatSucceededMeanwhile()
    {
        await SeedPaymentAsync("FN-10");
        _channel.FinalizedStatus = PaymentStatus.Processing;
        _channel.DuringSync = () => UpdatePaymentAsync("FN-10", q =>
            q.ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PaymentStatus.Succeeded)
                .SetProperty(p => p.PaidAmount, 100m)));

        (await SyncAsync("FN-10")).Succeeded.ShouldBeTrue();

        var payment = await LoadAsync("FN-10");
        payment.Status.ShouldBe(PaymentStatus.Succeeded);
        payment.PaidAmount.ShouldBe(100m);
    }

    /// <summary>
    /// ★ 收款与关单撞在一起：服务层刚让渠道收了款，渠道往返期间这张单被关掉（付款人在另一个标签页取消了订阅）。
    /// 终态不回退，但这笔钱是本系统自己收下来的，必须在支付行上留下渠道回报供退款核对，
    /// 而不是像「并发路径已经记成功」那样安静地返回。
    /// </summary>
    [Theory]
    [InlineData(PaymentStatus.Closed)]
    [InlineData(PaymentStatus.Expired)]
    public async Task ACollectionThatLandsAfterThePaymentWasClosed_LeavesATrail(PaymentStatus endedMeanwhile)
    {
        await SeedPaymentAsync("FN-13");
        _channel.DuringSync = () => UpdatePaymentAsync("FN-13", q =>
            q.ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, endedMeanwhile)));

        (await SyncAsync("FN-13")).Succeeded.ShouldBeTrue();

        var payment = await LoadAsync("FN-13");
        payment.Status.ShouldBe(endedMeanwhile);
        payment.ChannelResponse.ShouldNotBeNull();
        payment.ChannelResponse.ShouldContain("cap_scr_FN-13");
    }

    /// <summary>同一形态经批准回调发生：回调收款期间单被关掉，同样留痕。</summary>
    [Fact]
    public async Task AnApprovalCallbackWhoseCollectionLandsAfterAClose_LeavesATrail()
    {
        _channel.CallbackStatus = PaymentStatus.Processing;
        _channel.CallbackRequiresFinalization = true;
        await SeedPaymentAsync("FN-14");
        _channel.DuringSync = () => UpdatePaymentAsync("FN-14", q =>
            q.ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PaymentStatus.Closed)));

        (await CallbackAsync("FN-14", "evt-fn-14")).Succeeded.ShouldBeTrue();

        var payment = await LoadAsync("FN-14");
        payment.Status.ShouldBe(PaymentStatus.Closed);
        payment.ChannelResponse.ShouldNotBeNull();
        payment.ChannelResponse.ShouldContain("cap_scr_FN-14");
    }

    /// <summary>并发路径已经把它记成功（回调与同步同时到）：照旧幂等返回，渠道回报不覆盖那一方写下的。</summary>
    [Fact]
    public async Task ACollectionThatLandsAfterAnotherPathRecordedSuccess_StaysIdempotent()
    {
        await SeedPaymentAsync("FN-15");
        _channel.DuringSync = () => UpdatePaymentAsync("FN-15", q =>
            q.ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PaymentStatus.Succeeded)
                .SetProperty(p => p.PaidAmount, 100m)
                .SetProperty(p => p.ChannelResponse, "recorded-by-the-callback")));

        (await SyncAsync("FN-15")).Succeeded.ShouldBeTrue();

        var payment = await LoadAsync("FN-15");
        payment.Status.ShouldBe(PaymentStatus.Succeeded);
        payment.ChannelResponse.ShouldBe("recorded-by-the-callback");
    }

    /// <summary>普通的中间态回调（没要求收款）照旧不动本地状态，也不打渠道。</summary>
    [Fact]
    public async Task AnOrdinaryProcessingCallback_DoesNotCollect()
    {
        _channel.CallbackStatus = PaymentStatus.Processing;
        await SeedPaymentAsync("FN-8");

        (await CallbackAsync("FN-8", "evt-fn-8")).Succeeded.ShouldBeTrue();

        _channel.FinalizedTradeNos.ShouldBeEmpty();
        (await LoadAsync("FN-8")).Status.ShouldBe(PaymentStatus.Processing);
    }
}
