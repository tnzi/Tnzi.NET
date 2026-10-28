namespace Tnzi.Payment.Stripe.Options;

/// <summary>
/// Stripe配置选项
/// 配置路径：Payment:Stripe
/// </summary>
/// <remarks>
/// 随 <see cref="Providers.StripeProvider"/> 一起住在 <c>Tnzi.Payment.Stripe</c> 里：
/// 不接 Stripe 的应用没有理由带着一份它的凭据配置。<b>配置节路径与拆分前一致</b>，
/// 绑定在 <see cref="PaymentStripeModule.PreConfigureServicesAsync"/>。
/// <para>
/// 没有「Stripe 币种」这一项：建单币种按「请求指定 &gt; <c>Payment:Channels:Stripe:Currency</c> &gt;
/// <c>Payment:DefaultCurrency</c>」解析，渠道只用解析出来的那一个。此前这里有一个 <c>Currency</c>
/// （启用时还必填），却没有任何代码读它，配成 eur 的商户照样按默认币种建单。
/// Connect 的两项同理删除：框架不支持 Stripe Connect，留着它们只会让人以为支持。
/// </para>
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
    /// Publishable Key（可选）。
    /// </summary>
    /// <remarks>
    /// 建单、扣款、回调都只用 <see cref="SecretKey"/>。它经 <c>GET /payment-methods/client-config</c> 下发给浏览器，
    /// 供 Stripe.js / Stripe Elements 收卡与做 3-D Secure 验证；不用浏览器收卡的部署可以不填，故启用时不要求。
    /// </remarks>
    public string PublishableKey { get; set; } = string.Empty;

    /// <summary>
    /// Webhook Secret
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// 应用标记的元数据键，默认 <c>app</c>。只在 <see cref="MetadataStampValue"/> 有值时生效。
    /// </summary>
    public string MetadataStampKey { get; set; } = DefaultMetadataStampKey;

    /// <summary>
    /// 应用标记的值（例如 <c>crm</c>）。默认空 = 不打标记、不按标记过滤，与引入这一项之前逐字相同。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 给<b>同一个 Stripe 账户</b>被几个应用共用的部署用：有值时，本应用建的每一个 Customer、PaymentIntent、SetupIntent
    /// 都带上 <c>{MetadataStampKey}={MetadataStampValue}</c>，webhook 收到的 PaymentIntent 事件不带这个标记（或带着别的值）
    /// 就当作「不是我的」安静地答 200 忽略：不记告警、不去本地找支付，也就不会因为「找不到这笔支付」答 404 让 Stripe 反复重投。
    /// 按流水号回查 PaymentIntent 的搜索同样加上这个条件。
    /// </para>
    /// <para>
    /// ★ 支付方式（PaymentMethod）事件不带本应用的元数据（卡是 Stripe Elements 建的），所以不按标记过滤：
    /// 撤卡与卡片更新按凭据在本地查找，别的应用的卡在这里本来就查不到，查不到即安静地成功。
    /// </para>
    /// <para>
    /// ★ 打开它之前建的 PaymentIntent 不带标记，打开之后它们的回调会被当成别人的忽略。打开前让在途的付款落定，
    /// 或接受这些单由过期清扫去渠道查一次后收口。
    /// </para>
    /// </remarks>
    public string? MetadataStampValue { get; set; }

    /// <summary>
    /// 每笔卡支付的账单描述后缀（Stripe 的 <c>statement_descriptor_suffix</c>），默认空 = 不设。
    /// </summary>
    /// <remarks>
    /// 账户级的前缀由几个应用共用时，用它让持卡人在对账单上分得清是哪一个产品。
    /// 前缀与后缀合起来不能超过 22 个字符（Stripe 的限制，本地只知道后缀，超出时 Stripe 会拒绝建单）；
    /// 后缀至少要有一个字母，不能含 <c>&lt; &gt; \ ' " *</c>。
    /// </remarks>
    public string? StatementDescriptorSuffix { get; set; }

    /// <summary><see cref="MetadataStampKey"/> 的出厂值。</summary>
    public const string DefaultMetadataStampKey = "app";

    /// <summary>Stripe 允许的账单描述总长度（前缀 + 后缀）。</summary>
    public const int StatementDescriptorMaxLength = 22;

    /// <summary>应用标记是否已配置。</summary>
    public bool HasMetadataStamp => !string.IsNullOrWhiteSpace(MetadataStampValue) && !string.IsNullOrWhiteSpace(MetadataStampKey);
}
