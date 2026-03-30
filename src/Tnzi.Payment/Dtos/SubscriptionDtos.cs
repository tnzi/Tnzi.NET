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
    /// 支付方式标识（Stripe PaymentMethod ID）
    /// </summary>
    public string? PaymentMethodId { get; set; }

    /// <summary>
    /// 扩展数据
    /// </summary>
    public string? ExtraData { get; set; }
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
/// 变更订阅 DTO
/// </summary>
public class ChangeSubscriptionDto
{
    /// <summary>
    /// 新计划ID
    /// </summary>
    [Required]
    public Guid NewPlanId { get; set; }

    /// <summary>
    /// 变更时间（immediate=立即，period_end=周期结束时）
    /// </summary>
    public string EffectiveTime { get; set; } = "period_end";
}

/// <summary>
/// 更新支付方式 DTO
/// </summary>
public class UpdatePaymentMethodDto
{
    /// <summary>
    /// 支付方式ID
    /// </summary>
    [Required(ErrorMessage = "Payment method ID is required.")]
    [MaxLength(256, ErrorMessage = "Payment method ID cannot exceed 256 characters.")]
    public string PaymentMethodId { get; set; } = string.Empty;
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
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }
}
