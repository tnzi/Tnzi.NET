
namespace Tnzi.Payment.PayPal.Providers;

/// <summary>
/// PayPal支付渠道实现。
/// 自动续费所需的账户保存与商户发起扣款在 <c>PayPalProvider.Vault.cs</c>。
/// </summary>
public partial class PayPalProvider : IPaymentProvider
{
    private readonly IOptions<PayPalOptions> _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PayPalProvider> _logger;

    /// <summary>
    /// 访问令牌的**跨请求**缓存，按 (环境, ClientId) 分桶。
    /// </summary>
    /// <remarks>
    /// 此前令牌存在实例字段上，而本类是 Scoped —— 每个请求都是一个新实例、一个空缓存，
    /// 于是每一次建单 / 查单 / 退款前都先打一次 <c>/v1/oauth2/token</c>：
    /// 对外请求数翻倍、每笔交易多一次往返，而 PayPal 发的令牌本来有效期一小时。
    /// 症状只有「PayPal 慢」，没有任何一条错误日志。
    /// 分桶到 ClientId 是为了让沙箱与生产、或多商户配置互不串用；
    /// 轮换凭据自然落到新桶上。
    /// </remarks>
    private static readonly ConcurrentDictionary<string, (string Token, DateTime ExpiresAt)> AccessTokens =
        new(StringComparer.Ordinal);

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
                            value = input.Amount.ToString(
                                $"F{CurrencyInfo.GetDecimalPlaces(input.Currency)}", CultureInfo.InvariantCulture)
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

            // PayPal-Request-Id 是 PayPal 的幂等键：网络超时重试不会重复建单
            using var orderMessage = new HttpRequestMessage(HttpMethod.Post, "/v2/checkout/orders")
            {
                Content = JsonContent.Create(orderRequest)
            };
            orderMessage.Headers.Add("PayPal-Request-Id", $"order:{input.TradeNo}");

            var response = await client.SendAsync(orderMessage);
            // 不看状态码直接反序列化时，一个 4xx 的错误体解出来是一个空的订单对象，
            // 于是失败被读成「PayPal 没给订单号」—— 真正的原因（哪个字段不合法）连日志都不留。
            // 退款那条路径一直是这么写的，建单这条漏了。
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("PayPal order creation rejected. TradeNo: {TradeNo}, Status: {Status}, Body: {Body}",
                    input.TradeNo, response.StatusCode, errorBody);
                return Result.Failure<PaymentProviderOrderResult>(ErrorCodes.PayPalOrderCreationFailed, 400);
            }

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

    /// <summary>
    /// 只读同步：传入订单 id 或 capture id（见 <see cref="ReadPaymentAsync"/>）。本地关单 / 过期清扫走这里核实渠道状态，所以它绝不 capture：
    /// 替一张正在被关掉的单收钱，等于向一个刚取消的人扣款。
    /// </summary>
    public Task<Result<PaymentProviderQueryResult>> SyncOrderAsync(string tradeNo)
        => ReadPaymentAsync(tradeNo, captureApproved: false);

    /// <summary>
    /// 付款人已经批准的订单在这里 capture（幂等键 <c>cap:{TradeNo}</c>），再回报 capture 之后的状态。
    /// </summary>
    /// <remarks>
    /// 一次性收款建的是 <c>intent=CAPTURE</c> 订单，付款人在 PayPal 点完「立即付款」之后订单停在 <c>APPROVED</c>，
    /// 要商户调用 capture 才真正收钱。此前全模块只有无人值守扣款那条路径会 capture：重定向收款的订单批准之后无人收款，
    /// 到期作废，付款人以为付了，商户一分钱没收到。服务层在付款人回到站点时的同步、以及 <c>CHECKOUT.ORDER.APPROVED</c>
    /// 回调上调用它。
    /// </remarks>
    public Task<Result<PaymentProviderQueryResult>> FinalizePaymentAsync(string tradeNo)
        => ReadPaymentAsync(tradeNo, captureApproved: true);

    /// <summary>
    /// 读一笔 PayPal 收款的状态；<paramref name="captureApproved"/> 时对已批准未收款的订单先 capture。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 入参可能是订单 id（建单时存的）也可能是 capture id（回调与收款之后存的：退款打的是 capture）。
    /// 先按订单查，订单接口答 404 再按 capture 查。此前只按订单查：一笔经回调落定的支付（存的是 capture id）
    /// 同步与关单核实永远答「找不到订单」。
    /// </para>
    /// <para>
    /// 收到钱之后回报的 <c>ExternalTradeNo</c> 是 capture id、金额与币种取 capture 自己的：此前回报订单 id，
    /// 经同步或关单核实落定为成功的支付把订单 id 记进 <c>ExternalTradeNo</c>，随后的退款打到
    /// <c>/v2/payments/captures/{订单 id}/refunds</c>，PayPal 答 404，这笔支付在系统里永远退不了款
    /// （之后到的 <c>PAYMENT.CAPTURE.COMPLETED</c> 撞上本地终态，不再改写它）。
    /// </para>
    /// </remarks>
    private async Task<Result<PaymentProviderQueryResult>> ReadPaymentAsync(string channelId, bool captureApproved)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(channelId))
                return Result.Failure<PaymentProviderQueryResult>("PayPal order id is required.");

            var client = CreateHttpClient();
            var accessToken = await GetAccessTokenAsync(client);
            if (accessToken == null)
                return Result.Failure<PaymentProviderQueryResult>("Failed to get PayPal access token.");

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await client.GetAsync($"/v2/checkout/orders/{channelId}");
            if (response.StatusCode == HttpStatusCode.NotFound)
                return await ReadCaptureAsync(client, channelId);

            if (!response.IsSuccessStatusCode)
                return Result.Failure<PaymentProviderQueryResult>("PayPal order not found.");

            var order = await response.Content.ReadFromJsonAsync<PayPalOrderResponse>();
            if (order == null)
                return Result.Failure<PaymentProviderQueryResult>("Failed to parse PayPal response.");

            if (captureApproved && string.Equals(order.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
            {
                var captured = await CaptureApprovedOrderAsync(client, order);
                if (!captured.Succeeded || captured.Data == null)
                    return Result.Failure<PaymentProviderQueryResult>(captured.Message ?? ErrorCodes.PayPalPaymentQueryFailed, captured.Code ?? 502);

                order = captured.Data;
            }

            return Result.Success(MapOrder(channelId, order));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayPal payment query failed. TradeNo: {TradeNo}", channelId);
            return Result.Failure<PaymentProviderQueryResult>(ErrorCodes.PayPalPaymentQueryFailed, 400);
        }
    }

    /// <summary>
    /// capture 一张已批准的订单，返回 capture 之后的订单。幂等键按内部流水号（订单的 <c>reference_id</c>），
    /// 付款人回站的同步与批准回调同时到达也只收一次。
    /// </summary>
    /// <remarks>
    /// PayPal 拒绝 capture（4xx：付款方式被拒、已经 capture 过之类）时重读订单，以它此刻的状态为准：
    /// 已经 capture 过的读到 COMPLETED，被拒的仍是 APPROVED（付款人可以换个付款方式再批准）。
    /// 5xx 与网络异常答失败，由调用方决定（回调答 5xx 让 PayPal 重投）。
    /// </remarks>
    private async Task<Result<PayPalOrderResponse>> CaptureApprovedOrderAsync(HttpClient client, PayPalOrderResponse order)
    {
        var tradeNo = order.PurchaseUnits?.FirstOrDefault()?.ReferenceId;
        if (string.IsNullOrWhiteSpace(tradeNo))
            tradeNo = order.Id;

        using var message = new HttpRequestMessage(HttpMethod.Post, $"/v2/checkout/orders/{order.Id}/capture")
        {
            Content = JsonContent.Create(new { })
        };
        message.Headers.Add("PayPal-Request-Id", $"cap:{tradeNo}");

        var response = await client.SendAsync(message);
        if (response.IsSuccessStatusCode)
        {
            var captured = await response.Content.ReadFromJsonAsync<PayPalOrderResponse>();
            _logger.LogInformation("PayPal order captured. TradeNo: {TradeNo}, OrderId: {OrderId}, Status: {Status}",
                tradeNo, order.Id, captured?.Status);
            return captured == null
                ? Result.Failure<PayPalOrderResponse>(ErrorCodes.PayPalPaymentQueryFailed, 502)
                : Result.Success(captured);
        }

        var reason = await LogPayPalErrorAsync(response, "PayPal order capture rejected. TradeNo: {TradeNo}", tradeNo);
        if ((int)response.StatusCode >= 500)
            return Result.Failure<PayPalOrderResponse>(reason ?? ErrorCodes.PayPalPaymentQueryFailed, 502);

        var reread = await client.GetAsync($"/v2/checkout/orders/{order.Id}");
        var current = reread.IsSuccessStatusCode ? await reread.Content.ReadFromJsonAsync<PayPalOrderResponse>() : null;
        return current == null
            ? Result.Failure<PayPalOrderResponse>(reason ?? ErrorCodes.PayPalPaymentQueryFailed, 502)
            : Result.Success(current);
    }

    /// <summary>
    /// 按 capture id 读一笔收款（存的是 capture id 时订单接口答 404）。
    /// </summary>
    private static async Task<Result<PaymentProviderQueryResult>> ReadCaptureAsync(HttpClient client, string captureId)
    {
        var response = await client.GetAsync($"/v2/payments/captures/{captureId}");
        if (!response.IsSuccessStatusCode)
            return Result.Failure<PaymentProviderQueryResult>("PayPal order not found.");

        var capture = await response.Content.ReadFromJsonAsync<PayPalCapture>();
        if (capture == null || string.IsNullOrWhiteSpace(capture.Id))
            return Result.Failure<PaymentProviderQueryResult>("Failed to parse PayPal response.");

        var status = MapPayPalCaptureStatus(capture.Status);
        return Result.Success(new PaymentProviderQueryResult
        {
            TradeNo = captureId,
            ExternalTradeNo = capture.Id,
            Status = status,
            Amount = ParseAmount(capture.Amount?.Value),
            Currency = string.IsNullOrWhiteSpace(capture.Amount?.CurrencyCode) ? null : capture.Amount.CurrencyCode,
            PaidTime = status == PaymentStatus.Succeeded ? DateTime.UtcNow : null,
            FailReason = status == PaymentStatus.Failed ? capture.Status : null
        });
    }

    /// <summary>
    /// 订单 → 查询结果。收到钱（<c>COMPLETED</c>）之后以 capture 为准：id、状态、金额、币种都取 capture 的；
    /// <c>VOIDED</c> 是确定没收到钱；其余（建好未批准、已批准未收款、等付款人操作）都是处理中。
    /// </summary>
    private static PaymentProviderQueryResult MapOrder(string channelId, PayPalOrderResponse order)
    {
        var unit = order.PurchaseUnits?.FirstOrDefault();
        var capture = unit?.Payments?.Captures?.FirstOrDefault();

        var status = order.Status?.ToUpperInvariant() switch
        {
            "COMPLETED" => MapPayPalCaptureStatus(capture?.Status ?? "COMPLETED"),
            "VOIDED" => PaymentStatus.Failed,
            _ => PaymentStatus.Processing
        };

        var amount = capture?.Amount ?? unit?.Amount;
        return new PaymentProviderQueryResult
        {
            TradeNo = channelId,
            ExternalTradeNo = capture?.Id ?? order.Id,
            Status = status,
            Amount = ParseAmount(amount?.Value),
            Currency = string.IsNullOrWhiteSpace(amount?.CurrencyCode) ? null : amount.CurrencyCode,
            PaidTime = status == PaymentStatus.Succeeded ? DateTime.UtcNow : null,
            FailReason = status == PaymentStatus.Failed ? (capture?.Status ?? order.Status) : null
        };
    }

    private static decimal ParseAmount(string? value)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount : 0m;

    public async Task<Result<PaymentProviderRefundResult>> RefundAsync(PaymentProviderRefundDto input)
    {
        try
        {
            var client = CreateHttpClient();
            var accessToken = await GetAccessTokenAsync(client);
            if (accessToken == null)
                return Result.Failure<PaymentProviderRefundResult>("Failed to get PayPal access token.");

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var decimals = CurrencyInfo.GetDecimalPlaces(input.Currency);
            var refundRequest = new
            {
                amount = new
                {
                    // 币种以本次退款的币种为准，而不是渠道全局默认币种（多币种下会串币）
                    currency_code = input.Currency,
                    value = input.RefundAmount.ToString($"F{decimals}", CultureInfo.InvariantCulture)
                },
                note_to_payer = input.Reason
            };

            // PayPal-Request-Id 是 PayPal 的幂等键：重试同一退款流水不会退两次
            var captureId = input.ExternalTradeNo ?? input.TradeNo;
            var response = await SendRefundAsync(client, captureId, refundRequest, input.RefundNo);

            // ★ 存的是订单 id 而不是 capture id（修复之前经同步或关单核实落定的支付就是这样）：capture 接口答 404。
            // 按订单找到它的 capture 再退一次，幂等键不变，不会退两次。
            if (response.StatusCode == HttpStatusCode.NotFound
                && await ResolveCaptureIdAsync(client, captureId) is { } resolvedCaptureId
                && !string.Equals(resolvedCaptureId, captureId, StringComparison.Ordinal))
            {
                _logger.LogWarning("PayPal refund {RefundNo}: {Id} is an order id, refunding its capture {CaptureId} instead.",
                    input.RefundNo, captureId, resolvedCaptureId);
                response.Dispose();
                response = await SendRefundAsync(client, resolvedCaptureId, refundRequest, input.RefundNo);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("PayPal refund rejected. RefundNo: {RefundNo}, Status: {Status}, Body: {Body}",
                    input.RefundNo, response.StatusCode, errorBody);
                return Result.Failure<PaymentProviderRefundResult>(ErrorCodes.PayPalRefundFailed, 400);
            }

            var content = await response.Content.ReadFromJsonAsync<PayPalRefundResponse>();

            _logger.LogInformation("PayPal refund created. RefundNo: {RefundNo}, Amount: {Amount}, Status: {Status}",
                input.RefundNo, input.RefundAmount, content?.Status);

            return Result.Success(new PaymentProviderRefundResult
            {
                RefundNo = input.RefundNo,
                ExternalRefundNo = content?.Id,
                RefundAmount = input.RefundAmount,
                Status = MapPayPalRefundStatus(content?.Status),
                CompletedTime = string.Equals(content?.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase)
                    ? DateTime.UtcNow
                    : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayPal refund failed. RefundNo: {RefundNo}", input.RefundNo);
            return Result.Failure<PaymentProviderRefundResult>(ErrorCodes.PayPalRefundFailed, 400);
        }
    }

    private static async Task<HttpResponseMessage> SendRefundAsync(HttpClient client, string captureId, object refundRequest, string refundNo)
    {
        using var refundMessage = new HttpRequestMessage(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refunds")
        {
            Content = JsonContent.Create(refundRequest)
        };
        refundMessage.Headers.Add("PayPal-Request-Id", $"re:{refundNo}");
        return await client.SendAsync(refundMessage);
    }

    /// <summary>把一个订单 id 解析成它的 capture id；不是订单或还没收款时为 null。</summary>
    private static async Task<string?> ResolveCaptureIdAsync(HttpClient client, string orderId)
    {
        var response = await client.GetAsync($"/v2/checkout/orders/{orderId}");
        if (!response.IsSuccessStatusCode)
            return null;

        var order = await response.Content.ReadFromJsonAsync<PayPalOrderResponse>();
        return order?.PurchaseUnits?.FirstOrDefault()?.Payments?.Captures?.FirstOrDefault()?.Id;
    }

    public async Task<Result<PaymentProviderRefundQueryResult>> QueryRefundAsync(string externalRefundNo)
    {
        if (string.IsNullOrWhiteSpace(externalRefundNo))
            return Result.Failure<PaymentProviderRefundQueryResult>("PayPal refund id is required.", 400);

        try
        {
            var client = CreateHttpClient();
            var accessToken = await GetAccessTokenAsync(client);
            if (accessToken == null)
                return Result.Failure<PaymentProviderRefundQueryResult>("Failed to get PayPal access token.");

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await client.GetAsync($"/v2/payments/refunds/{externalRefundNo}");
            if (!response.IsSuccessStatusCode)
                return Result.Failure<PaymentProviderRefundQueryResult>(ErrorCodes.PayPalRefundFailed, 400);

            var content = await response.Content.ReadFromJsonAsync<PayPalRefundResponse>();

            return Result.Success(new PaymentProviderRefundQueryResult
            {
                RefundNo = externalRefundNo,
                ExternalRefundNo = content?.Id,
                Status = MapPayPalRefundStatus(content?.Status),
                CompletedTime = string.Equals(content?.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase)
                    ? DateTime.UtcNow
                    : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayPal refund query failed. ExternalRefundNo: {ExternalRefundNo}", externalRefundNo);
            return Result.Failure<PaymentProviderRefundQueryResult>(ErrorCodes.PayPalRefundFailed, 400);
        }
    }

    public Task<Result<PaymentProviderCallbackResult>> HandleCallbackAsync(IDictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue(PaymentConstants.CallbackRawBodyKey, out var rawBody) || string.IsNullOrWhiteSpace(rawBody))
            return Task.FromResult(Result.Failure<PaymentProviderCallbackResult>(ErrorCodes.PaymentInvalidSignature, 400));

        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;
            var eventType = root.TryGetProperty("event_type", out var eventTypeElement)
                ? eventTypeElement.GetString()
                : null;
            var eventId = root.TryGetProperty("id", out var eventIdElement)
                ? eventIdElement.GetString()
                : null;

            var resource = root.TryGetProperty("resource", out var resourceElement)
                ? resourceElement
                : default;

            // 付款人在 PayPal 撤销了对本商户的授权（或商户后台删了这个凭据）。
            // 不处理的话，本地会一直拿着一个已经作废的凭据，直到下次续费扣款失败才发现。
            if (string.Equals(eventType, "VAULT.PAYMENT-TOKEN.DELETED", StringComparison.OrdinalIgnoreCase))
            {
                var revokedToken = TryGetNestedString(resource, "id");
                _logger.LogInformation("PayPal vault token {Token} was deleted at the channel.", revokedToken);

                return Task.FromResult(Result.Success(new PaymentProviderCallbackResult
                {
                    EventId = eventId,
                    Kind = PaymentCallbackKind.PaymentMethodRevoked,
                    PaymentMethodToken = revokedToken,
                    IsHandled = !string.IsNullOrWhiteSpace(revokedToken)
                }));
            }

            var status = MapPayPalEventStatus(eventType);

            // 与支付状态无关的事件（如 BILLING.*、CUSTOMER.*）不该被当成失败，
            // 否则 PayPal 会持续重投直至端点被判定不可用
            if (status == null)
            {
                return Task.FromResult(Result.Success(new PaymentProviderCallbackResult
                {
                    EventId = eventId,
                    IsHandled = false
                }));
            }

            var tradeNo =
                TryGetNestedString(resource, "purchase_units", 0, "reference_id")
                ?? TryGetNestedString(resource, "custom_id")
                ?? TryGetNestedString(resource, "invoice_id")
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(tradeNo))
            {
                _logger.LogWarning("PayPal callback event {EventId} ({EventType}) has no trade reference; ignored.", eventId, eventType);
                return Task.FromResult(Result.Success(new PaymentProviderCallbackResult
                {
                    EventId = eventId,
                    IsHandled = false
                }));
            }

            // ★ 订单资源（CHECKOUT.ORDER.*）上没有 amount：金额、币种与收款 id 在 purchase_units[0].payments.captures[0]。
            //   此前一律按收款资源的路径去读，CHECKOUT.ORDER.COMPLETED 恒以 0 金额进服务层，被判金额不符答 400、
            //   记一条看着像金额篡改的告警，PayPal 再把它一路重投到收款事件落地为止。外部单号也要记收款 id：
            //   退款打的是 /v2/payments/captures/{id}/refunds，记成订单 id，PayPal 答 404。
            var capture = TryGetOrderCapture(resource);
            if (status == PaymentStatus.Succeeded && capture == null && IsOrderEvent(eventType))
            {
                // 订单完成却读不到收款：没有可以核对的金额。按收款事件（PAYMENT.CAPTURE.COMPLETED）落定，这一条只确认收到
                _logger.LogInformation("PayPal callback event {EventId} ({EventType}) carries no capture; the capture event settles the payment.", eventId, eventType);
                return Task.FromResult(Result.Success(new PaymentProviderCallbackResult
                {
                    EventId = eventId,
                    IsHandled = false
                }));
            }

            var settled = capture ?? resource;

            var externalTradeNo =
                TryGetNestedString(settled, "id")
                ?? TryGetNestedString(settled, "supplementary_data", "related_ids", "order_id");

            var amountText =
                TryGetNestedString(settled, "amount", "value")
                ?? TryGetNestedString(settled, "seller_receivable_breakdown", "gross_amount", "value");

            decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var paidAmount);

            // 币种与金额在同一个对象上，此前只取了 value 把它丢掉 ——
            // 于是服务层只能比数值，一笔 100 USD 的订单收到 100 JPY 的回报照样通过校验
            var currency =
                TryGetNestedString(settled, "amount", "currency_code")
                ?? TryGetNestedString(settled, "seller_receivable_breakdown", "gross_amount", "currency_code");

            return Task.FromResult(Result.Success(new PaymentProviderCallbackResult
            {
                TradeNo = tradeNo,
                ExternalTradeNo = externalTradeNo,
                Status = status.Value,
                PaidAmount = paidAmount,
                Currency = currency,
                FailReason = status == PaymentStatus.Failed ? eventType : null,
                EventId = eventId,
                // 付款人批准了，钱还要商户 capture 才收得到：服务层据此调用 FinalizePaymentAsync
                RequiresFinalization = string.Equals(eventType, "CHECKOUT.ORDER.APPROVED", StringComparison.OrdinalIgnoreCase)
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

        if (!parameters.TryGetValue(PaymentConstants.CallbackRawBodyKey, out var rawBody) || string.IsNullOrWhiteSpace(rawBody))
            return false;

        if (!parameters.TryGetValue(PaymentConstants.CallbackPayPalTransmissionIdKey, out var transmissionId)
            || !parameters.TryGetValue(PaymentConstants.CallbackPayPalTransmissionTimeKey, out var transmissionTime)
            || !parameters.TryGetValue(PaymentConstants.CallbackPayPalTransmissionSigKey, out var transmissionSig)
            || !parameters.TryGetValue(PaymentConstants.CallbackPayPalCertUrlKey, out var certUrl)
            || !parameters.TryGetValue(PaymentConstants.CallbackPayPalAuthAlgoKey, out var authAlgo))
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

    public Task<Result<PaymentParamsDto>> GetPaymentParamsAsync(string tradeNo)
    {
        return Task.FromResult(Result.Success(new PaymentParamsDto
        {
            TradeNo = tradeNo,
            OrderId = tradeNo
        }));
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
        var cacheKey = $"{_options.Value.Mode}|{_options.Value.ClientId}";

        // 缓存的 token 仍然有效就直接用（提前 60 秒过期，避免拿着一个正好在路上过期的令牌）
        if (AccessTokens.TryGetValue(cacheKey, out var cached)
            && DateTime.UtcNow < cached.ExpiresAt.AddSeconds(-60))
        {
            return cached.Token;
        }

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
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("PayPal token request rejected. Status: {Status}, Body: {Body}",
                    response.StatusCode, errorBody);
                AccessTokens.TryRemove(cacheKey, out _);
                return null;
            }

            var content = await response.Content.ReadFromJsonAsync<PayPalTokenResponse>();

            if (content == null || string.IsNullOrEmpty(content.AccessToken))
            {
                AccessTokens.TryRemove(cacheKey, out _);
                return null;
            }

            var expiresAt = DateTime.UtcNow.AddSeconds(content.ExpiresIn > 0 ? content.ExpiresIn : 3600);
            AccessTokens[cacheKey] = (content.AccessToken, expiresAt);

            return content.AccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get PayPal access token.");
            AccessTokens.TryRemove(cacheKey, out _);
            return null;
        }
    }

    /// <summary>
    /// PayPal 事件类型映射为支付状态；返回 null 表示该事件与支付状态无关，应被忽略而非当作失败。
    /// </summary>
    private static PaymentStatus? MapPayPalEventStatus(string? eventType)
    {
        return eventType switch
        {
            "PAYMENT.CAPTURE.COMPLETED" or "CHECKOUT.ORDER.COMPLETED" => PaymentStatus.Succeeded,
            "PAYMENT.CAPTURE.DENIED" or "CHECKOUT.ORDER.VOIDED" => PaymentStatus.Failed,
            "PAYMENT.CAPTURE.PENDING" or "CHECKOUT.ORDER.APPROVED" => PaymentStatus.Processing,
            // 退款类事件由退款对账链路负责（见 IRefundService.ReconcilePendingRefundsAsync），
            // 不能在这里当成"支付失败"，否则会把一笔已成功的支付错误改写
            _ => null
        };
    }

    /// <summary>
    /// PayPal 退款状态映射。PENDING 落 Refunding：PayPal 退款可能需要数日才终结。
    /// </summary>
    private static RefundStatus MapPayPalRefundStatus(string? status)
    {
        return status?.ToUpperInvariant() switch
        {
            "COMPLETED" => RefundStatus.Succeeded,
            "FAILED" => RefundStatus.Failed,
            "CANCELLED" => RefundStatus.Cancelled,
            _ => RefundStatus.Refunding
        };
    }

    /// <summary>事件资源是不是一张订单（<c>CHECKOUT.ORDER.*</c>），而不是一笔收款（<c>PAYMENT.CAPTURE.*</c>）。</summary>
    private static bool IsOrderEvent(string? eventType)
        => eventType != null && eventType.StartsWith("CHECKOUT.ORDER.", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 订单资源上的第一笔收款（<c>purchase_units[0].payments.captures[0]</c>）；不是订单或还没有收款时为 null。
    /// </summary>
    private static JsonElement? TryGetOrderCapture(JsonElement resource)
    {
        if (resource.ValueKind != JsonValueKind.Object
            || !resource.TryGetProperty("purchase_units", out var units)
            || units.ValueKind != JsonValueKind.Array
            || units.GetArrayLength() == 0)
        {
            return null;
        }

        var unit = units[0];
        if (unit.ValueKind != JsonValueKind.Object
            || !unit.TryGetProperty("payments", out var payments)
            || payments.ValueKind != JsonValueKind.Object
            || !payments.TryGetProperty("captures", out var captures)
            || captures.ValueKind != JsonValueKind.Array
            || captures.GetArrayLength() == 0)
        {
            return null;
        }

        var capture = captures[0];
        return capture.ValueKind == JsonValueKind.Object ? capture : null;
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
