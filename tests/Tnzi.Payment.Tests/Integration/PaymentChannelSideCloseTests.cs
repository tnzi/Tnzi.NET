using Microsoft.Extensions.DependencyInjection;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Options;
using Tnzi.Payment.Providers;
using Tnzi.Payment.Services;
using Tnzi.Results;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 本地关单（用户 / 运维关闭、过期清扫、订阅侧连带关单）必须先让渠道侧的支付意图失效，
/// 或者至少确认渠道侧还没收到钱。
/// </summary>
/// <remarks>
/// 此前关单只是本地一次 CAS 把状态写成 <c>Closed</c>，渠道侧的 PaymentIntent 原样活着。
/// 付款人正开着收银台时本地关了单（订阅侧的续费 / 取消 / 过期现在会自动触发这件事），
/// 他接着把那张单付掉：渠道扣了钱，成功回调到达时本地已是终态，被幂等守卫静静吞掉 ——
/// 没有事件、没有告警、没有退款线索。修复前同一序列至少留下一行 orphan payment 告警。
/// 三层收口：能作废的渠道先作废；不能作废的先查渠道侧状态，已付就记成功而不是关单；
/// 渠道侧读不到时不关（宁可让过期扫描下一轮再来）；实在已关而钱后到的，回调侧留痕并告警。
/// </remarks>
public class PaymentChannelSideCloseTests : PaymentIntegrationTestBase
{
    private readonly ScriptedChannelProvider _channel = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<PaymentOptions>(o =>
            o.Channels[ScriptedChannelProvider.Code] = new ChannelOptions { Enabled = true, Currency = "USD" });
        services.AddScoped<IPaymentProvider>(_ => _channel);
    }

    private async Task<PaymentEntity> SeedProcessingPaymentAsync(string tradeNo, DateTime? expireTime = null)
    {
        var payment = new PaymentEntity
        {
            TradeNo = tradeNo,
            ExternalTradeNo = "scr_" + tradeNo,
            BusinessOrderNo = "ORDER-" + tradeNo,
            BusinessType = BusinessType.Order,
            OriginalAmount = 100m,
            PayableAmount = 100m,
            Currency = "USD",
            Status = PaymentStatus.Processing,
            ChannelCode = ScriptedChannelProvider.Code,
            PaymentMethod = PaymentMethod.CreditCard,
            ExpireTime = expireTime ?? DateTime.UtcNow.AddMinutes(30)
        };
        await SeedAsync(payment);
        return payment;
    }

    private Task<Result> CloseAsync(string tradeNo) =>
        InScopeAsync<IPaymentService, Result>(svc => svc.ClosePaymentAsync(tradeNo, "test"));

    private Task<Result<int>> RunExpiryScanAsync() =>
        InScopeAsync<IPaymentService, Result<int>>(svc => svc.CloseExpiredPaymentsAsync());

    private async Task<PaymentEntity> LoadAsync(string tradeNo)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<Tnzi.Domain.Repositories.IRepository<PaymentEntity, Guid>>();
        return (await repo.FirstOrDefaultAsync(p => p.TradeNo == tradeNo))!;
    }

    // ---- 能作废的渠道 ----

    [Fact]
    public async Task Close_VoidsTheChannelSideOrderBeforeClosingLocally()
    {
        _channel.SupportsPaymentCancellation = true;
        await SeedProcessingPaymentAsync("CS-1");

        (await CloseAsync("CS-1")).Succeeded.ShouldBeTrue();

        _channel.CancelledTradeNos.ShouldContain("scr_CS-1");
        (await LoadAsync("CS-1")).Status.ShouldBe(PaymentStatus.Closed);
    }

    [Fact]
    public async Task Close_WhenTheChannelRefusesToVoidBecauseItAlreadyCaptured_RecordsSuccessInsteadOfClosing()
    {
        _channel.SupportsPaymentCancellation = true;
        _channel.CancelOutcome = Result.Failure("already succeeded", 400);
        _channel.ChannelStatus = PaymentStatus.Succeeded;
        await SeedProcessingPaymentAsync("CS-2");

        var closed = await CloseAsync("CS-2");

        closed.Succeeded.ShouldBeFalse();
        closed.Code.ShouldBe(409);
        var payment = await LoadAsync("CS-2");
        payment.Status.ShouldBe(PaymentStatus.Succeeded);
        payment.PaidAmount.ShouldBe(100m);
    }

    [Fact]
    public async Task Close_WhenVoidFailsAndTheChannelStateCannotBeRead_LeavesTheOrderOpen()
    {
        // 两边都答不上来 = 渠道不可达。宁可留着这张单让过期扫描下一轮再来，也不能在渠道侧仍可付款时本地关掉。
        _channel.SupportsPaymentCancellation = true;
        _channel.CancelOutcome = Result.Failure("network", 400);
        _channel.QueryFails = true;
        await SeedProcessingPaymentAsync("CS-3");

        (await CloseAsync("CS-3")).Succeeded.ShouldBeFalse();

        (await LoadAsync("CS-3")).Status.ShouldBe(PaymentStatus.Processing);
    }

    [Fact]
    public async Task Close_WhenVoidFailsButTheChannelReportsTheOrderDead_ClosesLocally()
    {
        _channel.SupportsPaymentCancellation = true;
        _channel.CancelOutcome = Result.Failure("already canceled", 400);
        _channel.ChannelStatus = PaymentStatus.Cancelled;
        await SeedProcessingPaymentAsync("CS-4");

        (await CloseAsync("CS-4")).Succeeded.ShouldBeTrue();

        (await LoadAsync("CS-4")).Status.ShouldBe(PaymentStatus.Closed);
    }

    // ---- 不能作废的渠道（PayPal 订单没有作废接口）----

    [Fact]
    public async Task Close_WhenTheChannelCannotVoidAndTheOrderIsUnpaid_ClosesLocally()
    {
        _channel.SupportsPaymentCancellation = false;
        _channel.ChannelStatus = PaymentStatus.Processing;
        await SeedProcessingPaymentAsync("CS-5");

        (await CloseAsync("CS-5")).Succeeded.ShouldBeTrue();

        _channel.CancelledTradeNos.ShouldBeEmpty();
        (await LoadAsync("CS-5")).Status.ShouldBe(PaymentStatus.Closed);
    }

    [Fact]
    public async Task Close_WhenTheChannelCannotVoidAndAlreadyCaptured_RecordsSuccessInsteadOfClosing()
    {
        _channel.SupportsPaymentCancellation = false;
        _channel.ChannelStatus = PaymentStatus.Succeeded;
        await SeedProcessingPaymentAsync("CS-6");

        var closed = await CloseAsync("CS-6");

        closed.Succeeded.ShouldBeFalse();
        (await LoadAsync("CS-6")).Status.ShouldBe(PaymentStatus.Succeeded);
    }

    [Fact]
    public async Task Close_WhenTheChannelCannotVoidAndItsStateCannotBeRead_StillClosesLocally()
    {
        // 没有作废杠杆的渠道，留着这张单下一轮也不会多知道什么（渠道把订单清掉后会永远 404）；
        // 关掉之后钱若真的到了，回调侧留痕告警 —— 与「仍可付款」那条放行的残余形态相同。
        _channel.SupportsPaymentCancellation = false;
        _channel.QueryFails = true;
        await SeedProcessingPaymentAsync("CS-7");

        (await CloseAsync("CS-7")).Succeeded.ShouldBeTrue();

        (await LoadAsync("CS-7")).Status.ShouldBe(PaymentStatus.Closed);
    }

    // ---- 过期清扫走同一条路 ----

    [Fact]
    public async Task ExpiryScan_VoidsTheChannelSideOrderBeforeExpiringLocally()
    {
        _channel.SupportsPaymentCancellation = true;
        await SeedProcessingPaymentAsync("CS-8", expireTime: DateTime.UtcNow.AddMinutes(-1));

        (await RunExpiryScanAsync()).Data.ShouldBe(1);

        _channel.CancelledTradeNos.ShouldContain("scr_CS-8");
        (await LoadAsync("CS-8")).Status.ShouldBe(PaymentStatus.Expired);
    }

    [Fact]
    public async Task ExpiryScan_WhenTheChannelAlreadyCaptured_RecordsSuccessInsteadOfExpiring()
    {
        _channel.SupportsPaymentCancellation = true;
        _channel.CancelOutcome = Result.Failure("already succeeded", 400);
        _channel.ChannelStatus = PaymentStatus.Succeeded;
        await SeedProcessingPaymentAsync("CS-9", expireTime: DateTime.UtcNow.AddMinutes(-1));

        (await RunExpiryScanAsync()).Data.ShouldBe(0);

        (await LoadAsync("CS-9")).Status.ShouldBe(PaymentStatus.Succeeded);
    }

    [Fact]
    public async Task ExpiryScan_WhenTheChannelIsUnreachable_LeavesTheOrderForTheNextRound()
    {
        _channel.SupportsPaymentCancellation = true;
        _channel.CancelOutcome = Result.Failure("network", 400);
        _channel.QueryFails = true;
        await SeedProcessingPaymentAsync("CS-10", expireTime: DateTime.UtcNow.AddMinutes(-1));

        (await RunExpiryScanAsync()).Data.ShouldBe(0);

        (await LoadAsync("CS-10")).Status.ShouldBe(PaymentStatus.Processing);
    }

    // ---- 关了之后钱还是到了 ----

    [Fact]
    public async Task ASucceededCallback_ForALocallyClosedPayment_LeavesATrailInsteadOfVanishing()
    {
        _channel.SupportsPaymentCancellation = false;
        _channel.ChannelStatus = PaymentStatus.Processing;
        await SeedProcessingPaymentAsync("CS-11");
        (await CloseAsync("CS-11")).Succeeded.ShouldBeTrue();

        _channel.CallbackStatus = PaymentStatus.Succeeded;
        var callback = await InScopeAsync<IPaymentService, Result>(svc => svc.HandleCallbackAsync(new PaymentCallbackDto
        {
            ChannelCode = ScriptedChannelProvider.Code,
            Parameters = new Dictionary<string, string> { ["trade_no"] = "CS-11", ["event_id"] = "evt-cs-11" }
        }));

        callback.Succeeded.ShouldBeTrue();
        var payment = await LoadAsync("CS-11");
        // 终态不回退：状态仍是 Closed，但这笔「关单之后仍被收走的钱」必须在支付行上留下渠道回报，供退款核对
        payment.Status.ShouldBe(PaymentStatus.Closed);
        payment.ChannelResponse.ShouldNotBeNull();
        payment.ChannelResponse.ShouldContain("scr_CS-11");
        payment.ChannelResponse.ShouldContain($"\"Status\":{(int)PaymentStatus.Succeeded}");
    }
}

/// <summary>
/// 渠道侧状态可脚本化的测试渠道：作废能力、作废结果、查询到的状态、查询是否失败都由用例设定。
/// 直接实现接口而不是 Moq：默认接口成员 Moq 不会执行，而「渠道没实现作废」正是要测的默认路径。
/// </summary>
internal sealed class ScriptedChannelProvider : IPaymentProvider
{
    public const string Code = "Scripted";

    public bool SupportsPaymentCancellation { get; set; }
    public Result CancelOutcome { get; set; } = Result.Success();
    public PaymentStatus ChannelStatus { get; set; } = PaymentStatus.Processing;
    public bool QueryFails { get; set; }
    public PaymentStatus CallbackStatus { get; set; } = PaymentStatus.Succeeded;
    public List<string> CancelledTradeNos { get; } = [];

    public string ChannelCode => Code;
    public string ChannelName => "Scripted (test)";
    public bool IsSupported(PaymentMethod method) => true;

    public Task<Result> CancelPaymentAsync(string tradeNo)
    {
        if (CancelOutcome.Succeeded)
            CancelledTradeNos.Add(tradeNo);
        return Task.FromResult(CancelOutcome);
    }

    public Task<Result<PaymentProviderOrderResult>> CreatePaymentAsync(PaymentProviderCreateDto input) =>
        Task.FromResult(Result.Success(new PaymentProviderOrderResult
        {
            TradeNo = input.TradeNo,
            ExternalTradeNo = "scr_" + input.TradeNo,
            ExpireTime = input.ExpireTime
        }));

    public Task<Result<PaymentProviderQueryResult>> QueryPaymentAsync(string tradeNo) => SyncOrderAsync(tradeNo);

    public Task<Result<PaymentProviderQueryResult>> SyncOrderAsync(string tradeNo) =>
        Task.FromResult(QueryFails
            ? Result.Failure<PaymentProviderQueryResult>("channel unreachable", 400)
            : Result.Success(new PaymentProviderQueryResult
            {
                TradeNo = tradeNo,
                ExternalTradeNo = tradeNo,
                Status = ChannelStatus,
                Amount = 0,
                PaidTime = ChannelStatus == PaymentStatus.Succeeded ? DateTime.UtcNow : null
            }));

    public Task<Result<PaymentProviderCallbackResult>> HandleCallbackAsync(IDictionary<string, string> parameters) =>
        Task.FromResult(Result.Success(new PaymentProviderCallbackResult
        {
            TradeNo = parameters["trade_no"],
            ExternalTradeNo = "scr_" + parameters["trade_no"],
            Status = CallbackStatus,
            PaidAmount = 100m,
            EventId = parameters.TryGetValue("event_id", out var id) ? id : null
        }));

    public Task<bool> VerifySignatureAsync(IDictionary<string, string> parameters) => Task.FromResult(true);

    public Task<Result<PaymentProviderRefundResult>> RefundAsync(PaymentProviderRefundDto input) =>
        throw new NotSupportedException();

    public Task<Result<PaymentProviderRefundQueryResult>> QueryRefundAsync(string refundNo) =>
        throw new NotSupportedException();

    public Task<Result<PaymentParamsDto>> GetPaymentParamsAsync(string tradeNo) =>
        Task.FromResult(Result.Success(new PaymentParamsDto { TradeNo = tradeNo }));
}
