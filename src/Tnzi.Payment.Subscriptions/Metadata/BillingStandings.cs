namespace Tnzi.Payment.Subscriptions.Metadata;

/// <summary>
/// 订阅状态 → 账户境况的唯一映射。事件、境况查询与（之后的）门禁都经它判，不各自列状态。
/// </summary>
public static class BillingStandings
{
    /// <summary>一条订阅的状态让账户处在什么境况。</summary>
    public static BillingStanding Of(SubscriptionStatus status) => status switch
    {
        SubscriptionStatus.Trial or SubscriptionStatus.Active or SubscriptionStatus.PendingRenewal => BillingStanding.Good,
        SubscriptionStatus.PastDue => BillingStanding.Warning,
        SubscriptionStatus.Suspended => BillingStanding.Suspended,
        // Pending（首期没付）、Paused（客户自己暂停）、Cancelled、Expired：没有在生效的服务
        _ => BillingStanding.None
    };

    /// <summary>
    /// 同一账户（同一产品）下几条订阅合起来的境况：有一条正常就是正常；否则取最需要处理的那一种（暂停服务 &gt; 欠费 &gt; 无）。
    /// </summary>
    /// <remarks>
    /// 唯一索引保证同一账户同一产品至多一条有效订阅，所以按产品问时最多一条；不指定产品时几个产品各一条，
    /// 有一条正常就不该把整个账户当成停服。
    /// </remarks>
    public static BillingStanding Combine(IEnumerable<BillingStanding> standings)
    {
        var all = standings.ToList();
        if (all.Contains(BillingStanding.Good))
            return BillingStanding.Good;
        if (all.Contains(BillingStanding.Suspended))
            return BillingStanding.Suspended;
        if (all.Contains(BillingStanding.Warning))
            return BillingStanding.Warning;
        return BillingStanding.None;
    }
}
