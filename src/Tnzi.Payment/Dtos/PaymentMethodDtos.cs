namespace Tnzi.Payment.Dtos;

/// <summary>
/// 创建绑卡会话请求 DTO
/// </summary>
public class CreateSetupSessionDto
{
    /// <summary>
    /// 支付渠道代码（缺省用默认渠道）
    /// </summary>
    public string? ChannelCode { get; set; }

    /// <summary>
    /// 付款人在渠道页面完成授权后跳回的地址（重定向式渠道用，如 PayPal）。
    /// 缺省取该渠道配置里的绑卡回跳地址。
    /// </summary>
    [MaxLength(512, ErrorMessage = "Return URL cannot exceed 512 characters.")]
    public string? ReturnUrl { get; set; }

    /// <summary>
    /// 付款人放弃授权后跳回的地址（重定向式渠道用，如 PayPal）
    /// </summary>
    [MaxLength(512, ErrorMessage = "Cancel URL cannot exceed 512 characters.")]
    public string? CancelUrl { get; set; }
}

/// <summary>
/// 绑卡会话结果 DTO。两种形态二选一，前端按哪个字段有值来决定怎么走：
/// <list type="bullet">
/// <item><b>内嵌式</b>（<see cref="ClientSecret"/> 非空，如 Stripe）：调渠道 SDK 就地收集支付方式（含 3DS）。</item>
/// <item><b>重定向式</b>（<see cref="ApprovalUrl"/> 非空，如 PayPal）：把用户整页送到该地址授权，
/// 授权后 PayPal 带着 <c>approval_token_id</c> 跳回 ReturnUrl。</item>
/// </list>
/// 两种形态最终都用 <see cref="SetupId"/> 调 <c>POST /payment-methods</c> 登记。
/// </summary>
public class SetupSessionDto
{
    /// <summary>
    /// 支付渠道代码
    /// </summary>
    public string ChannelCode { get; set; } = string.Empty;

    /// <summary>
    /// 渠道侧会话标识。重定向式渠道授权完成后，把它当作 <c>PaymentMethodToken</c> 提交登记。
    /// </summary>
    public string SetupId { get; set; } = string.Empty;

    /// <summary>
    /// 前端完成支付方式收集所需的密钥（内嵌式渠道）
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// 付款人授权地址（重定向式渠道）
    /// </summary>
    public string? ApprovalUrl { get; set; }
}

/// <summary>
/// 登记（绑定）支付方式请求 DTO
/// </summary>
public class BindPaymentMethodDto
{
    /// <summary>
    /// 渠道侧支付方式标识。内嵌式渠道传 SDK 返回的支付方式 ID；
    /// 重定向式渠道（PayPal）传绑卡会话的 <c>SetupId</c>，服务端负责换成长期可用的凭据。
    /// </summary>
    [Required(ErrorMessage = "Payment method token is required.")]
    [MaxLength(128, ErrorMessage = "Payment method token cannot exceed 128 characters.")]
    public string PaymentMethodToken { get; set; } = string.Empty;

    /// <summary>
    /// 支付渠道代码（缺省用默认渠道）
    /// </summary>
    public string? ChannelCode { get; set; }

    /// <summary>
    /// 是否设为默认支付方式（用户首个支付方式总是默认）
    /// </summary>
    public bool SetAsDefault { get; set; } = true;
}

/// <summary>
/// 已保存支付方式 DTO
/// </summary>
public class StoredPaymentMethodDto
{
    /// <summary>
    /// 支付方式ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 支付渠道代码
    /// </summary>
    public string ChannelCode { get; set; } = string.Empty;

    /// <summary>
    /// 支付方式类型
    /// </summary>
    public PaymentMethod MethodType { get; set; }

    /// <summary>
    /// 卡组织/钱包类型
    /// </summary>
    public string? Brand { get; set; }

    /// <summary>
    /// 卡号尾四位
    /// </summary>
    public string? Last4 { get; set; }

    /// <summary>
    /// 钱包账户标识（已脱敏，如 PayPal 付款人邮箱）。钱包没有卡号尾四位，
    /// 绑了两个 PayPal 账户时这是唯一能区分它们的展示信息。
    /// </summary>
    public string? AccountLabel { get; set; }

    /// <summary>
    /// 有效期月份
    /// </summary>
    public int? ExpiryMonth { get; set; }

    /// <summary>
    /// 有效期年份
    /// </summary>
    public int? ExpiryYear { get; set; }

    /// <summary>
    /// 是否为默认支付方式
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// 是否已过有效期
    /// </summary>
    public bool IsExpired { get; set; }

    /// <summary>
    /// 最近一次成功扣款时间
    /// </summary>
    public DateTime? LastUsedTime { get; set; }

    /// <summary>
    /// 绑定时间
    /// </summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 将支付方式绑定到订阅请求 DTO
/// </summary>
public class AttachPaymentMethodDto
{
    /// <summary>
    /// 已保存的支付方式ID；为空时使用该渠道下的默认支付方式
    /// </summary>
    public Guid? PaymentMethodId { get; set; }

    /// <summary>
    /// 渠道侧支付方式标识；提供时先登记为已保存支付方式再绑定（前端一步完成绑卡+绑订阅）
    /// </summary>
    [MaxLength(128, ErrorMessage = "Payment method token cannot exceed 128 characters.")]
    public string? PaymentMethodToken { get; set; }
}
