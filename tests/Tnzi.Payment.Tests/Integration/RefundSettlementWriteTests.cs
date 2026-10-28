using Tnzi.Payment.Dtos;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Services;
using Tnzi.Results;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 退款落定成功、回写支付的退款状态时，只写状态这一列。
/// </summary>
/// <remarks>
/// 退款执行读出的那份支付在渠道退款请求（一次网络往返）之前，更早的建单路径里它甚至读在本笔退款占用额度之前。
/// 此前回写状态时把这份快照整行写回：<c>ReservedRefundAmount</c> 被拨回读时的值（第一笔退款落定之后归零），
/// 支付行上的额度占用这道并发超退防线就此失效；渠道往返期间别处写下的列也一并丢掉。
/// </remarks>
public class RefundSettlementWriteTests : PaymentIntegrationTestBase
{
    private async Task<Guid> SeedSucceededPaymentAsync(string tradeNo, decimal paidAmount)
    {
        var payment = new PaymentEntity
        {
            TradeNo = tradeNo,
            BusinessOrderNo = "ORDER-" + tradeNo,
            BusinessType = BusinessType.Order,
            OriginalAmount = paidAmount,
            PayableAmount = paidAmount,
            PaidAmount = paidAmount,
            Currency = "USD",
            Status = PaymentStatus.Succeeded,
            ChannelCode = "Null",
            PaymentMethod = PaymentMethod.CreditCard,
            PaidTime = DateTime.UtcNow
        };
        await SeedAsync(payment);
        return payment.Id;
    }

    private Task<Result<RefundDto>> RefundAsync(string tradeNo, decimal amount) =>
        InScopeAsync<IRefundService, Result<RefundDto>>(
            svc => svc.CreateRefundAsync(new CreateRefundDto { TradeNo = tradeNo, RefundAmount = amount, Reason = "test" }));

    [Fact]
    public async Task ASettledRefund_KeepsItsReservationOnThePayment()
    {
        var paymentId = await SeedSucceededPaymentAsync("PAY-SET1", 100m);

        var refund = await RefundAsync("PAY-SET1", 30m);

        refund.Succeeded.ShouldBeTrue(refund.Message);
        refund.Data!.Status.ShouldBe(RefundStatus.Succeeded);
        var payment = await ReloadAsync<PaymentEntity>(paymentId);
        payment!.Status.ShouldBe(PaymentStatus.PartialRefunded);
        payment.ReservedRefundAmount.ShouldBe(30m);
    }

    /// <summary>
    /// 一笔落定的部分退款之后多要了一点：照旧被拒且不留占用，剩下的那部分仍然退得出来。
    /// 占用被拨回 0 时，多要的那一笔会先过了占用这一关、再被求和那一关拒掉，留下一份永不归还的占用，这笔支付从此退不了。
    /// </summary>
    [Fact]
    public async Task AfterASettledPartialRefund_AnOverAskIsRefusedAndTheRemainderStillRefunds()
    {
        var paymentId = await SeedSucceededPaymentAsync("PAY-SET3", 100m);
        (await RefundAsync("PAY-SET3", 30m)).Succeeded.ShouldBeTrue();

        var overAsk = await RefundAsync("PAY-SET3", 80m);

        overAsk.Succeeded.ShouldBeFalse();
        overAsk.Message.ShouldBe(ErrorCodes.PaymentRefundExceedAmount);
        (await ReloadAsync<PaymentEntity>(paymentId))!.ReservedRefundAmount.ShouldBe(30m);

        var remainder = await RefundAsync("PAY-SET3", 70m);

        remainder.Succeeded.ShouldBeTrue(remainder.Message);
        var payment = await ReloadAsync<PaymentEntity>(paymentId);
        payment!.Status.ShouldBe(PaymentStatus.Refunded);
        payment.ReservedRefundAmount.ShouldBe(100m);
    }

    [Fact]
    public async Task AFullRefund_MarksThePaymentRefundedAndKeepsTheReservation()
    {
        var paymentId = await SeedSucceededPaymentAsync("PAY-SET2", 100m);

        (await RefundAsync("PAY-SET2", 100m)).Succeeded.ShouldBeTrue();

        var payment = await ReloadAsync<PaymentEntity>(paymentId);
        payment!.Status.ShouldBe(PaymentStatus.Refunded);
        payment.ReservedRefundAmount.ShouldBe(100m);
    }
}
