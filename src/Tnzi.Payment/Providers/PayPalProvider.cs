using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;

namespace Tnzi.Payment.Providers;

/// <summary>
/// PayPal支付渠道实现
/// </summary>
public class PayPalProvider : IPaymentProvider
{
    private readonly IOptions<PayPalOptions> _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PayPalProvider> _logger;
    private string? _accessToken;
    private DateTime _tokenExpireTime;

    public string ChannelCode => "PayPal";
    public string ChannelName => "PayPal";

    public PayPalProvider(
        IOptions<PayPalOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<PayPalProvider> logger)
    {
        _options = Check.NotNull(options);
        _httpClientFactory = Check.NotNull(httpClientFactory);
        _logger = Check.NotNull(logger);
    }

    public bool IsSupported(PaymentMethod method)
    {
        return method == PaymentMethod.PayPal;
    }

    public async Task<Result<PaymentProviderOrderResult>> CreatePaymentAsync(PaymentProviderCreateDto input)
    {
        try
        {
            var client = CreateHttpClient();
            var accessToken = await GetAccessTokenAsync(client);
            if (accessToken == null)
                return Result.Failure<PaymentProviderOrderResult>("Failed to get PayPal access token.");

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var orderRequest = new
            {
                intent = "CAPTURE",
                purchase_units = new[]
                {
                    new
                    {
                        reference_id = input.TradeNo,
                        custom_id = input.TradeNo,
                        invoice_id = input.TradeNo,
                        description = input.Description,
                        amount = new
                        {
                            currency_code = input.Currency,
                            value = input.Amount.ToString("F2")
                        }
                    }
                },
                application_context = new
                {
                    brand_name = _options.Value.BrandName,
                    landing_page = "NO_PREFERENCE",
                    user_action = "PAY_NOW",
                    return_url = input.ReturnUrl ?? _options.Value.ReturnUrl ?? string.Empty,
                    cancel_url = _options.Value.CancelUrl ?? string.Empty
                }
            };

            var response = await client.PostAsJsonAsync("/v2/checkout/orders", orderRequest);
            var content = await response.Content.ReadFromJsonAsync<PayPalOrderResponse>();

            if (content == null || string.IsNullOrEmpty(content.Id))
                return Result.Failure<PaymentProviderOrderResult>("Failed to create PayPal order.");

            var approvalUrl = content.Links?.FirstOrDefault(l => l.Rel == "approve")?.Href;

            _logger.LogInformation("PayPal order created. TradeNo: {TradeNo}, OrderId: {OrderId}",
                input.TradeNo, content.Id);

            return Result.Success(new PaymentProviderOrderResult
            {
                TradeNo = input.TradeNo,
                ExternalTradeNo = content.Id,
                PayParams = content.Id,
                PayUrl = approvalUrl,
                ExpireTime = input.ExpireTime,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayPal payment creation failed. TradeNo: {TradeNo}", input.TradeNo);
            return Result.Failure<PaymentProviderOrderResult>(ErrorCodes.PayPalPaymentFailed, 400);
        }
    }

    public async Task<Result<PaymentProviderQueryResult>> QueryPaymentAsync(string tradeNo)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(tradeNo))
                return Result.Failure<PaymentProviderQueryResult>("PayPal order id is required.");

            var client = CreateHttpClient();
            var accessToken = await GetAccessTokenAsync(client);
            if (accessToken == null)
                return Result.Failure<PaymentProviderQueryResult>("Failed to get PayPal access token.");

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await client.GetAsync($"/v2/checkout/orders/{tradeNo}");
            if (!response.IsSuccessStatusCode)
                return Result.Failure<PaymentProviderQueryResult>("PayPal order not found.");

            var content = await response.Content.ReadFromJsonAsync<PayPalOrderResponse>();
            if (content == null)
                return Result.Failure<PaymentProviderQueryResult>("Failed to parse PayPal response.");

            return Result.Success(new PaymentProviderQueryResult
            {
                TradeNo = tradeNo,
                ExternalTradeNo = content.Id,
                Status = content.Status == "COMPLETED" ? PaymentStatus.Succeeded : PaymentStatus.Processing,
                Amount = decimal.Parse(
                    content.PurchaseUnits?.FirstOrDefault()?.Amount?.Value ?? "0",
                    System.Globalization.CultureInfo.InvariantCulture),
                PaidTime = content.Status == "COMPLETED" ? DateTime.UtcNow : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayPal payment query failed. TradeNo: {TradeNo}", tradeNo);
            return Result.Failure<PaymentProviderQueryResult>(ErrorCodes.PayPalPaymentQueryFailed, 400);
        }
    }

    public async Task<Result<PaymentProviderRefundResult>> RefundAsync(PaymentProviderRefundDto input)
    {
        try
        {
            var client = CreateHttpClient();
            var accessToken = await GetAccessTokenAsync(client);
            if (accessToken == null)
                return Result.Failure<PaymentProviderRefundResult>("Failed to get PayPal access token.");

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var refundRequest = new
            {
                amount = new
                {
                    currency_code = _options.Value.Currency,
                    value = input.RefundAmount.ToString("F2")
                },
                note_to_payer = input.Reason
            };

            var captureId = input.ExternalTradeNo ?? input.TradeNo;
            var response = await client.PostAsJsonAsync($"/v2/payments/captures/{captureId}/refunds", refundRequest);
            var content = await response.Content.ReadFromJsonAsync<PayPalRefundResponse>();

            _logger.LogInformation("PayPal refund created. RefundNo: {RefundNo}, Amount: {Amount}",
                input.RefundNo, input.RefundAmount);

            return Result.Success(new PaymentProviderRefundResult
            {
                RefundNo = input.RefundNo,
                ExternalRefundNo = content?.Id,
                RefundAmount = input.RefundAmount,
                Status = response.IsSuccessStatusCode ? RefundStatus.Succeeded : RefundStatus.Refunding,
                CompletedTime = response.IsSuccessStatusCode ? DateTime.UtcNow : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayPal refund failed. RefundNo: {RefundNo}", input.RefundNo);
            return Result.Failure<PaymentProviderRefundResult>(ErrorCodes.PayPalRefundFailed, 400);
        }
    }

    public Task<Result<PaymentProviderRefundQueryResult>> QueryRefundAsync(string refundNo)
    {
        // PayPal 退款查询需要 capture ID
        return Task.FromResult(Result.Success(new PaymentProviderRefundQueryResult
        {
            RefundNo = refundNo,
            Status = RefundStatus.Succeeded
        }));
    }

    public Task<Result<PaymentProviderCallbackResult>> HandleCallbackAsync(IDictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("__raw_body", out var rawBody) || string.IsNullOrWhiteSpace(rawBody))
            return Task.FromResult(Result.Failure<PaymentProviderCallbackResult>(ErrorCodes.PaymentInvalidSignature, 400));

        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;
            var eventType = root.TryGetProperty("event_type", out var eventTypeElement)
                ? eventTypeElement.GetString()
                : null;

            var resource = root.TryGetProperty("resource", out var resourceElement)
                ? resourceElement
                : default;

            var tradeNo =
                TryGetNestedString(resource, "purchase_units", 0, "reference_id")
                ?? TryGetNestedString(resource, "custom_id")
                ?? TryGetNestedString(resource, "invoice_id")
                ?? string.Empty;

            var externalTradeNo =
                TryGetNestedString(resource, "id")
                ?? TryGetNestedString(resource, "supplementary_data", "related_ids", "order_id");

            var amountText =
                TryGetNestedString(resource, "amount", "value")
                ?? TryGetNestedString(resource, "seller_receivable_breakdown", "gross_amount", "value");

            decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var paidAmount);

            return Task.FromResult(Result.Success(new PaymentProviderCallbackResult
            {
                TradeNo = tradeNo,
                ExternalTradeNo = externalTradeNo,
                Status = MapPayPalEventStatus(eventType),
                PaidAmount = paidAmount,
                FailReason = eventType
            }));
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse PayPal callback payload.");
            return Task.FromResult(Result.Failure<PaymentProviderCallbackResult>(ErrorCodes.PaymentInvalidSignature, 400));
        }
    }

    public async Task<bool> VerifySignatureAsync(IDictionary<string, string> parameters)
    {
        if (string.IsNullOrWhiteSpace(_options.Value.WebhookId))
            return false;

        if (!parameters.TryGetValue("__raw_body", out var rawBody) || string.IsNullOrWhiteSpace(rawBody))
            return false;

        if (!parameters.TryGetValue("__paypal_transmission_id", out var transmissionId)
            || !parameters.TryGetValue("__paypal_transmission_time", out var transmissionTime)
            || !parameters.TryGetValue("__paypal_transmission_sig", out var transmissionSig)
            || !parameters.TryGetValue("__paypal_cert_url", out var certUrl)
            || !parameters.TryGetValue("__paypal_auth_algo", out var authAlgo))
        {
            return false;
        }

        try
        {
            var client = CreateHttpClient();
            var accessToken = await GetAccessTokenAsync(client);
            if (string.IsNullOrWhiteSpace(accessToken))
                return false;

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var verifyRequest = new
            {
                transmission_id = transmissionId,
                transmission_time = transmissionTime,
                cert_url = certUrl,
                auth_algo = authAlgo,
                transmission_sig = transmissionSig,
                webhook_id = _options.Value.WebhookId,
                webhook_event = JsonSerializer.Deserialize<JsonElement>(rawBody)
            };

            var response = await client.PostAsJsonAsync("/v1/notifications/verify-webhook-signature", verifyRequest);
            var content = await response.Content.ReadFromJsonAsync<PayPalWebhookVerifyResponse>();

            return response.IsSuccessStatusCode
                && string.Equals(content?.VerificationStatus, "SUCCESS", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PayPal webhook signature verification failed.");
            return false;
        }
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
            OrderId = tradeNo
        }));
    }

    public Task<Result> UpdatePaymentMethodAsync(string subscriptionNo, string paymentMethodId)
    {
        // PayPal 不支持通过 API 直接更新订阅支付方式，需用户在 PayPal 端操作
        _logger.LogWarning("PayPal does not support updating subscription payment method via API. SubscriptionNo: {SubscriptionNo}", subscriptionNo);
        return Task.FromResult(Result.Failure("PayPal does not support updating subscription payment method via API. The subscriber must update their payment method directly on PayPal.", 501));
    }

    private HttpClient CreateHttpClient()
    {
        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(
            string.Equals(_options.Value.Mode, "sandbox", StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.sandbox.paypal.com"
                : "https://api-m.paypal.com");
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        client.DefaultRequestHeaders.Add("Accept-Language", "en_US");
        return client;
    }

    private async Task<string?> GetAccessTokenAsync(HttpClient client)
    {
        // 检查缓存的 token 是否仍然有效（提前 60 秒过期以避免竞态）
        if (_accessToken != null && DateTime.UtcNow < _tokenExpireTime.AddSeconds(-60))
            return _accessToken;

        try
        {
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_options.Value.ClientId}:{_options.Value.ClientSecret}"));

            var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token")
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials")
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            var response = await client.SendAsync(request);
            var content = await response.Content.ReadFromJsonAsync<PayPalTokenResponse>();

            if (content == null || string.IsNullOrEmpty(content.AccessToken))
            {
                _accessToken = null;
                return null;
            }

            _accessToken = content.AccessToken;
            _tokenExpireTime = DateTime.UtcNow.AddSeconds(content.ExpiresIn > 0 ? content.ExpiresIn : 3600);

            return _accessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get PayPal access token.");
            _accessToken = null;
            return null;
        }
    }

    private static PaymentStatus MapPayPalEventStatus(string? eventType)
    {
        return eventType switch
        {
            "PAYMENT.CAPTURE.COMPLETED" or "CHECKOUT.ORDER.COMPLETED" => PaymentStatus.Succeeded,
            "PAYMENT.CAPTURE.DENIED" or "PAYMENT.CAPTURE.REFUNDED" or "CHECKOUT.ORDER.VOIDED" => PaymentStatus.Failed,
            _ => PaymentStatus.Processing
        };
    }

    private static string? TryGetNestedString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static string? TryGetNestedString(JsonElement element, string propertyName, int index, string nestedProperty)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
            return null;

        if (array.GetArrayLength() <= index)
            return null;

        var item = array[index];
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(nestedProperty, out var value))
            return null;

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static string? TryGetNestedString(JsonElement element, string property1, string property2)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property1, out var nested))
            return null;

        return TryGetNestedString(nested, property2);
    }

    private static string? TryGetNestedString(JsonElement element, string property1, string property2, string property3)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property1, out var nested))
            return null;

        return TryGetNestedString(nested, property2, property3);
    }
}
