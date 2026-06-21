namespace Tnzi.Payment.Providers;

/// <summary>
/// 空支付渠道实现（用于测试）
/// </summary>
public class NullProvider : IPaymentProvider
{
    private readonly ILogger<NullProvider> _logger;

    public string ChannelCode => "Null";
    public string ChannelName => "Null Provider (Test)";

    public NullProvider(ILogger<NullProvider> logger)
    {
        _logger = logger;
    }

    public bool IsSupported(PaymentMethod method)
    {
        return true; // Supports all methods for testing
    }

    public Task<Result<PaymentProviderOrderResult>> CreatePaymentAsync(PaymentProviderCreateDto input)
    {
        _logger.LogInformation("Null payment created (test mode). TradeNo: {TradeNo}, Amount: {Amount}",
            input.TradeNo, input.Amount);

        return Task.FromResult(Result.Success(new PaymentProviderOrderResult
        {
            TradeNo = input.TradeNo,
            PayParams = $"null_{input.TradeNo}",
            ExpireTime = input.ExpireTime,
        }));
    }

    public Task<Result<PaymentProviderQueryResult>> QueryPaymentAsync(string tradeNo)
    {
        _logger.LogInformation("Null payment query (test mode). TradeNo: {TradeNo}", tradeNo);

        return Task.FromResult(Result.Success(new PaymentProviderQueryResult
        {
            TradeNo = tradeNo,
            Status = PaymentStatus.Processing,
            Amount = 0
        }));
    }

    public Task<Result<PaymentProviderRefundResult>> RefundAsync(PaymentProviderRefundDto input)
    {
        _logger.LogInformation("Null refund (test mode). RefundNo: {RefundNo}, Amount: {Amount}",
            input.RefundNo, input.RefundAmount);

        return Task.FromResult(Result.Success(new PaymentProviderRefundResult
        {
            RefundNo = input.RefundNo,
            ExternalRefundNo = $"null_refund_{input.RefundNo}",
            RefundAmount = input.RefundAmount,
            Status = RefundStatus.Succeeded,
            CompletedTime = DateTime.UtcNow
        }));
    }

    public Task<Result<PaymentProviderRefundQueryResult>> QueryRefundAsync(string refundNo)
    {
        return Task.FromResult(Result.Success(new PaymentProviderRefundQueryResult
        {
            RefundNo = refundNo,
            Status = RefundStatus.Succeeded,
            RefundAmount = 0,
            CompletedTime = DateTime.UtcNow
        }));
    }

    public Task<Result<PaymentProviderCallbackResult>> HandleCallbackAsync(IDictionary<string, string> parameters)
    {
        parameters.TryGetValue("trade_no", out var tradeNo);
        parameters.TryGetValue("amount", out var amountText);
        decimal.TryParse(amountText, out var amount);

        return Task.FromResult(Result.Success(new PaymentProviderCallbackResult
        {
            TradeNo = tradeNo ?? string.Empty,
            Status = PaymentStatus.Succeeded,
            PaidAmount = amount
        }));
    }

    public Task<bool> VerifySignatureAsync(IDictionary<string, string> parameters)
    {
        return Task.FromResult(true); // Always valid in test mode
    }

    public Task<Result<PaymentProviderQueryResult>> SyncOrderAsync(string tradeNo)
    {
        _logger.LogInformation("Null sync order (test mode). TradeNo: {TradeNo}", tradeNo);
        return Task.FromResult(Result.Success(new PaymentProviderQueryResult
        {
            TradeNo = tradeNo,
            Status = PaymentStatus.Processing,
            Amount = 0
        }));
    }

    public Task<Result<PaymentParamsDto>> GetPaymentParamsAsync(string tradeNo)
    {
        return Task.FromResult(Result.Success(new PaymentParamsDto
        {
            TradeNo = tradeNo,
            AvailableMethods = new List<string> { "CreditCard", "Test" }
        }));
    }

    public Task<Result> UpdatePaymentMethodAsync(string subscriptionNo, string paymentMethodId)
    {
        return Task.FromResult(Result.Success());
    }

    public bool SupportsOffSessionCharge => true;

    public Task<Result<PaymentProviderChargeResult>> ChargeOffSessionAsync(PaymentProviderChargeDto input)
    {
        _logger.LogInformation("Null off-session charge (test mode). TradeNo: {TradeNo}, Amount: {Amount}",
            input.TradeNo, input.Amount);

        return Task.FromResult(Result.Success(new PaymentProviderChargeResult
        {
            TradeNo = input.TradeNo,
            ExternalTradeNo = $"null_charge_{input.TradeNo}",
            Status = PaymentStatus.Succeeded,
            PaidAmount = input.Amount
        }));
    }
}
