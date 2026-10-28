using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.Domain.Repositories;
using Tnzi.EventBus;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Entities;
using Tnzi.Payment.Events;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Services;
using Tnzi.Results;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 渠道侧发生的退款与拒付经回调记进本地：在渠道后台手工退的款按渠道退款号幂等地记成一条退款；
/// 本模块发起的那一笔只推进它自己；拒付记状态并发事件。
/// </summary>
public class ChannelRefundAndDisputeTests : PaymentIntegrationTestBase
{
    private const string Intent = "pi_paid_in_channel";

    private readonly ChannelEventRecorder _events = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddSingleton(_events);
        services.AddScoped<IEventHandler<RefundProcessedEvent>>(sp => sp.GetRequiredService<ChannelEventRecorder>());
        services.AddScoped<IEventHandler<PaymentDisputedEvent>>(sp => sp.GetRequiredService<ChannelEventRecorder>());
    }

    private async Task<PaymentEntity> SeedPaidAsync(decimal paid = 100m)
    {
        var payment = new PaymentEntity
        {
            TradeNo = "PAY-" + Guid.NewGuid().ToString("N")[..10],
            ExternalTradeNo = Intent,
            BusinessOrderNo = "ORDER-1",
            BusinessType = BusinessType.Order,
            OriginalAmount = paid,
            PayableAmount = paid,
            PaidAmount = paid,
            Currency = "CAD",
            Status = PaymentStatus.Succeeded,
            ChannelCode = "Null",
            PaymentMethod = PaymentMethod.CreditCard,
            PaidTime = DateTime.UtcNow
        };
        await SeedAsync(payment);
        return payment;
    }

    private Task<Result> CallbackAsync(Dictionary<string, string> parameters)
    {
        parameters.TryAdd("event_id", Guid.NewGuid().ToString("N"));
        return InScopeAsync<IPaymentService, Result>(svc => svc.HandleCallbackAsync(new PaymentCallbackDto
        {
            ChannelCode = "Null",
            Parameters = parameters
        }));
    }

    private Task<Result> ChannelRefundAsync(string refundId, string amount, string status = "Succeeded", string? localRefundNo = null, string intent = Intent)
    {
        var parameters = new Dictionary<string, string>
        {
            ["external_refund_id"] = refundId,
            ["external_trade_no"] = intent,
            ["refund_amount"] = amount,
            ["refund_status"] = status,
            ["currency"] = "cad",
            ["refund_reason"] = "requested_by_customer"
        };
        if (localRefundNo != null)
            parameters["local_refund_no"] = localRefundNo;
        return CallbackAsync(parameters);
    }

    private async Task<List<Refund>> RefundsOfAsync(Guid paymentId)
        => await InScopeAsync<IRepository<Refund, Guid>, List<Refund>>(repo =>
            repo.AsQueryable().AsNoTracking().Where(r => r.PaymentId == paymentId).ToListAsync());

    [Fact]
    public async Task ARefundMadeInTheChannel_IsRecordedOnce_AndMarksThePaymentRefunded()
    {
        var payment = await SeedPaidAsync();

        (await ChannelRefundAsync("re_dashboard_1", "100")).Succeeded.ShouldBeTrue();
        // 同一笔退款的第二次投递（另一个事件 id）
        (await ChannelRefundAsync("re_dashboard_1", "100")).Succeeded.ShouldBeTrue();

        var refund = (await RefundsOfAsync(payment.Id)).ShouldHaveSingleItem();
        refund.Origin.ShouldBe(RefundOrigin.Channel);
        refund.ExternalRefundNo.ShouldBe("re_dashboard_1");
        refund.Status.ShouldBe(RefundStatus.Succeeded);
        refund.RefundAmount.ShouldBe(100m);
        refund.Currency.ShouldBe("CAD");
        refund.Reason.ShouldContain("payment channel");

        var reloaded = (await ReloadAsync<PaymentEntity>(payment.Id))!;
        reloaded.Status.ShouldBe(PaymentStatus.Refunded);
        reloaded.ReservedRefundAmount.ShouldBe(100m, "the refundable amount left must account for money returned in the channel");

        _events.Refunds.ShouldHaveSingleItem().RefundId.ShouldBe(refund.Id);
    }

    [Fact]
    public async Task APendingChannelRefund_SettlesWhenTheChannelReportsTheOutcome()
    {
        var payment = await SeedPaidAsync();

        await ChannelRefundAsync("re_pending", "40", status: "Refunding");
        (await RefundsOfAsync(payment.Id)).ShouldHaveSingleItem().Status.ShouldBe(RefundStatus.Refunding);
        _events.Refunds.ShouldBeEmpty();

        await ChannelRefundAsync("re_pending", "40", status: "Succeeded");

        (await RefundsOfAsync(payment.Id)).ShouldHaveSingleItem().Status.ShouldBe(RefundStatus.Succeeded);
        (await ReloadAsync<PaymentEntity>(payment.Id))!.Status.ShouldBe(PaymentStatus.PartialRefunded);
        _events.Refunds.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task OurOwnRefund_IsNeverRecordedASecondTime()
    {
        var payment = await SeedPaidAsync();
        var created = await InScopeAsync<IRefundService, Result<RefundDto>>(svc => svc.CreateRefundAsync(
            new CreateRefundDto { TradeNo = payment.TradeNo, RefundAmount = 30m, Reason = "goodwill" }));
        created.Succeeded.ShouldBeTrue(created.Message);
        var before = await RefundsOfAsync(payment.Id);

        // Stripe 回报这笔退款（元数据里带着我们的退款号），而渠道退款号与本地记下的不同也不新建
        (await ChannelRefundAsync("re_ours_" + Guid.NewGuid().ToString("N")[..6], "30", localRefundNo: created.Data!.RefundNo)).Succeeded.ShouldBeTrue();

        (await RefundsOfAsync(payment.Id)).Count.ShouldBe(before.Count);
        (await RefundsOfAsync(payment.Id)).ShouldAllBe(r => r.Origin == RefundOrigin.Framework);
    }

    [Fact]
    public async Task ARefundForAPaymentThisAppDoesNotHave_IsAcceptedAndIgnored()
    {
        await SeedPaidAsync();

        // 同一个渠道账户上别的应用的支付：答成功，渠道不再重投
        (await ChannelRefundAsync("re_someone_else", "10", intent: "pi_other_app")).Succeeded.ShouldBeTrue();
        _events.Refunds.ShouldBeEmpty();
    }

    [Fact]
    public async Task ADispute_IsRecorded_PublishedOnEachChange_AndNotReopenedByALateEvent()
    {
        var payment = await SeedPaidAsync();

        Task<Result> DisputeAsync(string status) => CallbackAsync(new Dictionary<string, string>
        {
            ["dispute_id"] = "dp_1",
            ["external_trade_no"] = Intent,
            ["dispute_status"] = status,
            ["dispute_amount"] = "100",
            ["dispute_reason"] = "fraudulent",
            ["currency"] = "cad"
        });

        (await DisputeAsync(nameof(PaymentDisputeStatus.NeedsResponse))).Succeeded.ShouldBeTrue();
        (await DisputeAsync(nameof(PaymentDisputeStatus.NeedsResponse))).Succeeded.ShouldBeTrue();
        (await DisputeAsync(nameof(PaymentDisputeStatus.Lost))).Succeeded.ShouldBeTrue();
        (await DisputeAsync(nameof(PaymentDisputeStatus.UnderReview))).Succeeded.ShouldBeTrue();

        var reloaded = (await ReloadAsync<PaymentEntity>(payment.Id))!;
        reloaded.DisputeId.ShouldBe("dp_1");
        reloaded.DisputeStatus.ShouldBe(PaymentDisputeStatus.Lost, "a late open status must not reopen a closed dispute");
        reloaded.DisputeReason.ShouldBe("fraudulent");
        reloaded.DisputeAmount.ShouldBe(100m);
        reloaded.Status.ShouldBe(PaymentStatus.Succeeded, "a dispute is recorded, it does not rewrite the payment");

        _events.Disputes.Select(e => (e.PreviousStatus, e.Status)).ShouldBe(
        [
            (PaymentDisputeStatus.None, PaymentDisputeStatus.NeedsResponse),
            (PaymentDisputeStatus.NeedsResponse, PaymentDisputeStatus.Lost)
        ], ignoreOrder: false);
        _events.Disputes.ShouldAllBe(e => e.TradeNo == payment.TradeNo && e.Currency == "CAD");
    }

    /// <summary>记下退款与拒付事件（按发生顺序）。</summary>
    public sealed class ChannelEventRecorder : IEventHandler<RefundProcessedEvent>, IEventHandler<PaymentDisputedEvent>
    {
        private readonly ConcurrentQueue<RefundProcessedEvent> _refunds = new();
        private readonly ConcurrentQueue<PaymentDisputedEvent> _disputes = new();

        public IReadOnlyList<RefundProcessedEvent> Refunds => _refunds.Where(e => e.Succeeded).ToList();
        public IReadOnlyList<PaymentDisputedEvent> Disputes => _disputes.ToList();

        public Task HandleAsync(RefundProcessedEvent eventData, CancellationToken cancellationToken = default)
        {
            _refunds.Enqueue(eventData);
            return Task.CompletedTask;
        }

        public Task HandleAsync(PaymentDisputedEvent eventData, CancellationToken cancellationToken = default)
        {
            _disputes.Enqueue(eventData);
            return Task.CompletedTask;
        }
    }
}
