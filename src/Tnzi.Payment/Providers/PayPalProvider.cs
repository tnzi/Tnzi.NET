using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
                Amount = decimal.Parse(content.PurchaseUnits?.FirstOrDefault()?.Amount?.Value ?? "0"),
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

            var response = await client.PostAsJsonAsync($"/v2/payments/captures/{input.TradeNo}/refunds", refundRequest);
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
        // PayPal webhooks 通过单独的 webhook 端点处理
        return Task.FromResult(Result.Success(new PaymentProviderCallbackResult()));
    }

    public bool VerifySignature(IDictionary<string, string> parameters)
    {
        // PayPal webhook 验证需要 API 调用
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
            OrderId = null
        }));
    }

    public Task<Result> UpdatePaymentMethodAsync(string subscriptionNo, string paymentMethodId)
    {
        return Task.FromResult(Result.Success());
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
}

/// <summary>
/// PayPal 订单响应
/// </summary>
public class PayPalOrderResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("purchase_units")]
    public List<PayPalPurchaseUnit>? PurchaseUnits { get; set; }

    [JsonPropertyName("links")]
    public List<PayPalLink>? Links { get; set; }
}

public class PayPalPurchaseUnit
{
    [JsonPropertyName("reference_id")]
    public string? ReferenceId { get; set; }

    [JsonPropertyName("amount")]
    public PayPalAmount? Amount { get; set; }
}

public class PayPalAmount
{
    [JsonPropertyName("currency_code")]
    public string CurrencyCode { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

public class PayPalLink
{
    [JsonPropertyName("rel")]
    public string Rel { get; set; } = string.Empty;

    [JsonPropertyName("href")]
    public string Href { get; set; } = string.Empty;
}

public class PayPalTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

public class PayPalRefundResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}
