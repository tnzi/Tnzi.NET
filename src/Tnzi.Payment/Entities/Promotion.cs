namespace Tnzi.Payment.Entities;

/// <summary>
/// 促销活动实体
/// </summary>
public class Promotion : AuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 促销代码
    /// </summary>
    public string PromotionCode { get; set; } = string.Empty;

    /// <summary>
    /// 促销名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 促销描述
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
    /// 范围ID列表（JSON格式）
    /// </summary>
    public string? ScopeIds { get; set; }

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
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 是否仅首次订阅可用
    /// </summary>
    public bool FirstSubscriptionOnly { get; set; }

    /// <summary>
    /// Stripe Coupon ID
    /// </summary>
    public string? StripeCouponId { get; set; }

    /// <summary>
    /// 优惠券使用记录集合
    /// </summary>
    public virtual ICollection<CouponUsage> CouponUsages { get; set; } = new List<CouponUsage>();

    /// <summary>
    /// 兑换码集合
    /// </summary>
    public virtual ICollection<RedemptionCode> RedemptionCodes { get; set; } = new List<RedemptionCode>();
}
