namespace Tnzi.Payment.Promotions.Entities;

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
    /// 币种（固定金额折扣时生效；百分比折扣与币种无关）
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// 是否公开：公开促销任何用户输入促销码即可使用；
    /// 非公开促销必须先通过兑换码领取（持有 <see cref="UserCoupon"/>）才能使用。
    /// </summary>
    public bool IsPublic { get; set; } = true;

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
    /// 适用产品类型（默认不限）
    /// </summary>
    public ProductType ProductType { get; set; } = ProductType.All;

    /// <summary>
    /// 应用范围
    /// </summary>
    public ApplyScope ApplyScope { get; set; }

    /// <summary>
    /// 范围ID列表的 JSON 存储（数据库列名仍为 ScopeIds）。
    /// </summary>
    /// <remarks>
    /// 刻意与 DTO 上的 <c>ScopeIds</c>（Guid 列表）取不同的属性名：
    /// 同名会让对象映射按名字把 JSON 字符串硬转成列表，在运行期抛 InvalidCastException，
    /// 且这种崩溃只在"映射配置恰好没注册"时才出现，极难排查。名字不同则天然不会误配。
    /// </remarks>
    public string? ScopeIdsJson { get; set; }

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
    /// 每用户实际生效的使用次数上限：本促销设了就用它，没设就用配置的默认值；不大于 0 视为不限（返回 null）。
    /// </summary>
    /// <remarks>
    /// 核销前的校验、核销事务内的复核与券包的「剩余次数」共用这一个判定。此前券包只看本列：
    /// 没单独设上限的促销被当成不限（用满默认 5 次后券包里仍显示可用、结账被拒），
    /// 设为 0 的又被当成「已用完」藏起来（而守卫把 0 当不限、照常放行）。
    /// </remarks>
    /// <param name="perUserUsageLimit">促销自己的 <see cref="PerUserUsageLimit"/>（实体或 DTO 上的那一列）。</param>
    /// <param name="defaultLimit">配置的默认上限（<c>Payment:Promotion:MaxCouponUsagePerUser</c>）。</param>
    public static int? ResolvePerUserLimit(int? perUserUsageLimit, int defaultLimit)
    {
        var limit = perUserUsageLimit ?? defaultLimit;
        return limit > 0 ? limit : null;
    }

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

    /// <summary>
    /// 用户持券集合
    /// </summary>
    public virtual ICollection<UserCoupon> UserCoupons { get; set; } = new List<UserCoupon>();
}
