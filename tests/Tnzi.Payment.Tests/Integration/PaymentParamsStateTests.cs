using Tnzi.Payment.Dtos;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Services;
using Tnzi.Results;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 付款凭据（Stripe 的 client secret 之类）只发给还能付的单。
/// </summary>
/// <remarks>
/// 渠道侧的意图可能比本地状态活得久：Stripe 拒付之后 intent 回到 <c>requires_payment_method</c>，
/// 同一个 client secret 换张卡就能再确认。此前这里不看本地状态，对一笔本地已经失败的续费扣款照样回源发凭据：
/// 订阅主拿它付掉，成功回调撞上失败终态只留一行告警，订阅照样降级并换一笔再扣。
/// </remarks>
public class PaymentParamsStateTests : PaymentIntegrationTestBase
{
    private static readonly Guid Owner = Guid.NewGuid();

    private async Task SeedAsync(string tradeNo, PaymentStatus status)
    {
        await SeedAsync(new PaymentEntity
        {
            TradeNo = tradeNo,
            BusinessOrderNo = "ORDER-" + tradeNo,
            BusinessType = BusinessType.Order,
            OriginalAmount = 30m,
            PayableAmount = 30m,
            Currency = "USD",
            Status = status,
            ChannelCode = "Null",
            PaymentMethod = PaymentMethod.CreditCard,
            UserId = Owner,
            BillingAccountType = BillingAccountTypes.User,
            BillingAccountId = Owner.ToString(),
            ExpireTime = DateTime.UtcNow.AddMinutes(30)
        });
    }

    private Task<Result<PaymentParamsDto>> ParamsAsync(string tradeNo) =>
        InScopeAsync<IPaymentService, Result<PaymentParamsDto>>(svc => svc.GetPaymentParamsAsync(tradeNo, Owner));

    [Theory]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Cancelled)]
    [InlineData(PaymentStatus.Closed)]
    [InlineData(PaymentStatus.Expired)]
    [InlineData(PaymentStatus.Succeeded)]
    [InlineData(PaymentStatus.Refunded)]
    public async Task AnEndedPayment_GetsNoPaymentCredentials(PaymentStatus status)
    {
        var tradeNo = $"PAY-PARAMS-{status}";
        await SeedAsync(tradeNo, status);

        var result = await ParamsAsync(tradeNo);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldBe(ErrorCodes.PaymentNotPayable);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Processing)]
    public async Task AnOpenPayment_StillGetsItsPaymentCredentials(PaymentStatus status)
    {
        var tradeNo = $"PAY-PARAMS-{status}";
        await SeedAsync(tradeNo, status);

        var result = await ParamsAsync(tradeNo);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.ClientSecret.ShouldNotBeNullOrEmpty();
    }
}
