namespace Tnzi.Payment.Providers;

/// <summary>
/// 空支付渠道实现（用于测试）
/// </summary>
public class NullProvider : IPaymentProvider
{
    private readonly ILogger<NullProvider> _logger;

    public string ChannelCode => PaymentConstants.NullChannelCode;
    public string ChannelName => "Null Provider (Test)";

    public NullProvider(ILogger<NullProvider> logger)
    {
        _logger = Check.NotNull(logger);
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
            ExternalTradeNo = $"null_{input.TradeNo}",
            PayParams = $"null_secret_{input.TradeNo}",
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

    public Task<Result<PaymentProviderRefundQueryResult>> QueryRefundAsync(string externalRefundNo)
    {
        return Task.FromResult(Result.Success(new PaymentProviderRefundQueryResult
        {
            RefundNo = externalRefundNo,
            ExternalRefundNo = externalRefundNo,
            Status = RefundStatus.Succeeded,
            RefundAmount = 0,
            CompletedTime = DateTime.UtcNow
        }));
    }

    public Task<Result<PaymentProviderCallbackResult>> HandleCallbackAsync(IDictionary<string, string> parameters)
    {
        parameters.TryGetValue("event_id", out var callbackEventId);

        // 支付方式撤销事件（对应 Stripe 的 payment_method.detached / PayPal 的 VAULT.PAYMENT-TOKEN.DELETED）
        if (parameters.TryGetValue("revoked_token", out var revokedToken) && !string.IsNullOrWhiteSpace(revokedToken))
        {
            return Task.FromResult(Result.Success(new PaymentProviderCallbackResult
            {
                EventId = callbackEventId,
                Kind = PaymentCallbackKind.PaymentMethodRevoked,
                PaymentMethodToken = revokedToken
            }));
        }

        parameters.TryGetValue("trade_no", out var tradeNo);
        parameters.TryGetValue("amount", out var amountText);
        // 回调金额一律按 invariant 解析：跟随服务器区域会把 "12.34" 在小数逗号区域解析成 1234
        decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount);

        return Task.FromResult(Result.Success(new PaymentProviderCallbackResult
        {
            TradeNo = tradeNo ?? string.Empty,
            Status = PaymentStatus.Succeeded,
            PaidAmount = amount,
            EventId = callbackEventId
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
            ClientSecret = $"null_secret_{tradeNo}",
            AvailableMethods = ["CreditCard", "Test"]
        }));
    }

    public bool SupportsOffSessionCharge => true;

    public bool SupportsPaymentMethodStorage => true;

    public Task<Result<PaymentProviderSetupResult>> CreateSetupSessionAsync(PaymentProviderSetupDto input)
    {
        return Task.FromResult(Result.Success(new PaymentProviderSetupResult
        {
            SetupId = $"null_seti_{input.UserId:N}",
            ClientSecret = $"null_seti_secret_{input.UserId:N}",
            ProviderCustomerId = input.ProviderCustomerId ?? $"null_cus_{input.UserId:N}"
        }));
    }

    public Task<Result<PaymentProviderPaymentMethodResult>> ResolvePaymentMethodAsync(PaymentProviderResolveMethodDto input)
    {
        if (string.IsNullOrWhiteSpace(input.PaymentMethodToken))
            return Task.FromResult(Result.Failure<PaymentProviderPaymentMethodResult>(ErrorCodes.PaymentMethodNotFound, 400));

        return Task.FromResult(Result.Success(new PaymentProviderPaymentMethodResult
        {
            Token = input.PaymentMethodToken,
            ProviderCustomerId = input.ProviderCustomerId ?? $"null_cus_{input.UserId:N}",
            MethodType = PaymentMethod.CreditCard,
            Brand = "null",
            Last4 = "4242",
            ExpiryMonth = 12,
            ExpiryYear = DateTime.UtcNow.Year + 5
        }));
    }

    public Task<Result> DetachPaymentMethodAsync(PaymentProviderResolveMethodDto input)
    {
        return Task.FromResult(Result.Success());
    }

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
