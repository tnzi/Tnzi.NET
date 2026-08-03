namespace Tnzi.Payment.Entities;

/// <summary>
/// 订阅计划实体
/// </summary>
public class SubscriptionPlan : AuditedEntity<Guid>
{
    /// <summary>
    /// 计划代码
    /// </summary>
    public string PlanCode { get; set; } = string.Empty;

    /// <summary>
    /// 产品代码（同一产品下的多个计划互为升降级；null 表示单产品应用）。
    /// 订阅判重与计划变更的边界都按它划分：跨产品不算“已有订阅”，也不允许互相变更。
    /// </summary>
    public string? ProductCode { get; set; }

    /// <summary>
    /// 计划名称
    /// </summary>
    public string PlanName { get; set; } = string.Empty;

    /// <summary>
    /// 计划描述
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
    /// 是否启用
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 排序号
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 是否允许试用
    /// </summary>
    public bool AllowTrial { get; set; } = true;

    /// <summary>
    /// 试用折扣
    /// </summary>
    public decimal? TrialDiscount { get; set; }

    /// <summary>
    /// 订阅集合
    /// </summary>
    public virtual ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
}
