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
    /// 优惠券适用范围目标ID（订阅计划ID或产品ID）。
    /// </summary>
    /// <remarks>
    /// 限定了 <c>ApplyScope=Plan/Product</c> 的促销靠它判定是否适用。缺了它，
    /// 一张只对某个套餐有效的券会在"试算通过 → 核销被拒"之间自相矛盾。
    /// </remarks>
    public Guid? CouponScopeId { get; set; }

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

    /// <summary>
    /// 付款用户ID。后台扣款没有当前用户上下文，账单归属必须由调用方显式带上。
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 客户名称（开票/通知用）
    /// </summary>
    public string? CustomerName { get; set; }

    /// <summary>
    /// 客户邮箱（开票/通知用）
    /// </summary>
    public string? CustomerEmail { get; set; }
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
/// 手动确认收款 DTO（线下渠道：银行转账/汇款/现金等由运营核对到账后登记）
/// </summary>
public class ConfirmOfflinePaymentDto
{
    /// <summary>
    /// 实际到账金额；不传则按订单应付金额入账
    /// </summary>
    [Range(0.01, double.MaxValue, ErrorMessage = "Paid amount must be greater than 0.")]
    public decimal? PaidAmount { get; set; }

    /// <summary>
    /// 收款凭证号（银行流水号、支票号等），作为人工入账的审计依据
    /// </summary>
    [Required(ErrorMessage = "Payment reference is required for manual confirmation.")]
    [MaxLength(128, ErrorMessage = "Payment reference cannot exceed 128 characters.")]
    public string Reference { get; set; } = string.Empty;

    /// <summary>
    /// 到账时间；不传则按当前时间
    /// </summary>
    public DateTime? PaidTime { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    [MaxLength(500, ErrorMessage = "Remark cannot exceed 500 characters.")]
    public string? Remark { get; set; }
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
    /// 应付金额（已扣减折扣、已计入税额，即渠道实际收取的金额）
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 原始金额（折扣与税额计算前）
    /// </summary>
    public decimal OriginalAmount { get; set; }

    /// <summary>
    /// 折扣金额
    /// </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// 税额
    /// </summary>
    public decimal TaxAmount { get; set; }

    /// <summary>
    /// 已应用的优惠券代码
    /// </summary>
    public string? AppliedCouponCode { get; set; }

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
    /// 税额
    /// </summary>
    public decimal TaxAmount { get; set; }

    /// <summary>
    /// 应付金额（向渠道实际发起收款的金额）
    /// </summary>
    public decimal PayableAmount { get; set; }

    /// <summary>
    /// 币种
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// 付款用户ID
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 客户名称
    /// </summary>
    public string? CustomerName { get; set; }

    /// <summary>
    /// 客户邮箱
    /// </summary>
    public string? CustomerEmail { get; set; }

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
    /// 付款用户ID（管理端按客户筛选）
    /// </summary>
    public Guid? UserId { get; set; }

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

    /// <summary>
    /// 渠道侧客户标识（有已保存客户时透传，便于渠道复用支付方式）
    /// </summary>
    public string? ProviderCustomerId { get; set; }
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
    public string Currency { get; set; } = "USD";
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 支付提供商绑卡会话 DTO (Internal Use)
/// </summary>
public class PaymentProviderSetupDto
{
    /// <summary>
    /// 渠道侧已有客户标识（首次绑卡为空，由渠道创建）
    /// </summary>
    public string? ProviderCustomerId { get; set; }

    /// <summary>
    /// 用户标识（写入渠道侧 metadata，便于对账与排障）
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 客户名称
    /// </summary>
    public string? CustomerName { get; set; }

    /// <summary>
    /// 客户邮箱
    /// </summary>
    public string? CustomerEmail { get; set; }

    /// <summary>
    /// 付款人在渠道侧完成授权后跳回的地址（重定向式渠道必需，如 PayPal）
    /// </summary>
    public string? ReturnUrl { get; set; }

    /// <summary>
    /// 付款人在渠道侧放弃授权后跳回的地址（重定向式渠道必需，如 PayPal）
    /// </summary>
    public string? CancelUrl { get; set; }
}

/// <summary>
/// 支付提供商绑卡会话结果
/// </summary>
public class PaymentProviderSetupResult
{
    /// <summary>
    /// 渠道侧会话标识（如 Stripe SetupIntent ID、PayPal setup token ID）
    /// </summary>
    public string SetupId { get; set; } = string.Empty;

    /// <summary>
    /// 前端完成支付方式收集所需的密钥（内嵌式渠道用，如 Stripe）
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// 付款人授权地址（重定向式渠道用，如 PayPal）。
    /// 非空即表示这条链路要把用户送到渠道页面授权，而不是在本站内嵌收集。
    /// </summary>
    public string? ApprovalUrl { get; set; }

    /// <summary>
    /// 渠道侧客户标识（首次绑卡时为新建的客户）
    /// </summary>
    public string? ProviderCustomerId { get; set; }
}

/// <summary>
/// 支付提供商支付方式解析 DTO (Internal Use)
/// </summary>
public class PaymentProviderResolveMethodDto
{
    /// <summary>
    /// 渠道侧支付方式标识（如 Stripe PaymentMethod ID）
    /// </summary>
    public string PaymentMethodToken { get; set; } = string.Empty;

    /// <summary>
    /// 渠道侧客户标识
    /// </summary>
    public string? ProviderCustomerId { get; set; }

    /// <summary>
    /// 用户标识（首次绑卡需要新建渠道客户时使用）
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 客户名称
    /// </summary>
    public string? CustomerName { get; set; }

    /// <summary>
    /// 客户邮箱
    /// </summary>
    public string? CustomerEmail { get; set; }
}

/// <summary>
/// 支付提供商支付方式解析结果
/// </summary>
public class PaymentProviderPaymentMethodResult
{
    /// <summary>
    /// 可长期保存的支付方式引用
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// 渠道侧客户标识
    /// </summary>
    public string? ProviderCustomerId { get; set; }

    /// <summary>
    /// 支付方式类型
    /// </summary>
    public PaymentMethod MethodType { get; set; } = PaymentMethod.CreditCard;

    /// <summary>
    /// 卡组织/钱包类型
    /// </summary>
    public string? Brand { get; set; }

    /// <summary>
    /// 卡号尾四位
    /// </summary>
    public string? Last4 { get; set; }

    /// <summary>
    /// 有效期月份
    /// </summary>
    public int? ExpiryMonth { get; set; }

    /// <summary>
    /// 有效期年份
    /// </summary>
    public int? ExpiryYear { get; set; }

    /// <summary>
    /// 钱包账户标识（已脱敏，如 PayPal 付款人邮箱）。
    /// 钱包没有卡号尾四位，绑了两个 PayPal 账户时这是唯一能区分它们的展示信息。
    /// </summary>
    public string? AccountLabel { get; set; }
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

    /// <summary>
    /// 渠道事件唯一标识（如 Stripe evt_xxx / PayPal event id）。
    /// 重投的同一事件带同一个 ID，是回调去重的唯一可靠依据：
    /// 签名头每次投递都会重新生成，用它去重永远不会命中。
    /// </summary>
    public string? EventId { get; set; }

    /// <summary>
    /// 该事件是否属于本模块关心的事件。
    /// 渠道会推送大量无关事件（如 customer.updated），这类事件应被识别为“已接收但无需处理”，
    /// 直接回 200 结束，而不是当成失败让渠道无休止重投。
    /// </summary>
    public bool IsHandled { get; set; } = true;

    /// <summary>
    /// 事件种类。决定服务层怎么处理这条回调；缺省是支付状态变更。
    /// </summary>
    public PaymentCallbackKind Kind { get; set; } = PaymentCallbackKind.Payment;

    /// <summary>
    /// 被撤销的支付方式凭据（<see cref="Kind"/> 为 <see cref="PaymentCallbackKind.PaymentMethodRevoked"/> 时有值）
    /// </summary>
    public string? PaymentMethodToken { get; set; }
}
