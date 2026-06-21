using Tnzi.Payment.Dtos;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Services;
using Tnzi.Results;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 退款资金安全集成测试（真实 SQLite，验证原子 CAS 与支付状态回写）
/// </summary>
public class RefundIntegrationTests : PaymentIntegrationTestBase
{
    private async Task<Guid> SeedSucceededPaymentAsync(string tradeNo, decimal paidAmount)
    {
        var payment = new PaymentEntity
        {
            TradeNo = tradeNo,
            BusinessOrderNo = "ORDER-" + tradeNo,
            BusinessType = BusinessType.Order,
            OriginalAmount = paidAmount,
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

    private Task<Result<RefundDto>> CreateRefundAsync(CreateRefundDto dto) =>
        InScopeAsync<IRefundService, Result<RefundDto>>(svc => svc.CreateRefundAsync(dto));

    [Fact]
    public async Task FullRefund_WritesBackPaymentStatus_Refunded()
    {
        var paymentId = await SeedSucceededPaymentAsync("PAY-FULL", 100m);

        var result = await CreateRefundAsync(new CreateRefundDto { TradeNo = "PAY-FULL", RefundAmount = 100m, Reason = "full refund" });

        result.Succeeded.ShouldBeTrue();
        var reloaded = await ReloadAsync<PaymentEntity>(paymentId);
        reloaded!.Status.ShouldBe(PaymentStatus.Refunded);
    }

    [Fact]
    public async Task PartialRefund_WritesBackPaymentStatus_PartialRefunded()
    {
        var paymentId = await SeedSucceededPaymentAsync("PAY-PARTIAL", 100m);

        var result = await CreateRefundAsync(new CreateRefundDto { TradeNo = "PAY-PARTIAL", RefundAmount = 40m, Reason = "partial refund" });

        result.Succeeded.ShouldBeTrue();
        var reloaded = await ReloadAsync<PaymentEntity>(paymentId);
        reloaded!.Status.ShouldBe(PaymentStatus.PartialRefunded);
    }

    [Fact]
    public async Task TwoPartialRefunds_ReachingFullAmount_MarkPaymentRefunded()
    {
        var paymentId = await SeedSucceededPaymentAsync("PAY-TWO", 100m);

        var first = await CreateRefundAsync(new CreateRefundDto { TradeNo = "PAY-TWO", RefundAmount = 60m, Reason = "r1" });
        var second = await CreateRefundAsync(new CreateRefundDto { TradeNo = "PAY-TWO", RefundAmount = 40m, Reason = "r2" });

        first.Succeeded.ShouldBeTrue();
        second.Succeeded.ShouldBeTrue();
        var reloaded = await ReloadAsync<PaymentEntity>(paymentId);
        reloaded!.Status.ShouldBe(PaymentStatus.Refunded);
    }

    [Fact]
    public async Task Refund_ExceedingRemainingAmount_IsRejected()
    {
        await SeedSucceededPaymentAsync("PAY-EXCEED", 100m);
        await CreateRefundAsync(new CreateRefundDto { TradeNo = "PAY-EXCEED", RefundAmount = 80m, Reason = "r1" });

        var second = await CreateRefundAsync(new CreateRefundDto { TradeNo = "PAY-EXCEED", RefundAmount = 50m, Reason = "r2" });

        second.Succeeded.ShouldBeFalse();
        second.Message.ShouldBe(ErrorCodes.PaymentRefundExceedAmount);
    }
}
