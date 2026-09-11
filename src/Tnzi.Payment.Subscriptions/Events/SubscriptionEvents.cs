namespace Tnzi.Payment.Subscriptions.Events;

/// <summary>
/// 订阅续费事件
/// </summary>
public class SubscriptionRenewedEvent : EventBase
{
    /// <summary>
    /// 订阅ID
    /// </summary>
    public Guid SubscriptionId { get; set; }

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
    /// 新结束时间
    /// </summary>
    public DateTime NewEndTime { get; set; }

    /// <summary>
    /// 续费金额
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 币种
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// 支付交易流水号
    /// </summary>
    public string? PaymentTradeNo { get; set; }

    /// <summary>
    /// 是否自动续费
    /// </summary>
    public bool AutoRenew { get; set; }
}

/// <summary>
/// 订阅创建事件
/// </summary>
public class SubscriptionCreatedEvent : EventBase
{
    /// <summary>
    /// 订阅ID
    /// </summary>
    public Guid SubscriptionId { get; set; }

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
    public string PlanName { get; set; } = string.Empty;

    /// <summary>
    /// 开始时间
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 结束时间
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// 是否试用
    /// </summary>
    public bool IsTrial { get; set; }

    /// <summary>
    /// 试用结束时间
    /// </summary>
    public DateTime? TrialEndTime { get; set; }
}

/// <summary>
/// 订阅取消事件
/// </summary>
public class SubscriptionCancelledEvent : EventBase
{
    /// <summary>
    /// 订阅ID
    /// </summary>
    public Guid SubscriptionId { get; set; }

    /// <summary>
    /// 订阅流水号
    /// </summary>
    public string SubscriptionNo { get; set; } = string.Empty;

    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 取消原因
    /// </summary>
    public string? CancelReason { get; set; }

    /// <summary>
    /// 是否立即取消
    /// </summary>
    public bool Immediate { get; set; }

    /// <summary>
    /// 到期时间
    /// </summary>
    public DateTime? ExpireTime { get; set; }
}

/// <summary>
/// 订阅过期事件
/// </summary>
public class SubscriptionExpiredEvent : EventBase
{
    /// <summary>
    /// 订阅ID
    /// </summary>
    public Guid SubscriptionId { get; set; }

    /// <summary>
    /// 订阅流水号
    /// </summary>
    public string SubscriptionNo { get; set; } = string.Empty;

    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 过期时间
    /// </summary>
    public DateTime ExpiredTime { get; set; }
}

/// <summary>
/// 订阅计划变更事件
/// </summary>
public class SubscriptionPlanChangedEvent : EventBase
{
    /// <summary>
    /// 订阅ID
    /// </summary>
    public Guid SubscriptionId { get; set; }

    /// <summary>
    /// 订阅流水号
    /// </summary>
    public string SubscriptionNo { get; set; } = string.Empty;

    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid UserId { get; set; }

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
    /// 按比例计算的金额
    /// </summary>
    public decimal ProratedAmount { get; set; }

    /// <summary>
    /// 生效日期
    /// </summary>
    public DateTime EffectiveDate { get; set; }

    /// <summary>
    /// 是否立即生效
    /// </summary>
    public bool Immediate { get; set; }
}

/// <summary>
/// 订阅计划变更**已生效**事件。
/// </summary>
/// <remarks>
/// 与 <see cref="SubscriptionPlanChangedEvent"/> 是两件事，不能互相替代：
/// 那一条在用户**提出**变更时发出，对延后生效的降级来说只说明「约好了周期末改」；
/// 这一条在计划真的换掉的那一刻发出。功能授权（能用哪些席位、哪些容量）挂在后者上，
/// 消费方只订前者就得自己拿 <c>EffectiveDate</c> 建一套定时器 ——
/// 而那套定时器与本模块的结算扫描随时可能对不上。
/// 立即生效的升级（补差付款确认后）与到期结算的降级都走这条事件，
/// 判据是同一个：一条 <c>SubscriptionChange</c> 变成 <c>Applied</c>。
/// </remarks>
public class SubscriptionPlanChangeAppliedEvent : EventBase
{
    /// <summary>
    /// 订阅ID
    /// </summary>
    public Guid SubscriptionId { get; set; }

    /// <summary>
    /// 订阅流水号
    /// </summary>
    public string SubscriptionNo { get; set; } = string.Empty;

    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 变更记录ID
    /// </summary>
    public Guid ChangeId { get; set; }

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
    /// 实际生效时间
    /// </summary>
    public DateTime AppliedTime { get; set; }
}

/// <summary>
/// 试用转正事件
/// </summary>
public class SubscriptionTrialConvertedEvent : EventBase
{
    /// <summary>
    /// 订阅ID
    /// </summary>
    public Guid SubscriptionId { get; set; }

    /// <summary>
    /// 订阅流水号
    /// </summary>
    public string SubscriptionNo { get; set; } = string.Empty;

    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 转正时间
    /// </summary>
    public DateTime ConvertedTime { get; set; }

    /// <summary>
    /// 支付交易流水号
    /// </summary>
    public string? PaymentTradeNo { get; set; }
}
