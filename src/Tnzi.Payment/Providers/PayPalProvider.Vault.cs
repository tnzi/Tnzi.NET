
namespace Tnzi.Payment.Providers;

/// <summary>
/// PayPal 账户保存（Vault v3）与商户发起扣款（reference transactions，旧称 billing agreements）。
/// </summary>
/// <remarks>
/// <para>
/// 这条链路让 PayPal 订阅能够自动续费：付款人授权一次，之后后台可无人值守扣款。
/// 三个 API 组成一条链，缺一不可：
/// </para>
/// <list type="number">
/// <item><c>POST /v3/vault/setup-tokens</c> —— 申请一次性授权凭据，拿到付款人授权地址；</item>
/// <item><c>POST /v3/vault/payment-tokens</c> —— 付款人授权后，把一次性凭据换成可长期保存的支付凭据；</item>
/// <item><c>POST /v2/checkout/orders</c>（带 <c>vault_id</c> + <c>stored_credential</c>）—— 用该凭据发起商户扣款。</item>
/// </list>
/// <para>
/// ⚠️ <b>没有采用 PayPal Subscriptions API</b>（<c>/v1/billing/subscriptions</c>）：那个产品由 PayPal
/// 自己持有订阅周期、续费重试与状态机，而本模块已经拥有这一整套（<c>ISubscriptionService</c> +
/// 后台计费扫描 + PastDue 催款）。两边各存一份订阅状态必然漂移，而且"何时算已收款"会有两个答案。
/// Vault + 商户发起扣款只提供"扣款能力"，状态机仍归框架，这才和现有架构自洽。
/// </para>
/// <para>
/// ⚠️ <b>也没有采用旧版 Billing Agreements</b>（<c>/v1/billing-agreements/agreement-tokens</c>）：
/// 它是 Vault v3 的前身，PayPal 现行文档已把这个场景全部指向 Payment Method Tokens。
/// 但商户账号侧的开通项名字仍叫 <b>reference transactions</b>（billing agreements），
/// 需要向 PayPal 单独申请——这也是 <see cref="PayPalOptions.EnableVault"/> 默认关闭的原因。
/// </para>
/// </remarks>
public partial class PayPalProvider
{
    /// <summary>
    /// 商户发起扣款与账户保存靠的是同一项 PayPal 开通（reference transactions），所以共用一个开关。
    /// 关着时如实报告"不支持"，而不是让用户走完整个绑定流程再在扣款当天失败。
    /// </summary>
    public bool SupportsOffSessionCharge => _options.Value.EnableVault;

    /// <inheritdoc cref="SupportsOffSessionCharge"/>
    public bool SupportsPaymentMethodStorage => _options.Value.EnableVault;

    public async Task<Result<PaymentProviderSetupResult>> CreateSetupSessionAsync(PaymentProviderSetupDto input)
    {
        var options = _options.Value;
        if (!options.EnableVault)
            return Result.Failure<PaymentProviderSetupResult>(ErrorCodes.PaymentMethodStorageNotSupported, 400);

        var returnUrl = FirstNonEmpty(input.ReturnUrl, options.VaultReturnUrl, options.ReturnUrl);
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            // 没有回跳地址，付款人在 PayPal 点完"同意"就无处可去，绑定永远走不完。
            // 与其把用户送进死路，不如现在就失败。
            _logger.LogError("PayPal vault return URL is not configured. Set Payment:PayPal:VaultReturnUrl.");
            return Result.Failure<PaymentProviderSetupResult>(ErrorCodes.PaymentMethodBindingFailed, 400);
        }

        var cancelUrl = FirstNonEmpty(input.CancelUrl, options.VaultCancelUrl, options.CancelUrl) ?? returnUrl;

        try
        {
            var client = await CreateAuthorizedClientAsync();
            if (client == null)
                return Result.Failure<PaymentProviderSetupResult>(ErrorCodes.PayPalVaultFailed, 400);

            var customer = new Dictionary<string, object?>
            {
                // 商户侧客户标识：登记时据此确认该凭据确实是为当前用户创建的，
                // 否则拿到别人的 setup token 就能把别人的 PayPal 账户绑到自己名下。
                ["merchant_customer_id"] = input.UserId.ToString()
            };

            // 复用该用户已有的 PayPal 客户，避免同一个人在 PayPal 侧散成多个客户
            if (!string.IsNullOrWhiteSpace(input.ProviderCustomerId))
                customer["id"] = input.ProviderCustomerId;

            var body = new Dictionary<string, object?>
            {
                ["customer"] = customer,
                ["payment_source"] = new Dictionary<string, object?>
                {
                    ["paypal"] = new Dictionary<string, object?>
                    {
                        // MERCHANT：声明后续扣款由商户发起（无人值守），PayPal 据此在授权页
                        // 要求付款人同意"允许该商户将来直接扣款"
                        ["usage_type"] = "MERCHANT",
                        ["customer_type"] = "CONSUMER",
                        ["permit_multiple_payment_tokens"] = false,
                        ["usage_pattern"] = options.VaultUsagePattern,
                        ["experience_context"] = new Dictionary<string, object?>
                        {
                            ["brand_name"] = options.BrandName,
                            // 绑定账户不涉及发货，要地址只会平添一步
                            ["shipping_preference"] = "NO_SHIPPING",
                            ["return_url"] = returnUrl,
                            ["cancel_url"] = cancelUrl
                        }
                    }
                }
            };

            using var message = new HttpRequestMessage(HttpMethod.Post, "/v3/vault/setup-tokens")
            {
                Content = JsonContent.Create(body)
            };
            // 幂等键按用户 + 客户：网络超时重试不会在 PayPal 侧堆出一串孤儿 setup token
            message.Headers.Add("PayPal-Request-Id", $"vault-setup:{input.UserId}:{input.ProviderCustomerId ?? "new"}");

            var response = await client.SendAsync(message);
            if (!response.IsSuccessStatusCode)
            {
                await LogPayPalErrorAsync(response, "PayPal vault setup token creation rejected. UserId: {UserId}", input.UserId);
                return Result.Failure<PaymentProviderSetupResult>(ErrorCodes.PayPalVaultFailed, 400);
            }

            var content = await response.Content.ReadFromJsonAsync<PayPalSetupTokenResponse>();
            if (content == null || string.IsNullOrWhiteSpace(content.Id))
                return Result.Failure<PaymentProviderSetupResult>(ErrorCodes.PayPalVaultFailed, 400);

            var approvalUrl = content.Links?.FirstOrDefault(
                l => string.Equals(l.Rel, "approve", StringComparison.OrdinalIgnoreCase))?.Href;

            if (string.IsNullOrWhiteSpace(approvalUrl))
            {
                // 没有授权地址就没有绑定流程可走。这通常意味着商户账号未开通 reference transactions，
                // 说清楚比返回一个前端不知道拿来干什么的空会话强。
                _logger.LogError(
                    "PayPal returned no approval link for setup token {SetupToken}. The merchant account may not be enabled for reference transactions.",
                    content.Id);
                return Result.Failure<PaymentProviderSetupResult>(ErrorCodes.PayPalVaultFailed, 400);
            }

            _logger.LogInformation("PayPal vault setup token created. UserId: {UserId}, SetupToken: {SetupToken}",
                input.UserId, content.Id);

            return Result.Success(new PaymentProviderSetupResult
            {
                SetupId = content.Id,
                ApprovalUrl = approvalUrl,
                ProviderCustomerId = content.Customer?.Id ?? input.ProviderCustomerId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayPal vault setup token creation failed. UserId: {UserId}", input.UserId);
            return Result.Failure<PaymentProviderSetupResult>(ErrorCodes.PayPalVaultFailed, 400);
        }
    }

    public async Task<Result<PaymentProviderPaymentMethodResult>> ResolvePaymentMethodAsync(PaymentProviderResolveMethodDto input)
    {
        if (!_options.Value.EnableVault)
            return Result.Failure<PaymentProviderPaymentMethodResult>(ErrorCodes.PaymentMethodStorageNotSupported, 400);

        if (string.IsNullOrWhiteSpace(input.PaymentMethodToken))
            return Result.Failure<PaymentProviderPaymentMethodResult>(ErrorCodes.PaymentMethodNotFound, 400);

        try
        {
            var client = await CreateAuthorizedClientAsync();
            if (client == null)
                return Result.Failure<PaymentProviderPaymentMethodResult>(ErrorCodes.PayPalVaultFailed, 400);

            // setup token 是一次性的：换过一次就作废。所以先当作"已经是 payment token"查一次——
            // 登记接口被重复调用（用户刷新回跳页、前端重试）时才不会第二次就报错。
            var token = await TryGetPaymentTokenAsync(client, input.PaymentMethodToken);

            if (token == null)
            {
                token = await ExchangeSetupTokenAsync(client, input.PaymentMethodToken);
                if (token == null)
                    return Result.Failure<PaymentProviderPaymentMethodResult>(ErrorCodes.PayPalVaultFailed, 400);
            }

            // 归属校验：凭据上带的商户侧客户标识必须就是当前用户。
            // PayPal 只在我们创建时写了它才会回传，所以只在有值时判定——
            // 有值而不匹配是明确的越权信号，必须拒绝；没有值只能说明信息不足，不能据此放行或拒绝。
            var merchantCustomerId = token.Customer?.MerchantCustomerId;
            if (!string.IsNullOrWhiteSpace(merchantCustomerId)
                && !string.Equals(merchantCustomerId, input.UserId.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "PayPal vault token {Token} belongs to another user; binding rejected.", token.Id);
                return Result.Failure<PaymentProviderPaymentMethodResult>(ErrorCodes.PaymentMethodBindingFailed, 403);
            }

            return Result.Success(MapPaymentToken(token));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayPal vault payment method resolution failed.");
            return Result.Failure<PaymentProviderPaymentMethodResult>(ErrorCodes.PayPalVaultFailed, 400);
        }
    }

    public async Task<Result> DetachPaymentMethodAsync(PaymentProviderResolveMethodDto input)
    {
        if (string.IsNullOrWhiteSpace(input.PaymentMethodToken))
            return Result.Failure(ErrorCodes.PaymentMethodNotFound, 400);

        if (!_options.Value.EnableVault)
            return Result.Failure(ErrorCodes.PaymentMethodStorageNotSupported, 400);

        try
        {
            var client = await CreateAuthorizedClientAsync();
            if (client == null)
                return Result.Failure(ErrorCodes.PayPalVaultFailed, 400);

            var response = await client.DeleteAsync($"/v3/vault/payment-tokens/{input.PaymentMethodToken}");

            // 渠道侧已不存在（用户自己在 PayPal 后台撤销了授权）：解绑是幂等操作，本地照常清理
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
                return Result.Success();

            await LogPayPalErrorAsync(response, "PayPal vault token detach rejected. Token: {Token}", input.PaymentMethodToken);
            return Result.Failure(ErrorCodes.PayPalVaultFailed, 400);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayPal vault token detach failed.");
            return Result.Failure(ErrorCodes.PayPalVaultFailed, 400);
        }
    }

    public async Task<Result<PaymentProviderChargeResult>> ChargeOffSessionAsync(PaymentProviderChargeDto input)
    {
        if (!_options.Value.EnableVault)
            return Result.Failure<PaymentProviderChargeResult>(ErrorCodes.PaymentOffSessionNotSupported, 400);

        if (string.IsNullOrWhiteSpace(input.PaymentMethodToken))
            return Result.Failure<PaymentProviderChargeResult>(ErrorCodes.SubscriptionPaymentMethodMissing, 400);

        try
        {
            var client = await CreateAuthorizedClientAsync();
            if (client == null)
                return Result.Failure<PaymentProviderChargeResult>(ErrorCodes.PayPalVaultFailed, 400);

            var decimals = CurrencyInfo.GetDecimalPlaces(input.Currency);
            var body = new
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
                            value = input.Amount.ToString($"F{decimals}", CultureInfo.InvariantCulture)
                        }
                    }
                },
                payment_source = new
                {
                    paypal = new
                    {
                        vault_id = input.PaymentMethodToken,
                        // 这三个字段合起来就是"这笔是商户发起的、周期性的、非首次扣款"。
                        // 少了它们 PayPal 会按客户在场交易处理，进而要求付款人交互——而这里没有人在。
                        stored_credential = new
                        {
                            payment_initiator = "MERCHANT",
                            payment_type = "RECURRING",
                            usage = "SUBSEQUENT"
                        }
                    }
                }
            };

            using var message = new HttpRequestMessage(HttpMethod.Post, "/v2/checkout/orders")
            {
                Content = JsonContent.Create(body)
            };
            // 幂等键按内部流水号：即便"扣款成功但本地状态机推进失败"后换轮重试，
            // 只要流水号未变，PayPal 返回的仍是同一笔订单，不会二次扣款。
            message.Headers.Add("PayPal-Request-Id", $"pi:{input.TradeNo}");

            var response = await client.SendAsync(message);
            if (!response.IsSuccessStatusCode)
            {
                // 扣款被拒（余额不足、付款人撤销了授权）属预期失败：返回"成功拿到一个失败结果"，
                // 让订阅状态机据此降级 PastDue 并进入催款，而不是让整轮后台扫描报错中断。
                var reason = await LogPayPalErrorAsync(response,
                    "PayPal off-session charge declined. TradeNo: {TradeNo}", input.TradeNo);

                return Result.Success(new PaymentProviderChargeResult
                {
                    TradeNo = input.TradeNo,
                    Status = PaymentStatus.Failed,
                    FailReason = reason
                });
            }

            var order = await response.Content.ReadFromJsonAsync<PayPalOrderResponse>();
            if (order == null || string.IsNullOrWhiteSpace(order.Id))
                return Result.Failure<PaymentProviderChargeResult>(ErrorCodes.PayPalVaultFailed, 400);

            // 商户发起的订单通常在创建时就已收款完成；没有的话补一次 capture。
            // 两种都处理是因为这取决于账号配置，而"少收一笔钱"是不能靠猜的。
            if (!string.Equals(order.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
                order = await CaptureOrderAsync(client, order, input.TradeNo) ?? order;

            var capture = order.PurchaseUnits?.FirstOrDefault()?.Payments?.Captures?.FirstOrDefault();
            var status = MapPayPalCaptureStatus(capture?.Status ?? order.Status);

            decimal.TryParse(capture?.Amount?.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var captured);

            _logger.LogInformation(
                "PayPal off-session charge processed. TradeNo: {TradeNo}, OrderId: {OrderId}, CaptureId: {CaptureId}, Status: {Status}",
                input.TradeNo, order.Id, capture?.Id, capture?.Status ?? order.Status);

            return Result.Success(new PaymentProviderChargeResult
            {
                TradeNo = input.TradeNo,
                // 退款接口打的是 capture 不是 order，所以这里存 capture id。
                // 拿不到 capture（尚未收款）时退回订单号，至少还能溯源。
                ExternalTradeNo = capture?.Id ?? order.Id,
                Status = status,
                PaidAmount = captured > 0 ? captured : input.Amount,
                FailReason = status == PaymentStatus.Failed ? (capture?.Status ?? order.Status) : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayPal off-session charge failed. TradeNo: {TradeNo}", input.TradeNo);
            return Result.Failure<PaymentProviderChargeResult>(ErrorCodes.PayPalVaultFailed, 400);
        }
    }

    /// <summary>
    /// 查询已有的 vault 支付凭据；不存在返回 null（用于区分"这是 setup token"和"这是 payment token"）。
    /// </summary>
    private async Task<PayPalPaymentTokenResponse?> TryGetPaymentTokenAsync(HttpClient client, string token)
    {
        var response = await client.GetAsync($"/v3/vault/payment-tokens/{token}");
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<PayPalPaymentTokenResponse>();
    }

    /// <summary>
    /// 用一次性的 setup token 换取可长期保存的 payment token。
    /// </summary>
    private async Task<PayPalPaymentTokenResponse?> ExchangeSetupTokenAsync(HttpClient client, string setupToken)
    {
        var body = new
        {
            payment_source = new
            {
                token = new { id = setupToken, type = "SETUP_TOKEN" }
            }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "/v3/vault/payment-tokens")
        {
            Content = JsonContent.Create(body)
        };
        message.Headers.Add("PayPal-Request-Id", $"vault-token:{setupToken}");

        var response = await client.SendAsync(message);
        if (!response.IsSuccessStatusCode)
        {
            await LogPayPalErrorAsync(response, "PayPal setup token exchange rejected. SetupToken: {SetupToken}", setupToken);
            return null;
        }

        var token = await response.Content.ReadFromJsonAsync<PayPalPaymentTokenResponse>();
        return string.IsNullOrWhiteSpace(token?.Id) ? null : token;
    }

    /// <summary>
    /// 订单创建后未自动收款时补一次 capture；失败返回 null 由调用方沿用原订单。
    /// </summary>
    private async Task<PayPalOrderResponse?> CaptureOrderAsync(HttpClient client, PayPalOrderResponse order, string tradeNo)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"/v2/checkout/orders/{order.Id}/capture")
        {
            Content = JsonContent.Create(new { })
        };
        message.Headers.Add("PayPal-Request-Id", $"cap:{tradeNo}");

        var response = await client.SendAsync(message);
        if (!response.IsSuccessStatusCode)
        {
            await LogPayPalErrorAsync(response, "PayPal order capture rejected. TradeNo: {TradeNo}", tradeNo);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<PayPalOrderResponse>();
    }

    private static PaymentProviderPaymentMethodResult MapPaymentToken(PayPalPaymentTokenResponse token)
    {
        var wallet = token.PaymentSource?.PayPal;
        var card = token.PaymentSource?.Card;

        var (expiryYear, expiryMonth) = ParseCardExpiry(card?.Expiry);

        return new PaymentProviderPaymentMethodResult
        {
            Token = token.Id,
            ProviderCustomerId = token.Customer?.Id,
            MethodType = card != null ? PaymentMethod.CreditCard : PaymentMethod.PayPal,
            Brand = card?.Brand ?? "PayPal",
            Last4 = card?.LastDigits,
            AccountLabel = MaskEmail(wallet?.EmailAddress),
            ExpiryMonth = expiryMonth,
            ExpiryYear = expiryYear
        };
    }

    /// <summary>
    /// PayPal 卡片有效期是 <c>YYYY-MM</c> 字符串，拆成年月两个字段（解析不出就当没有有效期信息）。
    /// </summary>
    private static (int? Year, int? Month) ParseCardExpiry(string? expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry))
            return (null, null);

        var parts = expiry.Split('-');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var month))
        {
            return (null, null);
        }

        return month is < 1 or > 12 ? (null, null) : (year, month);
    }

    /// <summary>
    /// 邮箱脱敏：保留首字符与域名（<c>john@ex.com</c> → <c>j***@ex.com</c>）。
    /// 展示用的账户标识不该是一个完整邮箱——它会出现在管理端列表和支持工单里。
    /// </summary>
    private static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var at = email.IndexOf('@');
        if (at <= 0)
            return "***";

        var name = email[..at];
        var visible = name.Length <= 1 ? name : name[..1];
        return $"{visible}***{email[at..]}";
    }

    private static PaymentStatus MapPayPalCaptureStatus(string? status)
    {
        return status?.ToUpperInvariant() switch
        {
            "COMPLETED" => PaymentStatus.Succeeded,
            "DECLINED" or "FAILED" or "VOIDED" => PaymentStatus.Failed,
            _ => PaymentStatus.Processing
        };
    }

    private static string? FirstNonEmpty(params string?[] candidates)
        => candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

    /// <summary>
    /// 创建已带访问令牌的客户端；取不到令牌返回 null。
    /// </summary>
    private async Task<HttpClient?> CreateAuthorizedClientAsync()
    {
        var client = CreateHttpClient();
        var accessToken = await GetAccessTokenAsync(client);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            _logger.LogError("Failed to get PayPal access token.");
            return null;
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>
    /// 记录 PayPal 的错误响应并返回可读原因。
    /// PayPal 的失败细节全在响应体里（<c>name</c> + <c>details</c>），只记状态码等于什么都没记。
    /// </summary>
    private async Task<string?> LogPayPalErrorAsync(HttpResponseMessage response, string messageTemplate, object arg)
    {
        string? body = null;
        try
        {
            body = await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read PayPal error body.");
        }

        _logger.LogError(messageTemplate + " Status: {Status}, Body: {Body}", arg, response.StatusCode, body);
        return string.IsNullOrWhiteSpace(body) ? response.StatusCode.ToString() : body;
    }
}
