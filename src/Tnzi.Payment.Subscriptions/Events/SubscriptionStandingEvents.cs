namespace Tnzi.Payment.Subscriptions.Events;

/// <summary>
/// 订阅境况事件的公共部分：哪条订阅、属于哪个账户、哪个产品。
/// </summary>
/// <remarks>
/// 这一组事件回答的是「这个账户现在能不能用」，给门禁、提示横幅、通知管理者用；
/// 「收了一笔钱 / 换了计划」照旧看 <see cref="SubscriptionRenewedEvent"/> 等既有事件。
/// 账户字段显式带上：发布方常是后台扫描或匿名回调，订阅者没有请求上下文可以回查。
/// </remarks>
public abstract class SubscriptionStandingEventBase : EventBase
{
    /// <summary>订阅ID</summary>
    public Guid SubscriptionId { get; set; }

    /// <summary>订阅流水号</summary>
    public string SubscriptionNo { get; set; } = string.Empty;

    /// <summary>所属计费账户：种类</summary>
    public string BillingAccountType { get; set; } = string.Empty;

    /// <summary>所属计费账户：Id</summary>
    public string BillingAccountId { get; set; } = string.Empty;

    /// <summary>产品代码（单产品应用为 null）</summary>
    public string? ProductCode { get; set; }

    /// <summary>发生时间</summary>
    public DateTime OccurredTime { get; set; }
}

/// <summary>
/// 订阅生效：首期付清，或开通即进入试用。
/// </summary>
public class SubscriptionActivatedEvent : SubscriptionStandingEventBase
{
    /// <summary>是否以试用开始</summary>
    public bool IsTrial { get; set; }
}

/// <summary>
/// 订阅进入逾期欠费：续费 / 转正扣款失败，宽限期开始。只在从别的状态<b>进入</b> PastDue 时发一次，宽限期内的重试失败不再重复发。
/// </summary>
public class SubscriptionPastDueEvent : SubscriptionStandingEventBase
{
    /// <summary>失败原因（渠道回报或错误码）</summary>
    public string? FailReason { get; set; }

    /// <summary>宽限期截止时间：过了它仍未付清就过期或暂停服务</summary>
    public DateTime GraceDeadline { get; set; }
}

/// <summary>
/// 订阅从逾期欠费或暂停服务恢复正常（扣款或补缴成功）。
/// </summary>
public class SubscriptionRecoveredEvent : SubscriptionStandingEventBase
{
    /// <summary>恢复之前的状态（PastDue 或 Suspended）</summary>
    public SubscriptionStatus PreviousStatus { get; set; }

    /// <summary>让它恢复的那笔支付流水号</summary>
    public string? PaymentTradeNo { get; set; }
}

/// <summary>
/// 宽限期满仍未付清，订阅暂停服务（<c>GracePeriodEndAction = Suspend</c>）。
/// </summary>
public class SubscriptionSuspendedEvent : SubscriptionStandingEventBase
{
    /// <summary>进入逾期欠费的时间</summary>
    public DateTime? PastDueSince { get; set; }
}

/// <summary>
/// 一条订阅让它的账户所处的境况变了（<see cref="BillingStanding"/>：None / Good / Warning / Suspended）。
/// </summary>
/// <remarks>
/// 上面几个事件的汇总形态，订阅一个事件就能维护「账户能不能用」的缓存：每一次境况变化恰好发一条，
/// 境况没变的状态变化（例如 Active ⇄ PendingRenewal）不发。
/// </remarks>
public class BillingAccountStandingChangedEvent : SubscriptionStandingEventBase
{
    /// <summary>变化前的境况</summary>
    public BillingStanding PreviousStanding { get; set; }

    /// <summary>变化后的境况</summary>
    public BillingStanding CurrentStanding { get; set; }

    /// <summary>变化后的订阅状态</summary>
    public SubscriptionStatus Status { get; set; }
}
