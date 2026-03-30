namespace Tnzi.Payment.Entities;

/// <summary>
/// 订阅计划变更记录
/// </summary>
public class SubscriptionChange : CreationAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>
    /// 订阅ID
    /// </summary>
    public Guid SubscriptionId { get; set; }

    /// <summary>
    /// 原计划ID
    /// </summary>
    public Guid FromPlanId { get; set; }

    /// <summary>
    /// 新计划ID
    /// </summary>
    public Guid ToPlanId { get; set; }

    /// <summary>
    /// 变更类型
    /// </summary>
    public SubscriptionChangeType ChangeType { get; set; }

    /// <summary>
    /// 按比例计算的金额（正数=需补差价，负数=返还额度）
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
    /// 租户ID
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 关联订阅
    /// </summary>
    public virtual Subscription? Subscription { get; set; }

    /// <summary>
    /// 原计划
    /// </summary>
    public virtual SubscriptionPlan? FromPlan { get; set; }

    /// <summary>
    /// 新计划
    /// </summary>
    public virtual SubscriptionPlan? ToPlan { get; set; }
}
