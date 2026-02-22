namespace Tnzi.Payment.Options;

/// <summary>
/// Stripe配置选项
/// 配置路径：Payment:Stripe
/// </summary>
public class StripeOptions
{
    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Secret Key
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Publishable Key
    /// </summary>
    public string PublishableKey { get; set; } = string.Empty;

    /// <summary>
    /// Webhook Secret
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// 币种
    /// </summary>
    public string Currency { get; set; } = "usd";

    /// <summary>
    /// 是否启用Connect
    /// </summary>
    public bool ConnectEnabled { get; set; }

    /// <summary>
    /// Connect Client ID
    /// </summary>
    public string? ConnectClientId { get; set; }
}

/// <summary>
/// PayPal配置选项
/// 配置路径：Payment:PayPal
/// </summary>
public class PayPalOptions
{
    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Client ID
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Client Secret
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// 模式（sandbox/live）
    /// </summary>
    public string Mode { get; set; } = "sandbox";

    /// <summary>
    /// 币种
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Webhook ID
    /// </summary>
    public string? WebhookId { get; set; }

    /// <summary>
    /// 品牌名称
    /// </summary>
    public string BrandName { get; set; } = string.Empty;

    /// <summary>
    /// 支付成功返回URL
    /// </summary>
    public string? ReturnUrl { get; set; }

    /// <summary>
    /// 支付取消返回URL
    /// </summary>
    public string? CancelUrl { get; set; }
}
