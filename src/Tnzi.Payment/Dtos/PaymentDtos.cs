namespace Tnzi.Payment.Dtos;

/// <summary>
/// 创建支付 DTO
/// </summary>
public class CreatePaymentDto
{
    /// <summary>
    /// 业务订单号
    /// </summary>
    [Required]
    public string BusinessOrderNo { get; set; } = string.Empty;

    /// <summary>
    /// 业务类型
    /// </summary>
    public BusinessType BusinessType { get; set; }

    /// <summary>
    /// 金额
    /// </summary>
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0.")]
    public decimal Amount { get; set; }

    /// <summary>
    /// 币种
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// 支付渠道代码
    /// </summary>
    public string? ChannelCode { get; set; }

    /// <summary>
    /// 支付方式
    /// </summary>
    public PaymentMethod? PaymentMethod { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 过期时间（分钟）
    /// </summary>
    public int? ExpireMinutes { get; set; }

    /// <summary>
    /// 优惠券代码
    /// </summary>
    public string? CouponCode { get; set; }

    /// <summary>
    /// 回调URL
    /// </summary>
    public string? ReturnUrl { get; set; }

    /// <summary>
    /// 扩展数据（JSON格式）
    /// </summary>
    public string? ExtraData { get; set; }
}

/// <summary>
/// off-session 自动扣款请求 DTO（后台续费/试用转正使用，使用渠道侧已保存的支付方式无人值守扣款）
/// </summary>
public class OffSessionChargeDto
{
    /// <summary>
    /// 业务订单号
    /// </summary>
    [Required]
    public string BusinessOrderNo { get; set; } = string.Empty;

    /// <summary>
    /// 业务类型
    /// </summary>
    public BusinessType BusinessType { get; set; }

    /// <summary>
    /// 金额
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 币种
    /// </summary>
    public string? Currency { get; set; }

    /// <summary>
    /// 支付渠道代码
    /// </summary>
    public string? ChannelCode { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 渠道侧客户标识（如 Stripe Customer ID）
    /// </summary>
    public string? ProviderCustomerId { get; set; }

    /// <summary>
    /// 渠道侧已保存的支付方式标识（如 Stripe PaymentMethod ID）
    /// </summary>
    public string PaymentMethodToken { get; set; } = string.Empty;

    /// <summary>
    /// 扩展数据（JSON，承载业务用途，如订阅计费 purpose）
    /// </summary>
    public string? ExtraData { get; set; }
}

/// <summary>
/// 关闭支付 DTO
/// </summary>
public class ClosePaymentDto
{
    /// <summary>
    /// 关闭原因
    /// </summary>
    [MaxLength(500, ErrorMessage = "Reason cannot exceed 500 characters.")]
    public string? Reason { get; set; }
}

/// <summary>
/// 支付订单结果 DTO
/// </summary>
public class PaymentOrderResultDto
{
    /// <summary>
    /// 交易流水号
    /// </summary>
    public string TradeNo { get; set; } = string.Empty;

    /// <summary>
    /// 支付参数（用于前端跳转或SDK调用）
    /// </summary>
    public string? PayParams { get; set; }

    /// <summary>
    /// 支付URL
    /// </summary>
    public string? PayUrl { get; set; }

    /// <summary>
    /// 过期时间
    /// </summary>
    public DateTime? ExpireTime { get; set; }

    /// <summary>
    /// 金额
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 币种
    /// </summary>
    public string Currency { get; set; } = "USD";
}

/// <summary>
/// 支付信息 DTO
/// </summary>
public class PaymentDto
{
    /// <summary>
    /// 支付ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 交易流水号
    /// </summary>
    public string TradeNo { get; set; } = string.Empty;

    /// <summary>
    /// 外部交易流水号
    /// </summary>
    public string? ExternalTradeNo { get; set; }

    /// <summary>
    /// 业务订单号
    /// </summary>
    public string BusinessOrderNo { get; set; } = string.Empty;

    /// <summary>
    /// 业务类型
    /// </summary>
    public BusinessType BusinessType { get; set; }

    /// <summary>
    /// 原始金额
    /// </summary>
    public decimal OriginalAmount { get; set; }

    /// <summary>
    /// 已付金额
    /// </summary>
    public decimal PaidAmount { get; set; }

    /// <summary>
    /// 折扣金额
    /// </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// 币种
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// 支付状态
    /// </summary>
    public PaymentStatus Status { get; set; }

    /// <summary>
    /// 支付渠道
    /// </summary>
    public string ChannelCode { get; set; } = string.Empty;

    /// <summary>
    /// 支付方式
    /// </summary>
    public PaymentMethod PaymentMethod { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 过期时间
    /// </summary>
    public DateTime? ExpireTime { get; set; }

    /// <summary>
    /// 支付完成时间
    /// </summary>
    public DateTime? PaidTime { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 支付查询 DTO
/// </summary>
public class PaymentQueryDto : PagedQueryDto
{
    /// <summary>
    /// 交易流水号
    /// </summary>
    public string? TradeNo { get; set; }

    /// <summary>
    /// 外部交易流水号
    /// </summary>
    public string? ExternalTradeNo { get; set; }

    /// <summary>
    /// 业务订单号
    /// </summary>
    public string? BusinessOrderNo { get; set; }

    /// <summary>
    /// 业务类型
    /// </summary>
    public BusinessType? BusinessType { get; set; }

    /// <summary>
    /// 支付状态
    /// </summary>
    public PaymentStatus? Status { get; set; }

    /// <summary>
    /// 支付渠道
    /// </summary>
    public string? ChannelCode { get; set; }

    /// <summary>
    /// 开始时间
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// 结束时间
    /// </summary>
    public DateTime? EndTime { get; set; }
}

/// <summary>
/// 支付回调 DTO
/// </summary>
public class PaymentCallbackDto
{
    /// <summary>
    /// 渠道代码
    /// </summary>
    public string ChannelCode { get; set; } = string.Empty;

    /// <summary>
    /// 回调参数
    /// </summary>
    public IDictionary<string, string> Parameters { get; set; } = null!;
}

/// <summary>
/// 支付参数字段 DTO（用于前端集成）
/// </summary>
public class PaymentParamsDto
{
    /// <summary>
    /// 交易流水号
    /// </summary>
    public string TradeNo { get; set; } = string.Empty;

    /// <summary>
    /// 客户端密钥（Stripe）
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// PayPal Order ID
    /// </summary>
    public string? OrderId { get; set; }

    /// <summary>
    /// 可用的支付方式列表
    /// </summary>
    public List<string> AvailableMethods { get; set; } = new();
}

/// <summary>
/// 支付提供商创建支付 DTO (Internal Use)
/// </summary>
public class PaymentProviderCreateDto
{
    public string TradeNo { get; set; } = string.Empty;
    public string BusinessOrderNo { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string? Description { get; set; }
    public DateTime? ExpireTime { get; set; }
    public string? ReturnUrl { get; set; }
    public string? ExtraData { get; set; }
}

/// <summary>
/// 支付提供商退款 DTO (Internal Use)
/// </summary>
public class PaymentProviderRefundDto
{
    public string TradeNo { get; set; } = string.Empty;
    public string? ExternalTradeNo { get; set; }
    public string RefundNo { get; set; } = string.Empty;
    public decimal RefundAmount { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 支付提供商 off-session 自动扣款 DTO (Internal Use)
/// 使用渠道侧已保存的客户/支付方式发起后台无人值守扣款
/// </summary>
public class PaymentProviderChargeDto
{
    public string TradeNo { get; set; } = string.Empty;
    public string BusinessOrderNo { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string? Description { get; set; }

    /// <summary>
    /// 渠道侧客户标识（如 Stripe Customer ID）
    /// </summary>
    public string? ProviderCustomerId { get; set; }

    /// <summary>
    /// 渠道侧已保存的支付方式标识（如 Stripe PaymentMethod ID）
    /// </summary>
    public string PaymentMethodToken { get; set; } = string.Empty;
}

/// <summary>
/// 支付提供商 off-session 扣款结果
/// </summary>
public class PaymentProviderChargeResult
{
    public string TradeNo { get; set; } = string.Empty;
    public string? ExternalTradeNo { get; set; }

    /// <summary>
    /// 扣款后的支付状态（Succeeded=已收款；Processing=需进一步动作/异步确认；Failed=失败）
    /// </summary>
    public PaymentStatus Status { get; set; }
    public decimal PaidAmount { get; set; }
    public string? FailReason { get; set; }
}

/// <summary>
/// 支付提供商创建订单结果
/// </summary>
public class PaymentProviderOrderResult
{
    public string TradeNo { get; set; } = string.Empty;
    public string? ExternalTradeNo { get; set; }
    public string? PayParams { get; set; }
    public string? PayUrl { get; set; }
    public DateTime? ExpireTime { get; set; }
}

/// <summary>
/// 支付提供商查询结果
/// </summary>
public class PaymentProviderQueryResult
{
    public string TradeNo { get; set; } = string.Empty;
    public string? ExternalTradeNo { get; set; }
    public PaymentStatus Status { get; set; }
    public decimal Amount { get; set; }
    public DateTime? PaidTime { get; set; }
    public string? FailReason { get; set; }
}

/// <summary>
/// 支付提供商退款结果
/// </summary>
public class PaymentProviderRefundResult
{
    public string RefundNo { get; set; } = string.Empty;
    public string? ExternalRefundNo { get; set; }
    public decimal RefundAmount { get; set; }
    public RefundStatus Status { get; set; }
    public DateTime? CompletedTime { get; set; }
}

/// <summary>
/// 支付提供商退款查询结果
/// </summary>
public class PaymentProviderRefundQueryResult
{
    public string RefundNo { get; set; } = string.Empty;
    public string? ExternalRefundNo { get; set; }
    public RefundStatus Status { get; set; }
    public decimal RefundAmount { get; set; }
    public DateTime? CompletedTime { get; set; }
}

/// <summary>
/// 支付提供商回调处理结果
/// </summary>
public class PaymentProviderCallbackResult
{
    public string TradeNo { get; set; } = string.Empty;
    public PaymentStatus Status { get; set; }
    public decimal PaidAmount { get; set; }
    public string? ExternalTradeNo { get; set; }
    public string? FailReason { get; set; }
}
