namespace Tnzi.Payment.Dtos;

/// <summary>
/// 创建促销 DTO
/// </summary>
public class CreatePromotionDto
{
    /// <summary>
    /// 促销代码
    /// </summary>
    [Required]
    [RegularExpression(@"^[A-Za-z0-9_-]+$", ErrorMessage = "Promotion code can only contain letters, numbers, underscores and hyphens.")]
    public string PromotionCode { get; set; } = string.Empty;

    /// <summary>
    /// 促销名称
    /// </summary>
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 促销类型
    /// </summary>
    [Required]
    public PromotionType Type { get; set; }

    /// <summary>
    /// 折扣值
    /// </summary>
    [Range(0, double.MaxValue, ErrorMessage = "Discount value cannot be negative.")]
    public decimal DiscountValue { get; set; }

    /// <summary>
    /// 折扣类型
    /// </summary>
    [Required]
    public DiscountType DiscountType { get; set; }

    /// <summary>
    /// 最大折扣金额
    /// </summary>
    public decimal? MaxDiscountAmount { get; set; }

    /// <summary>
    /// 最低订单金额
    /// </summary>
    public decimal? MinimumOrderAmount { get; set; }

    /// <summary>
    /// 产品类型
    /// </summary>
    public ProductType ProductType { get; set; } = ProductType.All;

    /// <summary>
    /// 应用范围
    /// </summary>
    public ApplyScope ApplyScope { get; set; } = ApplyScope.Global;

    /// <summary>
    /// 范围ID列表
    /// </summary>
    public List<Guid>? ScopeIds { get; set; }

    /// <summary>
    /// 开始时间
    /// </summary>
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 结束时间
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// 总使用次数限制
    /// </summary>
    public int? TotalUsageLimit { get; set; }

    /// <summary>
    /// 每用户使用次数限制
    /// </summary>
    public int? PerUserUsageLimit { get; set; }

    /// <summary>
    /// 是否可叠加
    /// </summary>
    public bool Stackable { get; set; } = false;

    /// <summary>
    /// 优先级
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// 是否仅首次订阅可用
    /// </summary>
    public bool FirstSubscriptionOnly { get; set; } = false;

    /// <summary>
    /// 币种（固定金额折扣时生效）
    /// </summary>
    public string Currency { get; set; } = PaymentConstants.DefaultCurrency;

    /// <summary>
    /// 是否公开：公开促销任何用户输码即可用；非公开促销必须先通过兑换码领取
    /// </summary>
    public bool IsPublic { get; set; } = true;
}

/// <summary>
/// 更新促销 DTO
/// </summary>
public class UpdatePromotionDto
{
    /// <summary>
    /// 促销名称
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 折扣值
    /// </summary>
    [Range(0, double.MaxValue, ErrorMessage = "Discount value cannot be negative.")]
    public decimal? DiscountValue { get; set; }

    /// <summary>
    /// 最大折扣金额
    /// </summary>
    public decimal? MaxDiscountAmount { get; set; }

    /// <summary>
    /// 最低订单金额
    /// </summary>
    public decimal? MinimumOrderAmount { get; set; }

    /// <summary>
    /// 结束时间
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// 总使用次数限制
    /// </summary>
    public int? TotalUsageLimit { get; set; }

    /// <summary>
    /// 每用户使用次数限制
    /// </summary>
    public int? PerUserUsageLimit { get; set; }

    /// <summary>
    /// 是否可叠加
    /// </summary>
    public bool? Stackable { get; set; }

    /// <summary>
    /// 优先级
    /// </summary>
    public int? Priority { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool? IsActive { get; set; }

    /// <summary>
    /// 是否公开
    /// </summary>
    public bool? IsPublic { get; set; }
}

/// <summary>
/// 促销 DTO
/// </summary>
public class PromotionDto
{
    /// <summary>
    /// 促销ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 促销代码
    /// </summary>
    public string PromotionCode { get; set; } = string.Empty;

    /// <summary>
    /// 促销名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 促销类型
    /// </summary>
    public PromotionType Type { get; set; }

    /// <summary>
    /// 折扣值
    /// </summary>
    public decimal DiscountValue { get; set; }

    /// <summary>
    /// 折扣类型
    /// </summary>
    public DiscountType DiscountType { get; set; }

    /// <summary>
    /// 最大折扣金额
    /// </summary>
    public decimal? MaxDiscountAmount { get; set; }

    /// <summary>
    /// 最低订单金额
    /// </summary>
    public decimal? MinimumOrderAmount { get; set; }

    /// <summary>
    /// 产品类型
    /// </summary>
    public ProductType ProductType { get; set; }

    /// <summary>
    /// 应用范围
    /// </summary>
    public ApplyScope ApplyScope { get; set; }

    /// <summary>
    /// 开始时间
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 结束时间
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// 总使用次数限制
    /// </summary>
    public int? TotalUsageLimit { get; set; }

    /// <summary>
    /// 已使用次数
    /// </summary>
    public int UsedCount { get; set; }

    /// <summary>
    /// 每用户使用次数限制
    /// </summary>
    public int? PerUserUsageLimit { get; set; }

    /// <summary>
    /// 是否可叠加
    /// </summary>
    public bool Stackable { get; set; }

    /// <summary>
    /// 优先级
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// 是否仅首次订阅可用
    /// </summary>
    public bool FirstSubscriptionOnly { get; set; }

    /// <summary>
    /// 币种（固定金额折扣时生效）
    /// </summary>
    public string Currency { get; set; } = PaymentConstants.DefaultCurrency;

    /// <summary>
    /// 是否公开
    /// </summary>
    public bool IsPublic { get; set; }

    /// <summary>
    /// 范围ID列表（ApplyScope=Plan/Product 时限定的目标）
    /// </summary>
    public List<Guid> ScopeIds { get; set; } = [];

    /// <summary>
    /// 是否有效（考虑时间和次数限制）
    /// </summary>
    public bool IsValid { get; set; }
}

/// <summary>
/// 促销查询 DTO
/// </summary>
public class PromotionQueryDto : PagedQueryDto
{
    /// <summary>
    /// 促销代码
    /// </summary>
    public string? PromotionCode { get; set; }

    /// <summary>
    /// 促销类型
    /// </summary>
    public PromotionType? Type { get; set; }

    /// <summary>
    /// 产品类型
    /// </summary>
    public ProductType? ProductType { get; set; }

    /// <summary>
    /// 是否仅显示有效的
    /// </summary>
    public bool ActiveOnly { get; set; }
}

/// <summary>
/// 验证优惠券 DTO
/// </summary>
public class ValidateCouponDto
{
    /// <summary>
    /// 优惠券代码
    /// </summary>
    [Required]
    public string CouponCode { get; set; } = string.Empty;

    /// <summary>
    /// 订单金额
    /// </summary>
    [Range(0.01, double.MaxValue, ErrorMessage = "Order amount must be greater than 0.")]
    public decimal OrderAmount { get; set; }

    /// <summary>
    /// 产品ID / 订阅计划ID（用于校验促销的适用范围）
    /// </summary>
    public Guid? ProductId { get; set; }

    /// <summary>
    /// 产品类型（用于校验促销的适用产品类型）
    /// </summary>
    public ProductType ProductType { get; set; } = ProductType.All;

    /// <summary>
    /// 订单币种。固定金额折扣与币种强相关，多币种应用不传会按默认币种误判。
    /// </summary>
    public string? Currency { get; set; }
}

/// <summary>
/// 优惠券核销上下文：把"这张券用在哪一单上"的完整信息一次带齐，
/// 使范围校验（适用产品/计划、首单限定）与幂等（同一业务单号只核销一次）成为可能。
/// </summary>
public class CouponApplyContext
{
    /// <summary>
    /// 优惠券代码
    /// </summary>
    [Required]
    public string CouponCode { get; set; } = string.Empty;

    /// <summary>
    /// 使用者
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 业务订单号（核销幂等键的一部分）
    /// </summary>
    public string BusinessOrderNo { get; set; } = string.Empty;

    /// <summary>
    /// 折扣计算基数（未折扣、未计税的原始金额）
    /// </summary>
    public decimal OrderAmount { get; set; }

    /// <summary>
    /// 币种
    /// </summary>
    public string Currency { get; set; } = PaymentConstants.DefaultCurrency;

    /// <summary>
    /// 产品类型
    /// </summary>
    public ProductType ProductType { get; set; } = ProductType.All;

    /// <summary>
    /// 适用范围目标ID（订阅计划ID或产品ID），用于校验 ApplyScope=Plan/Product 的促销
    /// </summary>
    public Guid? ScopeId { get; set; }

    /// <summary>
    /// 关联支付ID
    /// </summary>
    public Guid? PaymentId { get; set; }

    /// <summary>
    /// 关联订阅ID
    /// </summary>
    public Guid? SubscriptionId { get; set; }

    /// <summary>
    /// 业务订单ID
    /// </summary>
    public Guid? OrderId { get; set; }
}

/// <summary>
/// 优惠券试算结果（不产生核销记录）
/// </summary>
public class CouponPreviewDto
{
    /// <summary>
    /// 促销ID
    /// </summary>
    public Guid PromotionId { get; set; }

    /// <summary>
    /// 优惠券代码
    /// </summary>
    public string CouponCode { get; set; } = string.Empty;

    /// <summary>
    /// 折扣金额
    /// </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// 折后金额
    /// </summary>
    public decimal FinalAmount { get; set; }
}

/// <summary>
/// 优惠券验证结果 DTO
/// </summary>
public class CouponValidationResultDto
{
    /// <summary>
    /// 是否有效
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// 优惠券代码
    /// </summary>
    public string? CouponCode { get; set; }

    /// <summary>
    /// 促销信息
    /// </summary>
    public PromotionDto? Promotion { get; set; }

    /// <summary>
    /// 折扣金额
    /// </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 计算折扣 DTO
/// </summary>
public class CalculateDiscountDto
{
    /// <summary>
    /// 优惠券代码
    /// </summary>
    [Required]
    public string CouponCode { get; set; } = string.Empty;

    /// <summary>
    /// 订单金额
    /// </summary>
    [Range(0.01, double.MaxValue, ErrorMessage = "Order amount must be greater than 0.")]
    public decimal OrderAmount { get; set; }

    /// <summary>
    /// 产品ID / 订阅计划ID（用于校验促销的适用范围）
    /// </summary>
    public Guid? ProductId { get; set; }

    /// <summary>
    /// 产品类型
    /// </summary>
    public ProductType ProductType { get; set; } = ProductType.All;

    /// <summary>
    /// 订单币种。固定金额折扣与币种强相关，多币种应用不传会按默认币种误判。
    /// </summary>
    public string? Currency { get; set; }
}

/// <summary>
/// 计算折扣结果 DTO
/// </summary>
public class DiscountCalculationResultDto
{
    /// <summary>
    /// 优惠券代码
    /// </summary>
    public string CouponCode { get; set; } = string.Empty;

    /// <summary>
    /// 原价
    /// </summary>
    public decimal OriginalAmount { get; set; }

    /// <summary>
    /// 折扣金额
    /// </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// 最终金额
    /// </summary>
    public decimal FinalAmount { get; set; }

    /// <summary>
    /// 折扣类型
    /// </summary>
    public DiscountType DiscountType { get; set; }
}

/// <summary>
/// 优惠券使用记录 DTO
/// </summary>
public class CouponUsageDto
{
    /// <summary>
    /// 使用记录ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 优惠券代码
    /// </summary>
    public string CouponCode { get; set; } = string.Empty;

    /// <summary>
    /// 折扣金额
    /// </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// 使用时间
    /// </summary>
    public DateTime UsedTime { get; set; }

    /// <summary>
    /// 业务订单ID
    /// </summary>
    public Guid? OrderId { get; set; }

    /// <summary>
    /// 业务订单号
    /// </summary>
    public string? BusinessOrderNo { get; set; }

    /// <summary>
    /// 关联支付ID
    /// </summary>
    public Guid? PaymentId { get; set; }
}

/// <summary>
/// 用户可用优惠券 DTO
/// </summary>
public class UserCouponDto
{
    /// <summary>
    /// 促销（优惠券）ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 持券记录ID：由兑换码领取而来的券有值；公开促销为空
    /// </summary>
    public Guid? UserCouponId { get; set; }

    /// <summary>
    /// 是否为用户已领取持有的券（false = 公开促销，任何人输码可用）
    /// </summary>
    public bool IsHeld { get; set; }

    /// <summary>
    /// 优惠券代码
    /// </summary>
    public string CouponCode { get; set; } = string.Empty;

    /// <summary>
    /// 促销名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 折扣值
    /// </summary>
    public decimal DiscountValue { get; set; }

    /// <summary>
    /// 折扣类型
    /// </summary>
    public DiscountType DiscountType { get; set; }

    /// <summary>
    /// 最大折扣金额
    /// </summary>
    public decimal? MaxDiscountAmount { get; set; }

    /// <summary>
    /// 剩余使用次数
    /// </summary>
    public int RemainingUsageCount { get; set; }

    /// <summary>
    /// 到期时间
    /// </summary>
    public DateTime? ExpireTime { get; set; }

    /// <summary>
    /// 是否可叠加
    /// </summary>
    public bool Stackable { get; set; }
}

/// <summary>
/// 兑换码 DTO
/// </summary>
public class RedeemCodeDto
{
    /// <summary>
    /// 兑换码
    /// </summary>
    [Required]
    public string Code { get; set; } = string.Empty;
}

/// <summary>
/// 发券 DTO（管理员定向发放）
/// </summary>
public class GrantCouponDto
{
    /// <summary>
    /// 接收用户ID
    /// </summary>
    [Required]
    public Guid UserId { get; set; }
}

/// <summary>
/// 创建兑换码 DTO
/// </summary>
public class CreateRedemptionCodeDto
{
    /// <summary>
    /// 促销ID
    /// </summary>
    [Required]
    public Guid PromotionId { get; set; }

    /// <summary>
    /// 数量
    /// </summary>
    [Range(1, 1000, ErrorMessage = "Quantity must be between 1 and 1000.")]
    public int Quantity { get; set; }
}
