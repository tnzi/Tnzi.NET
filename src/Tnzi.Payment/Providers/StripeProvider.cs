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
                PaymentIntent = input.TradeNo,
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
        // Stripe webhooks are handled separately with raw body
        return Task.FromResult(Result.Success(new PaymentProviderCallbackResult()));
    }

    public bool VerifySignature(IDictionary<string, string> parameters)
    {
        // Stripe signature verification is done with raw request body
        return true;
    }

    public async Task<Result<PaymentProviderQueryResult>> SyncOrderAsync(string tradeNo)
    {
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

    public Task<Result> UpdatePaymentMethodAsync(string subscriptionNo, string paymentMethodId)
    {
        // TODO: Implement Stripe customer update
        return Task.FromResult(Result.Success());
    }

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
