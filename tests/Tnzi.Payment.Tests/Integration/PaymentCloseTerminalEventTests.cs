using System.Collections.Concurrent;
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
/// 关单与失败、取消、过期一样是「钱没收到」的终态：下游要收到终态事件，同一次关单不论哪条路径先落地，结果都一样。
/// </summary>
/// <remarks>
/// 此前关单只改状态（后来补上了还券），不发任何事件：用户在收银台点「取消」，等着失败 / 过期事件去取消业务单的消费方
/// 永远等不到；订阅的首期单被这样关掉，订阅一直停在 Pending。而同一次关单若被渠道的作废回调抢先（关单先作废了渠道侧的意图），
/// 走的是失败路径（还券 + 失败事件），本地关单反倒答 409：同一个操作，下游看到什么取决于竞态。
/// </remarks>
public class PaymentCloseTerminalEventTests : PaymentIntegrationTestBase
{
    private readonly ScriptedChannelProvider _channel = new();
    private readonly FailedEventRecorder _events = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<PaymentOptions>(o =>
            o.Channels[ScriptedChannelProvider.Code] = new ChannelOptions { Enabled = true, Currency = "USD" });
        services.AddScoped<IPaymentProvider>(_ => _channel);

        services.AddSingleton(_events);
        services.AddScoped<IEventHandler<PaymentFailedEvent>>(sp => sp.GetRequiredService<FailedEventRecorder>());
    }

    private async Task<string> CreateOpenPaymentAsync(string channelCode)
    {
        var created = await InScopeAsync<IPaymentService, Result<PaymentOrderResultDto>>(svc => svc.CreatePaymentAsync(new CreatePaymentDto
        {
            BusinessOrderNo = "ORDER-" + Guid.NewGuid().ToString("N")[..8],
            BusinessType = BusinessType.Order,
            Amount = 100m,
            Currency = "USD",
            ChannelCode = channelCode
        }));
        created.Succeeded.ShouldBeTrue(created.Message);
        return created.Data!.TradeNo;
    }

    private Task<Result> CloseAsync(string tradeNo, string? reason = "customer cancelled at checkout") =>
        InScopeAsync<IPaymentService, Result>(svc => svc.ClosePaymentAsync(tradeNo, reason));

    private Task<Result> ChannelReportsAsync(string tradeNo) =>
        InScopeAsync<IPaymentService, Result>(svc => svc.HandleCallbackAsync(new PaymentCallbackDto
        {
            ChannelCode = ScriptedChannelProvider.Code,
            Parameters = new Dictionary<string, string> { ["trade_no"] = tradeNo, ["event_id"] = Guid.NewGuid().ToString("N") }
        }));

    private async Task<PaymentEntity> LoadAsync(string tradeNo)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<Tnzi.Domain.Repositories.IRepository<PaymentEntity, Guid>>();
        return (await repo.FirstOrDefaultAsync(p => p.TradeNo == tradeNo))!;
    }

    [Fact]
    public async Task ClosingAPayment_AnnouncesItAsEndedUnpaid()
    {
        var tradeNo = await CreateOpenPaymentAsync(PaymentConstants.NullChannelCode);

        (await CloseAsync(tradeNo)).Succeeded.ShouldBeTrue();

        (await LoadAsync(tradeNo)).Status.ShouldBe(PaymentStatus.Closed);
        var announced = _events.For(tradeNo).ShouldHaveSingleItem();
        announced.FailReason.ShouldContain("closed", Case.Insensitive);
        announced.FailReason.ShouldContain("customer cancelled at checkout");
    }

    /// <summary>
    /// 渠道的作废回调在本地关单的条件更新之前落地（关单自己先作废了渠道侧）：这张单已经以「没收到钱」结束、券已还、事件已发，
    /// 关单达成了它要的结果，答成功而不是 409，也不再发第二次终态事件。
    /// </summary>
    [Fact]
    public async Task WhenTheChannelEndsThePaymentWhileItIsBeingClosed_TheCloseSucceedsAndItIsAnnouncedOnce()
    {
        _channel.SupportsPaymentCancellation = false;
        _channel.ChannelStatus = PaymentStatus.Processing;
        var tradeNo = await CreateOpenPaymentAsync(ScriptedChannelProvider.Code);
        _channel.CallbackStatus = PaymentStatus.Cancelled;
        _channel.DuringSync = async () => (await ChannelReportsAsync(tradeNo)).Succeeded.ShouldBeTrue();

        var closed = await CloseAsync(tradeNo);

        closed.Succeeded.ShouldBeTrue(closed.Message);
        (await LoadAsync(tradeNo)).Status.ShouldBe(PaymentStatus.Cancelled);
        _events.For(tradeNo).ShouldHaveSingleItem();
    }

    /// <summary>关单途中钱到了：那不是一次关单，照旧拒绝，也不发终态失败事件。</summary>
    [Fact]
    public async Task WhenThePaymentIsPaidWhileItIsBeingClosed_TheCloseIsRefused()
    {
        _channel.SupportsPaymentCancellation = false;
        _channel.ChannelStatus = PaymentStatus.Processing;
        var tradeNo = await CreateOpenPaymentAsync(ScriptedChannelProvider.Code);
        _channel.CallbackStatus = PaymentStatus.Succeeded;
        _channel.DuringSync = async () => (await ChannelReportsAsync(tradeNo)).Succeeded.ShouldBeTrue();

        var closed = await CloseAsync(tradeNo);

        closed.Succeeded.ShouldBeFalse();
        closed.Code.ShouldBe(409);
        (await LoadAsync(tradeNo)).Status.ShouldBe(PaymentStatus.Succeeded);
        _events.For(tradeNo).ShouldBeEmpty();
    }

    /// <summary>记下收到的支付失败事件。</summary>
    public sealed class FailedEventRecorder : IEventHandler<PaymentFailedEvent>
    {
        private readonly ConcurrentQueue<PaymentFailedEvent> _received = new();

        public IReadOnlyList<PaymentFailedEvent> For(string tradeNo) => _received.Where(e => e.TradeNo == tradeNo).ToList();

        public Task HandleAsync(PaymentFailedEvent eventData, CancellationToken cancellationToken = default)
        {
            _received.Enqueue(eventData);
            return Task.CompletedTask;
        }
    }
}
