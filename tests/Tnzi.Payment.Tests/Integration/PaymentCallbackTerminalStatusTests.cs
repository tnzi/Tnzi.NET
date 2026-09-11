using Tnzi.Payment.Dtos;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Services;
using Tnzi.Results;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 回调带来的终态：成功 / 失败 / **取消**。
/// </summary>
/// <remarks>
/// 取消此前落在 <c>default</c> 分支里被当成中间态忽略：支付永远停在 Pending，
/// 直到过期扫描按 <c>ExpireTime</c> 把它关掉 —— 那可能是几天之后。
/// 在这段时间里，为这笔订单核销掉的优惠券一直被占着，订阅状态机也拿不到任何失败信号。
/// 渠道说「这笔付款被取消了」与「这笔付款失败了」，对下游是同一件事：钱没有收到。
/// </remarks>
public class PaymentCallbackTerminalStatusTests : PaymentIntegrationTestBase
{
    private async Task<Guid> SeedPendingPaymentAsync(string tradeNo)
    {
        var payment = new PaymentEntity
        {
            TradeNo = tradeNo,
            BusinessOrderNo = "ORDER-" + tradeNo,
            BusinessType = BusinessType.Order,
            OriginalAmount = 100m,
            PayableAmount = 100m,
            Currency = "USD",
            Status = PaymentStatus.Pending,
            ChannelCode = "Null",
            PaymentMethod = PaymentMethod.CreditCard,
            ExpireTime = DateTime.UtcNow.AddDays(3)
        };
        await SeedAsync(payment);
        return payment.Id;
    }

    private Task<Result> CallbackAsync(string tradeNo, string status, string? currency = null)
    {
        var parameters = new Dictionary<string, string>
        {
            ["trade_no"] = tradeNo,
            ["amount"] = "100",
            ["status"] = status,
            ["event_id"] = Guid.NewGuid().ToString("N")
        };

        if (currency != null)
            parameters["currency"] = currency;

        return InScopeAsync<IPaymentService, Result>(svc => svc.HandleCallbackAsync(new PaymentCallbackDto
        {
            ChannelCode = "Null",
            Parameters = parameters
        }));
    }

    [Fact]
    public async Task ACancelledCallback_MovesThePaymentToATerminalState()
    {
        var paymentId = await SeedPendingPaymentAsync("PAY-CANCEL");

        (await CallbackAsync("PAY-CANCEL", nameof(PaymentStatus.Cancelled))).Succeeded.ShouldBeTrue();

        // 取消写 Cancelled 而不是 Failed：对账与报表要分得出「付款失败」与「付款人取消」
        (await ReloadAsync<PaymentEntity>(paymentId))!.Status.ShouldBe(PaymentStatus.Cancelled);
    }

    [Fact]
    public async Task AFailedCallback_MovesThePaymentToFailed()
    {
        var paymentId = await SeedPendingPaymentAsync("PAY-FAILED");

        (await CallbackAsync("PAY-FAILED", nameof(PaymentStatus.Failed))).Succeeded.ShouldBeTrue();

        (await ReloadAsync<PaymentEntity>(paymentId))!.Status.ShouldBe(PaymentStatus.Failed);
    }

    [Fact]
    public async Task ASucceededCallback_StillSucceeds()
    {
        var paymentId = await SeedPendingPaymentAsync("PAY-OK");

        (await CallbackAsync("PAY-OK", nameof(PaymentStatus.Succeeded))).Succeeded.ShouldBeTrue();

        (await ReloadAsync<PaymentEntity>(paymentId))!.Status.ShouldBe(PaymentStatus.Succeeded);
    }

    [Fact]
    public async Task AProcessingCallback_LeavesThePaymentPending()
    {
        var paymentId = await SeedPendingPaymentAsync("PAY-PROC");

        (await CallbackAsync("PAY-PROC", nameof(PaymentStatus.Processing))).Succeeded.ShouldBeTrue();

        (await ReloadAsync<PaymentEntity>(paymentId))!.Status.ShouldBe(PaymentStatus.Pending);
    }

    /// <summary>
    /// 金额校验只比数值不比币种，就是把「收到 100 JPY」判成「收到 100 USD」——
    /// 两者差着两个数量级，而这笔支付会被记成完全成功。
    /// </summary>
    [Fact]
    public async Task ASucceededCallbackInAnotherCurrency_IsRejected()
    {
        var paymentId = await SeedPendingPaymentAsync("PAY-CUR-BAD");

        var result = await CallbackAsync("PAY-CUR-BAD", nameof(PaymentStatus.Succeeded), currency: "JPY");

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.PaymentAmountMismatch);
        (await ReloadAsync<PaymentEntity>(paymentId))!.Status.ShouldBe(PaymentStatus.Pending);
    }

    [Fact]
    public async Task ASucceededCallbackInTheSameCurrency_GoesThrough()
    {
        var paymentId = await SeedPendingPaymentAsync("PAY-CUR-OK");

        // 大小写不参与判定：渠道之间的写法不统一（Stripe 报小写）
        (await CallbackAsync("PAY-CUR-OK", nameof(PaymentStatus.Succeeded), currency: "usd")).Succeeded.ShouldBeTrue();

        (await ReloadAsync<PaymentEntity>(paymentId))!.Status.ShouldBe(PaymentStatus.Succeeded);
    }

    /// <summary>
    /// 渠道没报币种时**跳过**比对而不是拒绝：自定义渠道与早期实现都可能不带这个值，
    /// 一律拒绝会让经它们的每一笔付款卡住，代价远大于它挡下的那一类错配。
    /// </summary>
    [Fact]
    public async Task ASucceededCallbackWithoutACurrency_IsStillAccepted()
    {
        var paymentId = await SeedPendingPaymentAsync("PAY-CUR-NONE");

        (await CallbackAsync("PAY-CUR-NONE", nameof(PaymentStatus.Succeeded))).Succeeded.ShouldBeTrue();

        (await ReloadAsync<PaymentEntity>(paymentId))!.Status.ShouldBe(PaymentStatus.Succeeded);
    }
}
