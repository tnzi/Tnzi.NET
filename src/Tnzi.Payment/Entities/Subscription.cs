namespace Tnzi.Payment.Entities;

/// <summary>
/// 订阅实体
/// </summary>
public class Subscription : FullAuditedEntity<Guid>
{
    /// <summary>
    /// 订阅流水号
    /// </summary>
    public string SubscriptionNo { get; set; } = string.Empty;

    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 订阅计划ID
    /// </summary>
    public Guid PlanId { get; set; }

    /// <summary>
    /// 订阅计划实体
    /// </summary>
    public virtual SubscriptionPlan? Plan { get; set; }

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
    /// 试用转正时间
    /// </summary>
    public DateTime? TrialConvertedTime { get; set; }

    /// <summary>
    /// 折扣金额
    /// </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// 原价
    /// </summary>
    public decimal OriginalPrice { get; set; }

    /// <summary>
    /// 已付金额
    /// </summary>
    public decimal PaidAmount { get; set; }

    /// <summary>
    /// 币种
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// 是否自动续费
    /// </summary>
    public bool AutoRenew { get; set; } = true;

    /// <summary>
    /// 支付渠道代码
    /// </summary>
    public string ChannelCode { get; set; } = string.Empty;

    /// <summary>
    /// 取消原因
    /// </summary>
    public string? CancelReason { get; set; }

    /// <summary>
    /// 取消时间
    /// </summary>
    public DateTime? CancelTime { get; set; }

    /// <summary>
    /// 优惠券ID
    /// </summary>
    public Guid? CouponId { get; set; }

    /// <summary>
    /// 关联支付集合
    /// </summary>
    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    /// <summary>
    /// 生成订阅流水号
    /// </summary>
    public static string GenerateSubscriptionNo()
    {
        return $"SUB{IdHelper.NextId()}";
    }
}
