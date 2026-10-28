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

        // 支付方式在渠道侧被更新（对应 Stripe 的 payment_method.updated / automatically_updated）
        if (parameters.TryGetValue("updated_token", out var updatedToken) && !string.IsNullOrWhiteSpace(updatedToken))
        {
            parameters.TryGetValue("last4", out var last4);
            parameters.TryGetValue("brand", out var brand);
            int? expiryYear = parameters.TryGetValue("exp_year", out var yearText) && int.TryParse(yearText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
                ? year
                : null;

            return Task.FromResult(Result.Success(new PaymentProviderCallbackResult
            {
                EventId = callbackEventId,
                Kind = PaymentCallbackKind.PaymentMethodUpdated,
                PaymentMethodToken = updatedToken,
                UpdatedPaymentMethod = new PaymentProviderPaymentMethodResult
                {
                    Token = updatedToken,
                    Brand = brand,
                    Last4 = last4,
                    ExpiryYear = expiryYear
                }
            }));
        }

        // 渠道后台的退款（对应 Stripe 的 charge.refunded / refund.updated）：按渠道交易号找支付
        if (parameters.TryGetValue("external_refund_id", out var externalRefundId) && !string.IsNullOrWhiteSpace(externalRefundId))
        {
            parameters.TryGetValue("external_trade_no", out var refundedTradeNo);
            parameters.TryGetValue("local_refund_no", out var localRefundNo);
            decimal.TryParse(parameters.TryGetValue("refund_amount", out var refundAmountText) ? refundAmountText : null,
                NumberStyles.Number, CultureInfo.InvariantCulture, out var refundAmount);
            var refundStatus = parameters.TryGetValue("refund_status", out var refundStatusText)
                && Enum.TryParse<RefundStatus>(refundStatusText, ignoreCase: true, out var parsedRefundStatus)
                ? parsedRefundStatus
                : RefundStatus.Succeeded;

            return Task.FromResult(Result.Success(new PaymentProviderCallbackResult
            {
                EventId = callbackEventId,
                Kind = PaymentCallbackKind.ExternalRefund,
                ExternalTradeNo = refundedTradeNo,
                Refunds =
                [
                    new PaymentProviderExternalRefund
                    {
                        ExternalRefundNo = externalRefundId,
                        LocalRefundNo = string.IsNullOrWhiteSpace(localRefundNo) ? null : localRefundNo,
                        Amount = refundAmount,
                        Currency = parameters.TryGetValue("currency", out var refundCurrency) ? refundCurrency : null,
                        Status = refundStatus,
                        Reason = parameters.TryGetValue("refund_reason", out var refundReason) ? refundReason : null
                    }
                ]
            }));
        }

        // 拒付（对应 Stripe 的 charge.dispute.*）
        if (parameters.TryGetValue("dispute_id", out var disputeId) && !string.IsNullOrWhiteSpace(disputeId))
        {
            parameters.TryGetValue("external_trade_no", out var disputedTradeNo);
            decimal.TryParse(parameters.TryGetValue("dispute_amount", out var disputeAmountText) ? disputeAmountText : null,
                NumberStyles.Number, CultureInfo.InvariantCulture, out var disputeAmount);
            var disputeStatus = parameters.TryGetValue("dispute_status", out var disputeStatusText)
                && Enum.TryParse<PaymentDisputeStatus>(disputeStatusText, ignoreCase: true, out var parsedDisputeStatus)
                ? parsedDisputeStatus
                : PaymentDisputeStatus.NeedsResponse;

            return Task.FromResult(Result.Success(new PaymentProviderCallbackResult
            {
                EventId = callbackEventId,
                Kind = PaymentCallbackKind.Dispute,
                ExternalTradeNo = disputedTradeNo,
                Dispute = new PaymentProviderDispute
                {
                    DisputeId = disputeId,
                    Status = disputeStatus,
                    Reason = parameters.TryGetValue("dispute_reason", out var disputeReason) ? disputeReason : null,
                    Amount = disputeAmount,
                    Currency = parameters.TryGetValue("currency", out var disputeCurrency) ? disputeCurrency : null
                }
            }));
        }

        parameters.TryGetValue("trade_no", out var tradeNo);
        parameters.TryGetValue("amount", out var amountText);
        // 回调金额一律按 invariant 解析：跟随服务器区域会把 "12.34" 在小数逗号区域解析成 1234
        decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount);

        // 可选的 status 参数：让"渠道回报失败 / 取消"这两条终态也能被端到端走通。
        // 不带它时仍然一律回成功，既有用例一字不差。
        var status = parameters.TryGetValue("status", out var statusText)
            && Enum.TryParse<PaymentStatus>(statusText, ignoreCase: true, out var parsed)
            ? parsed
            : PaymentStatus.Succeeded;

        return Task.FromResult(Result.Success(new PaymentProviderCallbackResult
        {
            TradeNo = tradeNo ?? string.Empty,
            Status = status,
            PaidAmount = amount,
            Currency = parameters.TryGetValue("currency", out var currency) ? currency : null,
            FailReason = status == PaymentStatus.Succeeded ? null : $"Null provider reported {status}",
            EventId = callbackEventId,
            // 可选：模拟「付款时按要求留存了这张卡」（Stripe 的 setup_future_usage）
            SavedPaymentMethodToken = status == PaymentStatus.Succeeded && parameters.TryGetValue("saved_token", out var savedToken) ? savedToken : null
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

    public bool SupportsPaymentCancellation => true;

    public Task<Result> CancelPaymentAsync(string tradeNo)
    {
        _logger.LogInformation("Null payment voided (test mode). TradeNo: {TradeNo}", tradeNo);
        return Task.FromResult(Result.Success());
    }

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
