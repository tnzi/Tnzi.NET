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

    /// <summary>
    /// 是否启用 Vault（保存 PayPal 账户用于后续无人值守扣款，即自动续费）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 默认关闭是有意的：PayPal 的商户发起交易（reference transactions，旧称 billing agreements）
    /// 需要 PayPal 逐个商户账号<b>单独审批开通</b>，未开通的账号调用 vault 接口会被拒。
    /// 关着时框架如实报告"该渠道不支持保存支付方式"，而不是让用户走完整个绑定流程再在
    /// 扣款当天失败。向 PayPal 申请开通后再打开它。
    /// </remarks>
    public bool EnableVault { get; set; }

    /// <summary>
    /// 绑定 PayPal 账户时付款人授权后跳回的地址。缺省回退到 <see cref="ReturnUrl"/>。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="ReturnUrl"/> 分开是因为两者语义不同：一个是"付完款回哪儿"，
    /// 一个是"绑完账户回哪儿"，通常是应用里两个不同页面。
    /// </remarks>
    public string? VaultReturnUrl { get; set; }

    /// <summary>
    /// 绑定 PayPal 账户时付款人放弃授权后跳回的地址。缺省回退到 <see cref="CancelUrl"/>。
    /// </summary>
    public string? VaultCancelUrl { get; set; }

    /// <summary>
    /// 商户发起扣款的用途模式（PayPal <c>usage_pattern</c>）。
    /// 默认 <c>SUBSCRIPTION_PREPAID</c>：订阅制先付费后使用，与本模块的订阅计费一致。
    /// 按用量后付费的应用应改为 <c>SUBSCRIPTION_POSTPAID</c>。
    /// </summary>
    public string VaultUsagePattern { get; set; } = "SUBSCRIPTION_PREPAID";
}
