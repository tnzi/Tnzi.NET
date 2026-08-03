namespace Tnzi.Payment.Dtos;

/// <summary>
/// 创建订阅 DTO
/// </summary>
public class CreateSubscriptionDto
{
    /// <summary>
    /// 订阅计划ID
    /// </summary>
    [Required]
    public Guid PlanId { get; set; }

    /// <summary>
    /// 支付渠道代码
    /// </summary>
    public string? ChannelCode { get; set; }

    /// <summary>
    /// 优惠券代码
    /// </summary>
    public string? CouponCode { get; set; }

    /// <summary>
    /// 是否试用
    /// </summary>
    public bool EnableTrial { get; set; }

    /// <summary>
    /// 渠道侧支付方式标识（如 Stripe PaymentMethod ID）。
    /// 提供后会登记为该用户已保存的支付方式并绑定到本订阅，使后续自动续费可用。
    /// </summary>
    public string? PaymentMethodToken { get; set; }

    /// <summary>
    /// 已保存的支付方式ID；与 <see cref="PaymentMethodToken"/> 二选一，都不传则用该渠道默认支付方式
    /// </summary>
    public Guid? PaymentMethodId { get; set; }

    /// <summary>
    /// 扩展数据
    /// </summary>
    public string? ExtraData { get; set; }
}

/// <summary>
/// 创建订阅结果：订阅本体 + 首期支付凭据。
/// </summary>
/// <remarks>
/// 此前只返回订阅本体，首期支付单的流水号与客户端密钥被直接丢弃，
/// 前端只能靠 BusinessOrderNo 反查支付列表才能拉起收银台。
/// </remarks>
public class SubscriptionCreateResultDto
{
    /// <summary>
    /// 订阅信息
    /// </summary>
    public SubscriptionDto Subscription { get; set; } = null!;

    /// <summary>
    /// 首期支付凭据；试用开通或零元订阅时为空（无需付款）
    /// </summary>
    public PaymentOrderResultDto? Payment { get; set; }

    /// <summary>
    /// 是否仍需付款才能激活
    /// </summary>
    public bool RequiresPayment => Payment != null;
}

/// <summary>
/// 暂停订阅 DTO
/// </summary>
public class PauseSubscriptionDto
{
    /// <summary>
    /// 恢复时间；不传表示手动恢复（在上限内可长期暂停）
    /// </summary>
    public DateTime? ResumeAt { get; set; }

    /// <summary>
    /// 暂停原因
    /// </summary>
    [MaxLength(500, ErrorMessage = "Reason cannot exceed 500 characters.")]
    public string? Reason { get; set; }
}

/// <summary>
/// 订阅 DTO
/// </summary>
public class SubscriptionDto
{
    /// <summary>
    /// 订阅ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 订阅流水号
    /// </summary>
    public string SubscriptionNo { get; set; } = string.Empty;

    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 计划ID
    /// </summary>
    public Guid PlanId { get; set; }

    /// <summary>
    /// 计划名称
    /// </summary>
    public string? PlanName { get; set; }

    /// <summary>
    /// 订阅状态
    /// </summary>
    public SubscriptionStatus Status { get; set; }

    /// <summary>
    /// 计费周期类型
    /// </summary>
    public BillingCycleType CycleType { get; set; }

    /// <summary>
    /// 计费周期值
    /// </summary>
    public int CycleValue { get; set; }

    /// <summary>
    /// 开始时间
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 结束时间
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// 下次计费时间
    /// </summary>
    public DateTime? NextBillingTime { get; set; }

    /// <summary>
    /// 试用开始时间
    /// </summary>
    public DateTime? TrialStartTime { get; set; }

    /// <summary>
    /// 试用结束时间
    /// </summary>
    public DateTime? TrialEndTime { get; set; }

    /// <summary>
    /// 原价
    /// </summary>
    public decimal OriginalPrice { get; set; }

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
    /// 是否自动续费
    /// </summary>
    public bool AutoRenew { get; set; }

    /// <summary>
    /// 产品代码
    /// </summary>
    public string? ProductCode { get; set; }

    /// <summary>
    /// 已绑定的支付方式ID
    /// </summary>
    public Guid? StoredPaymentMethodId { get; set; }

    /// <summary>
    /// 已绑定支付方式的卡组织（展示用）
    /// </summary>
    public string? PaymentMethodBrand { get; set; }

    /// <summary>
    /// 已绑定支付方式的尾号（展示用）
    /// </summary>
    public string? PaymentMethodLast4 { get; set; }

    /// <summary>
    /// 是否已绑定可用于自动续费的支付方式。
    /// 前端据此提示"未绑卡将无法自动续费"，而不是等到扣款失败才发现。
    /// </summary>
    public bool HasPaymentMethod { get; set; }

    /// <summary>
    /// 暂停起始时间。恢复时这段时长会补回下次计费日，
    /// 客服解释"为什么账单日推后了"时需要看到它。
    /// </summary>
    public DateTime? PausedAt { get; set; }

    /// <summary>
    /// 暂停恢复时间
    /// </summary>
    public DateTime? PausedUntil { get; set; }

    /// <summary>
    /// 逾期欠费起始时间
    /// </summary>
    public DateTime? PastDueSince { get; set; }

    /// <summary>
    /// 续费扣款连续失败次数
    /// </summary>
    public int RenewalRetryCount { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 订阅查询 DTO
/// </summary>
public class SubscriptionQueryDto : PagedQueryDto
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 订阅状态
    /// </summary>
    public SubscriptionStatus? Status { get; set; }

    /// <summary>
    /// 计划ID
    /// </summary>
    public Guid? PlanId { get; set; }

    /// <summary>
    /// 是否自动续费
    /// </summary>
    public bool? AutoRenew { get; set; }
}

/// <summary>
/// 订阅计划 DTO
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>
    /// 计划ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 计划代码
    /// </summary>
    public string PlanCode { get; set; } = string.Empty;

    /// <summary>
    /// 产品代码（同一产品下的多个计划互为升降级）
    /// </summary>
    public string? ProductCode { get; set; }

    /// <summary>
    /// 计划名称
    /// </summary>
    public string PlanName { get; set; } = string.Empty;

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 价格
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// 币种
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// 计费周期类型
    /// </summary>
    public BillingCycleType CycleType { get; set; }

    /// <summary>
    /// 计费周期值
    /// </summary>
    public int CycleValue { get; set; }

    /// <summary>
    /// 试用天数
    /// </summary>
    public int TrialDays { get; set; }

    /// <summary>
    /// 是否允许试用
    /// </summary>
    public bool AllowTrial { get; set; }

    /// <summary>
    /// 试用折扣
    /// </summary>
    public decimal? TrialDiscount { get; set; }

    /// <summary>
    /// 排序号
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsActive { get; set; }
}

/// <summary>
/// 取消订阅 DTO
/// </summary>
public class CancelSubscriptionDto
{
    /// <summary>
    /// 取消原因
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// 是否立即取消（false则到期后取消）
    /// </summary>
    public bool Immediate { get; set; }
}

/// <summary>
/// 更新自动续费 DTO
/// </summary>
public class UpdateAutoRenewDto
{
    /// <summary>
    /// 是否自动续费
    /// </summary>
    public bool AutoRenew { get; set; }
}

/// <summary>
/// 变更订阅计划 DTO
/// </summary>
public class ChangeSubscriptionPlanDto
{
    /// <summary>
    /// 新计划ID
    /// </summary>
    [Required]
    public Guid NewPlanId { get; set; }

    /// <summary>
    /// 是否立即生效（默认 true，升级立即，降级到期生效）
    /// </summary>
    public bool EffectiveImmediately { get; set; } = true;
}

/// <summary>
/// 订阅变更记录 DTO
/// </summary>
public class SubscriptionChangeDto
{
    /// <summary>
    /// 变更ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 订阅ID
    /// </summary>
    public Guid SubscriptionId { get; set; }

    /// <summary>
    /// 原计划ID
    /// </summary>
    public Guid FromPlanId { get; set; }

    /// <summary>
    /// 原计划名称
    /// </summary>
    public string? FromPlanName { get; set; }

    /// <summary>
    /// 新计划ID
    /// </summary>
    public Guid ToPlanId { get; set; }

    /// <summary>
    /// 新计划名称
    /// </summary>
    public string? ToPlanName { get; set; }

    /// <summary>
    /// 变更类型
    /// </summary>
    public SubscriptionChangeType ChangeType { get; set; }

    /// <summary>
    /// 按比例计算的金额
    /// </summary>
    public decimal ProratedAmount { get; set; }

    /// <summary>
    /// 生效日期
    /// </summary>
    public DateTime EffectiveDate { get; set; }

    /// <summary>
    /// 变更状态
    /// </summary>
    public SubscriptionChangeStatus Status { get; set; }

    /// <summary>
    /// 补差价支付凭据：需要补差且用户未绑卡时返回，供前端拉起收银台完成付款。
    /// 已绑卡时由后台直接扣款，此处为空。
    /// </summary>
    public PaymentOrderResultDto? Payment { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }
}
