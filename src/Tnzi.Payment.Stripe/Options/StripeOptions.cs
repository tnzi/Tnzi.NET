namespace Tnzi.Payment.Stripe.Options;

/// <summary>
/// Stripe配置选项
/// 配置路径：Payment:Stripe
/// </summary>
/// <remarks>
/// 随 <see cref="Providers.StripeProvider"/> 一起住在 <c>Tnzi.Payment.Stripe</c> 里：
/// 不接 Stripe 的应用没有理由带着一份它的凭据配置。<b>配置节路径与拆分前一致</b>，
/// 绑定在 <see cref="PaymentStripeModule.PreConfigureServicesAsync"/>。
/// </remarks>
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
