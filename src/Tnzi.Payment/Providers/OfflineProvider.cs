namespace Tnzi.Payment.Providers;

/// <summary>
/// 线下支付渠道：银行转账、汇款、现金、支票等由人工核对到账的方式。
/// </summary>
/// <remarks>
/// 该渠道没有任何自动化能力，只负责把订单挂成"待确认收款"：
/// 建单即返回 Pending，收款由运营在管理端核对银行流水后调
/// <c>POST /admin/payments/{tradeNo}/confirm</c> 手动确认；退款同样走线下，
/// 由运营在打款后手动登记，因此这里显式拒绝渠道退款而不是假装成功。
/// <para>
/// 与测试渠道（<see cref="NullProvider"/>）的本质区别：Null 会自说自话地宣称"支付成功"，
/// 所以必须被生产门控关掉；Offline 从不自动置成功，任何一笔到账都需要人工凭据，可安全用于生产。
/// </para>
/// </remarks>
public class OfflineProvider : IPaymentProvider
{
    private readonly ILogger<OfflineProvider> _logger;

    public string ChannelCode => PaymentConstants.OfflineChannelCode;
    public string ChannelName => "Offline (Manual Settlement)";

    public OfflineProvider(ILogger<OfflineProvider> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public bool IsSupported(PaymentMethod method)
        => method is PaymentMethod.BankTransfer or PaymentMethod.Offline;

    public Task<Result<PaymentProviderOrderResult>> CreatePaymentAsync(PaymentProviderCreateDto input)
    {
        _logger.LogInformation(
            "Offline payment order created, awaiting manual confirmation. TradeNo: {TradeNo}, Amount: {Amount} {Currency}",
            input.TradeNo, input.Amount, input.Currency);

        return Task.FromResult(Result.Success(new PaymentProviderOrderResult
        {
            TradeNo = input.TradeNo,
            ExternalTradeNo = null,
            PayParams = null,
            PayUrl = null,
            ExpireTime = input.ExpireTime
        }));
    }

    /// <summary>
    /// 线下渠道没有可查询的外部状态，本地状态即唯一真值，原样返回 Pending 让调用方不改写本地状态。
    /// </summary>
    public Task<Result<PaymentProviderQueryResult>> QueryPaymentAsync(string tradeNo)
    {
        return Task.FromResult(Result.Success(new PaymentProviderQueryResult
        {
            TradeNo = tradeNo,
            Status = PaymentStatus.Pending
        }));
    }

    public Task<Result<PaymentProviderQueryResult>> SyncOrderAsync(string tradeNo)
        => QueryPaymentAsync(tradeNo);

    public Task<Result<PaymentProviderRefundResult>> RefundAsync(PaymentProviderRefundDto input)
    {
        // 明确失败优于假装成功：线下退款必须有人真的把钱打回去
        _logger.LogWarning("Offline channel cannot execute refunds automatically. RefundNo: {RefundNo}", input.RefundNo);
        return Task.FromResult(Result.Failure<PaymentProviderRefundResult>(ErrorCodes.PaymentChannelNotSupported, 400));
    }

    public Task<Result<PaymentProviderRefundQueryResult>> QueryRefundAsync(string externalRefundNo)
        => Task.FromResult(Result.Failure<PaymentProviderRefundQueryResult>(ErrorCodes.PaymentChannelNotSupported, 400));

    public Task<Result<PaymentProviderCallbackResult>> HandleCallbackAsync(IDictionary<string, string> parameters)
        => Task.FromResult(Result.Failure<PaymentProviderCallbackResult>(ErrorCodes.PaymentChannelNotSupported, 400));

    /// <summary>
    /// 线下渠道不接受任何外部回调：始终验签失败，避免有人伪造回调把订单刷成已支付。
    /// </summary>
    public Task<bool> VerifySignatureAsync(IDictionary<string, string> parameters)
        => Task.FromResult(false);

    public Task<Result<PaymentParamsDto>> GetPaymentParamsAsync(string tradeNo)
    {
        return Task.FromResult(Result.Success(new PaymentParamsDto
        {
            TradeNo = tradeNo,
            AvailableMethods = [nameof(PaymentMethod.BankTransfer), nameof(PaymentMethod.Offline)]
        }));
    }
}
