namespace Tnzi.Payment.Providers.Models;

// PayPal REST API 反序列化模型。
// 仅供 PayPalProvider 内部解析渠道响应使用，不对外暴露，故声明为 internal。

/// <summary>
/// PayPal 订单响应
/// </summary>
internal class PayPalOrderResponse
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

internal class PayPalPurchaseUnit
{
    [JsonPropertyName("reference_id")]
    public string? ReferenceId { get; set; }

    [JsonPropertyName("amount")]
    public PayPalAmount? Amount { get; set; }

    [JsonPropertyName("payments")]
    public PayPalPurchaseUnitPayments? Payments { get; set; }
}

internal class PayPalPurchaseUnitPayments
{
    [JsonPropertyName("captures")]
    public List<PayPalCapture>? Captures { get; set; }
}

/// <summary>
/// PayPal 收款记录。off-session 扣款要用它的 <c>id</c> 作为退款依据，
/// 而不是订单 id —— 退款接口打的是 capture 不是 order。
/// </summary>
internal class PayPalCapture
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public PayPalAmount? Amount { get; set; }
}

internal class PayPalAmount
{
    [JsonPropertyName("currency_code")]
    public string CurrencyCode { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

internal class PayPalLink
{
    [JsonPropertyName("rel")]
    public string Rel { get; set; } = string.Empty;

    [JsonPropertyName("href")]
    public string Href { get; set; } = string.Empty;
}

internal class PayPalTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

internal class PayPalRefundResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

internal class PayPalWebhookVerifyResponse
{
    [JsonPropertyName("verification_status")]
    public string? VerificationStatus { get; set; }
}

// ---- Vault v3（Payment Method Tokens）----
// 保存 PayPal 账户供商户发起的后续扣款（reference transactions）使用。
// setup token 是一次性的授权凭据，payment token 才是可长期保存的引用。

/// <summary>
/// PayPal vault setup token 响应（<c>POST /v3/vault/setup-tokens</c>）
/// </summary>
internal class PayPalSetupTokenResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("customer")]
    public PayPalVaultCustomer? Customer { get; set; }

    [JsonPropertyName("links")]
    public List<PayPalLink>? Links { get; set; }
}

/// <summary>
/// PayPal vault payment token 响应（<c>POST/GET /v3/vault/payment-tokens</c>）
/// </summary>
internal class PayPalPaymentTokenResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("customer")]
    public PayPalVaultCustomer? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalVaultPaymentSource? PaymentSource { get; set; }
}

internal class PayPalVaultCustomer
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// 商户侧客户标识。绑定时写入本框架的 UserId，登记时据此确认这个 vault 凭据
    /// 确实是为当前用户创建的（防止拿到别人的 setup token 就能把别人的 PayPal 账户绑到自己名下）。
    /// </summary>
    [JsonPropertyName("merchant_customer_id")]
    public string? MerchantCustomerId { get; set; }
}

internal class PayPalVaultPaymentSource
{
    [JsonPropertyName("paypal")]
    public PayPalVaultWallet? PayPal { get; set; }

    [JsonPropertyName("card")]
    public PayPalVaultCard? Card { get; set; }
}

internal class PayPalVaultWallet
{
    [JsonPropertyName("email_address")]
    public string? EmailAddress { get; set; }

    [JsonPropertyName("payer_id")]
    public string? PayerId { get; set; }
}

internal class PayPalVaultCard
{
    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("last_digits")]
    public string? LastDigits { get; set; }

    /// <summary>
    /// 有效期，格式 <c>YYYY-MM</c>
    /// </summary>
    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }
}
