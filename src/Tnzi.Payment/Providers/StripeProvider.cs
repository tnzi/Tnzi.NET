using Stripe;
using PaymentMethodEnum = Tnzi.Payment.Metadata.PaymentMethod;

namespace Tnzi.Payment.Providers;

/// <summary>
/// Stripe支付渠道实现
/// </summary>
public class StripeProvider : IPaymentProvider
{
    private readonly IOptions<StripeOptions> _options;
    private readonly ILogger<StripeProvider> _logger;
    private StripeClient? _stripeClient;

    public string ChannelCode => "Stripe";
    public string ChannelName => "Stripe";

    public StripeProvider(IOptions<StripeOptions> options, ILogger<StripeProvider> logger)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    private StripeClient GetClient()
    {
        if (_stripeClient == null)
        {
            _stripeClient = new StripeClient(_options.Value.SecretKey);
        }
        return _stripeClient;
    }

    public bool IsSupported(PaymentMethodEnum method)
    {
        return method switch
        {
            PaymentMethodEnum.CreditCard or PaymentMethodEnum.DebitCard or PaymentMethodEnum.ApplePay or PaymentMethodEnum.GooglePay => true,
            _ => false
        };
    }

    public async Task<Result<PaymentProviderOrderResult>> CreatePaymentAsync(PaymentProviderCreateDto input)
    {
        try
        {
            var client = GetClient();
            var service = new PaymentIntentService(client);

            var options = new PaymentIntentCreateOptions
            {
                Amount = (long)(input.Amount * 100), // Stripe uses cents
                Currency = input.Currency.ToLower(),
                Metadata = new Dictionary<string, string>
                {
                    { "TradeNo", input.TradeNo },
                    { "BusinessOrderNo", input.BusinessOrderNo }
                },
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                {
                    Enabled = true
                }
            };

            if (!string.IsNullOrEmpty(input.ReturnUrl))
            {
                options.Confirm = true;
                options.ReturnUrl = input.ReturnUrl;
            }

            var paymentIntent = await service.CreateAsync(options);

            _logger.LogInformation("Stripe payment intent created. TradeNo: {TradeNo}, IntentId: {IntentId}",
                input.TradeNo, paymentIntent.Id);

            return Result.Success(new PaymentProviderOrderResult
            {
                TradeNo = input.TradeNo,
                ExternalTradeNo = paymentIntent.Id,
                PayParams = paymentIntent.ClientSecret,
                ExpireTime = input.ExpireTime,
            });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe payment creation failed. TradeNo: {TradeNo}", input.TradeNo);
            return Result.Failure<PaymentProviderOrderResult>(ErrorCodes.StripePaymentFailed, 400);
        }
    }

    public async Task<Result<PaymentProviderQueryResult>> QueryPaymentAsync(string tradeNo)
    {
        try
        {
            var client = GetClient();
            var service = new PaymentIntentService(client);

            var searchResult = await service.SearchAsync(new PaymentIntentSearchOptions
            {
                Query = $"metadata['TradeNo']:'{tradeNo}'"
            });

            var intent = searchResult.Data.FirstOrDefault();
            if (intent == null)
            {
                return Result.Failure<PaymentProviderQueryResult>("Payment intent not found.");
            }

            return Result.Success(new PaymentProviderQueryResult
            {
                TradeNo = tradeNo,
                ExternalTradeNo = intent.Id,
                Status = MapStripeStatus(intent.Status),
                Amount = intent.Amount / 100m,
                PaidTime = intent.Status == "succeeded" ? DateTime.UtcNow : null
            });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe payment query failed. TradeNo: {TradeNo}", tradeNo);
            return Result.Failure<PaymentProviderQueryResult>(ErrorCodes.StripePaymentQueryFailed, 400);
        }
    }

    public async Task<Result<PaymentProviderRefundResult>> RefundAsync(PaymentProviderRefundDto input)
    {
        try
        {
            var client = GetClient();
            var service = new Stripe.RefundService(client);

            var refundOptions = new RefundCreateOptions
            {
                PaymentIntent = input.ExternalTradeNo ?? input.TradeNo,
                Amount = (long)(input.RefundAmount * 100),
                Reason = RefundReasons.RequestedByCustomer
            };

            var refund = await service.CreateAsync(refundOptions);

            _logger.LogInformation("Stripe refund created. RefundNo: {RefundNo}, Amount: {Amount}",
                input.RefundNo, input.RefundAmount);

            return Result.Success(new PaymentProviderRefundResult
            {
                RefundNo = input.RefundNo,
                ExternalRefundNo = refund.Id,
                RefundAmount = input.RefundAmount,
                Status = refund.Status == "succeeded" ? RefundStatus.Succeeded : RefundStatus.Refunding,
                CompletedTime = refund.Status == "succeeded" ? DateTime.UtcNow : null
            });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe refund failed. RefundNo: {RefundNo}", input.RefundNo);
            return Result.Failure<PaymentProviderRefundResult>(ErrorCodes.StripeRefundFailed, 400);
        }
    }

    public async Task<Result<PaymentProviderRefundQueryResult>> QueryRefundAsync(string refundNo)
    {
        try
        {
            var client = GetClient();
            var service = new Stripe.RefundService(client);

            var refund = await service.GetAsync(refundNo);

            return Result.Success(new PaymentProviderRefundQueryResult
            {
                RefundNo = refundNo,
                ExternalRefundNo = refund.Id,
                Status = refund.Status == "succeeded" ? RefundStatus.Succeeded : RefundStatus.Refunding,
                RefundAmount = refund.Amount / 100m,
                CompletedTime = refund.Status == "succeeded" ? DateTime.UtcNow : null
            });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe refund query failed. RefundNo: {RefundNo}", refundNo);
            return Result.Failure<PaymentProviderRefundQueryResult>(ErrorCodes.StripeRefundQueryFailed, 400);
        }
    }

    public Task<Result<PaymentProviderCallbackResult>> HandleCallbackAsync(IDictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("__raw_body", out var rawBody) || string.IsNullOrWhiteSpace(rawBody))
            return Task.FromResult(Result.Failure<PaymentProviderCallbackResult>(ErrorCodes.PaymentInvalidSignature, 400));

        try
        {
            var stripeEvent = EventUtility.ParseEvent(rawBody);
            if (stripeEvent.Data.Object is not PaymentIntent paymentIntent)
            {
                return Task.FromResult(Result.Failure<PaymentProviderCallbackResult>(
                    "Stripe callback payload does not contain payment intent.", 400));
            }

            if (!paymentIntent.Metadata.TryGetValue("TradeNo", out var tradeNo) || string.IsNullOrWhiteSpace(tradeNo))
            {
                return Task.FromResult(Result.Failure<PaymentProviderCallbackResult>(
                    "Stripe callback missing TradeNo metadata.", 400));
            }

            return Task.FromResult(Result.Success(new PaymentProviderCallbackResult
            {
                TradeNo = tradeNo,
                ExternalTradeNo = paymentIntent.Id,
                Status = MapStripeStatus(paymentIntent.Status),
                PaidAmount = paymentIntent.AmountReceived > 0 ? paymentIntent.AmountReceived / 100m : paymentIntent.Amount / 100m,
                FailReason = paymentIntent.LastPaymentError?.Message
            }));
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe callback parse failed.");
            return Task.FromResult(Result.Failure<PaymentProviderCallbackResult>(ErrorCodes.PaymentInvalidSignature, 400));
        }
    }

    public Task<bool> VerifySignatureAsync(IDictionary<string, string> parameters)
    {
        if (string.IsNullOrWhiteSpace(_options.Value.WebhookSecret))
            return Task.FromResult(false);

        if (!parameters.TryGetValue("__raw_body", out var rawBody) || string.IsNullOrWhiteSpace(rawBody))
            return Task.FromResult(false);

        if (!parameters.TryGetValue("__stripe_signature", out var signature) || string.IsNullOrWhiteSpace(signature))
            return Task.FromResult(false);

        try
        {
            _ = EventUtility.ConstructEvent(rawBody, signature, _options.Value.WebhookSecret);
            return Task.FromResult(true);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe webhook signature verification failed.");
            return Task.FromResult(false);
        }
    }

    public async Task<Result<PaymentProviderQueryResult>> SyncOrderAsync(string tradeNo)
    {
        // PaymentService 传入 ExternalTradeNo（Stripe PaymentIntent ID，形如 pi_xxx）
        // 直接通过 ID 获取比 metadata 搜索更快更可靠
        if (tradeNo.StartsWith("pi_", StringComparison.Ordinal))
        {
            try
            {
                var client = GetClient();
                var service = new PaymentIntentService(client);
                var intent = await service.GetAsync(tradeNo);

                return Result.Success(new PaymentProviderQueryResult
                {
                    TradeNo = intent.Metadata.TryGetValue("TradeNo", out var internalNo) ? internalNo : tradeNo,
                    ExternalTradeNo = intent.Id,
                    Status = MapStripeStatus(intent.Status),
                    Amount = intent.Amount / 100m,
                    PaidTime = intent.Status == "succeeded" ? DateTime.UtcNow : null
                });
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe sync by PaymentIntent ID failed. Id: {Id}", tradeNo);
                return Result.Failure<PaymentProviderQueryResult>(ErrorCodes.StripePaymentQueryFailed, 400);
            }
        }

        // Fallback: 按内部 TradeNo 通过 metadata 搜索
        return await QueryPaymentAsync(tradeNo);
    }

    public Task<Result<PaymentParamsDto>> GetPaymentParamsAsync(string tradeNo)
    {
        return Task.FromResult(Result.Success(new PaymentParamsDto
        {
            TradeNo = tradeNo,
            ClientSecret = null // Will be set during payment creation
        }));
    }

    public async Task<Result> UpdatePaymentMethodAsync(string subscriptionNo, string paymentMethodId)
    {
        try
        {
            var client = GetClient();
            var service = new Stripe.SubscriptionService(client);

            // 通过 metadata 搜索订阅
            var searchResult = await service.SearchAsync(new SubscriptionSearchOptions
            {
                Query = $"metadata['SubscriptionNo']:'{subscriptionNo}'"
            });

            var subscription = searchResult.Data.FirstOrDefault();
            if (subscription == null)
            {
                _logger.LogWarning("Stripe subscription not found. SubscriptionNo: {SubscriptionNo}", subscriptionNo);
                return Result.Failure(ErrorCodes.SubscriptionNotFound, 404);
            }

            await service.UpdateAsync(subscription.Id, new SubscriptionUpdateOptions
            {
                DefaultPaymentMethod = paymentMethodId
            });

            _logger.LogInformation("Stripe subscription payment method updated. SubscriptionNo: {SubscriptionNo}", subscriptionNo);
            return Result.Success();
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe payment method update failed. SubscriptionNo: {SubscriptionNo}", subscriptionNo);
            return Result.Failure($"Stripe payment method update failed: {ex.Message}", 400);
        }
    }

    public bool SupportsOffSessionCharge => true;

    public async Task<Result<PaymentProviderChargeResult>> ChargeOffSessionAsync(PaymentProviderChargeDto input)
    {
        if (string.IsNullOrWhiteSpace(input.PaymentMethodToken))
            return Result.Failure<PaymentProviderChargeResult>(ErrorCodes.SubscriptionPaymentMethodMissing, 400);

        try
        {
            var client = GetClient();
            var service = new PaymentIntentService(client);

            var options = new PaymentIntentCreateOptions
            {
                Amount = (long)(input.Amount * 100), // Stripe uses cents
                Currency = input.Currency.ToLower(),
                Customer = input.ProviderCustomerId,
                PaymentMethod = input.PaymentMethodToken,
                // 无人值守扣款：立即确认 + 标记为非交互场景，触发已保存卡的链下扣款
                Confirm = true,
                OffSession = true,
                Description = input.Description,
                Metadata = new Dictionary<string, string>
                {
                    { "TradeNo", input.TradeNo },
                    { "BusinessOrderNo", input.BusinessOrderNo }
                }
            };

            var paymentIntent = await service.CreateAsync(options);

            _logger.LogInformation("Stripe off-session charge created. TradeNo: {TradeNo}, IntentId: {IntentId}, Status: {Status}",
                input.TradeNo, paymentIntent.Id, paymentIntent.Status);

            return Result.Success(new PaymentProviderChargeResult
            {
                TradeNo = input.TradeNo,
                ExternalTradeNo = paymentIntent.Id,
                Status = MapStripeStatus(paymentIntent.Status),
                PaidAmount = paymentIntent.AmountReceived > 0 ? paymentIntent.AmountReceived / 100m : input.Amount
            });
        }
        catch (StripeException ex)
        {
            // 链下扣款被拒（如卡需要 3DS / 余额不足）属预期失败，记录原因供降级 PastDue
            _logger.LogWarning(ex, "Stripe off-session charge failed. TradeNo: {TradeNo}", input.TradeNo);
            return Result.Success(new PaymentProviderChargeResult
            {
                TradeNo = input.TradeNo,
                Status = PaymentStatus.Failed,
                FailReason = ex.StripeError?.Message ?? ex.Message
            });
        }
    }

    /// <summary>
    /// 获取 Stripe 客户端（供模块内部使用）
    /// </summary>
    internal StripeClient GetStripeClient() => GetClient();

    private static PaymentStatus MapStripeStatus(string? status)
    {
        return status switch
        {
            "requires_payment_method" or "requires_confirmation" or "requires_action" => PaymentStatus.Processing,
            "succeeded" => PaymentStatus.Succeeded,
            "canceled" => PaymentStatus.Cancelled,
            _ => PaymentStatus.Processing
        };
    }
}
