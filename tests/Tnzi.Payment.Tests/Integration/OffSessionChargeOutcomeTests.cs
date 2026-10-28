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
/// 无人值守扣款只有在确定没收到钱时才落失败；渠道说「处理中」、或这次调用的结果无从得知，支付保持处理中等渠道落定。
/// </summary>
/// <remarks>
/// 此前一切不是「已收款」的结果都落 Failed 并发失败事件：订阅降级 PastDue、下一轮换一个流水号再扣（幂等键跟流水号走），
/// 而第一笔稍后入账（PayPal capture 从 PENDING 变 COMPLETED）时撞上 Failed 终态，同一期扣了两次。
/// </remarks>
public class OffSessionChargeOutcomeTests : PaymentIntegrationTestBase
{
    private const string Channel = "Settling";

    private readonly Mock<IPaymentProvider> _channel = new();
    private readonly EventRecorder _events = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        _channel.SetupGet(p => p.ChannelCode).Returns(Channel);
        _channel.SetupGet(p => p.ChannelName).Returns(Channel);
        _channel.SetupGet(p => p.SupportsOffSessionCharge).Returns(true);
        services.AddScoped(_ => _channel.Object);
        services.Configure<PaymentOptions>(o => o.Channels[Channel] = new ChannelOptions { Enabled = true, Currency = "USD" });

        services.AddSingleton(_events);
        services.AddScoped<IEventHandler<PaymentCompletedEvent>>(sp => sp.GetRequiredService<EventRecorder>());
        services.AddScoped<IEventHandler<PaymentFailedEvent>>(sp => sp.GetRequiredService<EventRecorder>());
    }

    private void ChannelAnswers(Result<PaymentProviderChargeResult> answer) =>
        _channel.Setup(p => p.ChargeOffSessionAsync(It.IsAny<PaymentProviderChargeDto>())).ReturnsAsync(answer);

    private Task<Result<PaymentDto>> ChargeAsync(string orderNo) =>
        InScopeAsync<IPaymentService, Result<PaymentDto>>(svc => svc.ChargeOffSessionAsync(new OffSessionChargeDto
        {
            BusinessOrderNo = orderNo,
            BusinessType = BusinessType.Order,
            Amount = 30m,
            Currency = "USD",
            ChannelCode = Channel,
            PaymentMethodToken = "pm_1",
            UserId = Guid.NewGuid()
        }));

    private async Task<PaymentEntity> PaymentOfAsync(string orderNo)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<Tnzi.Domain.Repositories.IRepository<PaymentEntity, Guid>>();
        return (await repo.FirstOrDefaultAsync(p => p.BusinessOrderNo == orderNo))!;
    }

    private Task<Result> SettleAsync(string tradeNo, string status) =>
        InScopeAsync<IPaymentService, Result>(svc => svc.HandleCallbackAsync(new PaymentCallbackDto
        {
            ChannelCode = "Null",
            Parameters = new Dictionary<string, string>
            {
                ["trade_no"] = tradeNo,
                ["amount"] = "30",
                ["status"] = status,
                ["event_id"] = Guid.NewGuid().ToString("N")
            }
        }));

    [Fact]
    public async Task AChargeTheChannelReportsAsProcessing_StaysOpenUntilTheChannelSettlesIt()
    {
        ChannelAnswers(Result.Success(new PaymentProviderChargeResult
        {
            TradeNo = "ignored",
            ExternalTradeNo = "CAP-PENDING",
            Status = PaymentStatus.Processing,
            PaidAmount = 30m
        }));

        var charged = await ChargeAsync("ORDER-PEND");

        charged.Succeeded.ShouldBeTrue(charged.Message);
        charged.Data!.Status.ShouldBe(PaymentStatus.Processing);
        var payment = await PaymentOfAsync("ORDER-PEND");
        payment.Status.ShouldBe(PaymentStatus.Processing);
        payment.ExternalTradeNo.ShouldBe("CAP-PENDING");
        payment.ExpireTime!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddHours(PaymentConstants.OffSessionSettlementHours - 1));
        _events.Failed.ShouldBeEmpty();
        _events.Completed.ShouldBeEmpty();

        // 渠道稍后报告已收款：照常推进为成功并发完成事件
        (await SettleAsync(payment.TradeNo, "succeeded")).Succeeded.ShouldBeTrue();

        (await PaymentOfAsync("ORDER-PEND")).Status.ShouldBe(PaymentStatus.Succeeded);
        _events.Completed.ShouldContain(payment.TradeNo);
        _events.Failed.ShouldBeEmpty();
    }

    /// <summary>渠道返回了失败但没说请求有没有发出去（PayPal 下单之后读不出响应、5xx）：结果未知，按在线收银台的有效期等。</summary>
    [Fact]
    public async Task AChannelFailureOfUnknownOutcome_IsNotRecordedAsADecline()
    {
        ChannelAnswers(Result.Failure<PaymentProviderChargeResult>("upstream timeout", 502));

        var charged = await ChargeAsync("ORDER-UNKNOWN");

        charged.Succeeded.ShouldBeTrue(charged.Message);
        charged.Data!.Status.ShouldBe(PaymentStatus.Processing);
        var payment = await PaymentOfAsync("ORDER-UNKNOWN");
        payment.Status.ShouldBe(PaymentStatus.Processing);
        payment.ExpireTime!.Value.ShouldBeLessThan(DateTime.UtcNow.AddHours(1));
        _events.Failed.ShouldBeEmpty();
    }

    /// <summary>连接中断、HttpClient 超时（抛的是 TaskCanceledException）：结果未知，支付保持处理中。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AChannelThatThrows_LeavesThePaymentOpenRatherThanFailed(bool timeout)
    {
        Exception thrown = timeout
            ? new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException())
            : new HttpRequestException("connection reset");
        _channel.Setup(p => p.ChargeOffSessionAsync(It.IsAny<PaymentProviderChargeDto>())).ThrowsAsync(thrown);
        var orderNo = timeout ? "ORDER-TIMEOUT" : "ORDER-THROW";

        var charged = await ChargeAsync(orderNo);

        charged.Succeeded.ShouldBeTrue(charged.Message);
        var payment = await PaymentOfAsync(orderNo);
        payment.Status.ShouldBe(PaymentStatus.Processing);
        payment.ExpireTime.ShouldNotBeNull();
        _events.Failed.ShouldBeEmpty();
    }

    /// <summary>渠道明确拒付：落失败、发失败事件，渠道侧单号留在支付行上供溯源（回归）。</summary>
    [Fact]
    public async Task ADecline_IsRecordedAsFailedWithTheChannelReference()
    {
        ChannelAnswers(Result.Success(new PaymentProviderChargeResult
        {
            TradeNo = "ignored",
            ExternalTradeNo = "pi_declined",
            Status = PaymentStatus.Failed,
            FailReason = "card_declined"
        }));

        var charged = await ChargeAsync("ORDER-DECLINE");

        charged.Succeeded.ShouldBeFalse();
        var payment = await PaymentOfAsync("ORDER-DECLINE");
        payment.Status.ShouldBe(PaymentStatus.Failed);
        payment.ExternalTradeNo.ShouldBe("pi_declined");
        _events.Failed.ShouldContain(payment.TradeNo);
    }

    /// <summary>渠道声明请求根本没发出去（拿不到访问令牌）：确定没收到钱，落失败。</summary>
    [Fact]
    public async Task AChargeTheChannelNeverSent_IsRecordedAsFailed()
    {
        ChannelAnswers(Result.Failure<PaymentProviderChargeResult>("no access token", 400, ErrorCodes.PaymentOffSessionNotAttempted));

        var charged = await ChargeAsync("ORDER-NOTSENT");

        charged.Succeeded.ShouldBeFalse();
        var payment = await PaymentOfAsync("ORDER-NOTSENT");
        payment.Status.ShouldBe(PaymentStatus.Failed);
        _events.Failed.ShouldContain(payment.TradeNo);
    }

    /// <summary>经钱包渠道保管的凭据扣的款，按渠道收的方式记（PayPal），而不是一律记成信用卡。</summary>
    [Fact]
    public async Task AChargeThroughAWalletOnlyChannel_IsRecordedWithThatWallet()
    {
        _channel.Setup(p => p.IsSupported(PaymentMethod.PayPal)).Returns(true);
        ChannelAnswers(Result.Success(new PaymentProviderChargeResult
        {
            TradeNo = "ignored",
            ExternalTradeNo = "CAP-WALLET",
            Status = PaymentStatus.Succeeded,
            PaidAmount = 30m
        }));

        (await ChargeAsync("ORDER-WALLET")).Succeeded.ShouldBeTrue();

        (await PaymentOfAsync("ORDER-WALLET")).PaymentMethod.ShouldBe(PaymentMethod.PayPal);
    }

    /// <summary>记下收到的支付完成 / 失败事件（按流水号）。</summary>
    public sealed class EventRecorder : IEventHandler<PaymentCompletedEvent>, IEventHandler<PaymentFailedEvent>
    {
        public ConcurrentBag<string> Completed { get; } = [];
        public ConcurrentBag<string> Failed { get; } = [];

        public Task HandleAsync(PaymentCompletedEvent eventData, CancellationToken cancellationToken = default)
        {
            Completed.Add(eventData.TradeNo);
            return Task.CompletedTask;
        }

        public Task HandleAsync(PaymentFailedEvent eventData, CancellationToken cancellationToken = default)
        {
            Failed.Add(eventData.TradeNo);
            return Task.CompletedTask;
        }
    }
}
