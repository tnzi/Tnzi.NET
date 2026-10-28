namespace Tnzi.Payment.Subscriptions.Dtos;

/// <summary>
/// 当前用户所代表的计费账户此刻的境况（<c>GET subscriptions/standing</c>）。外壳启动时与收到 402 之后读它。
/// </summary>
/// <remarks>
/// 任何已登录用户都能读：不能看账单的人也得知道服务暂停了，才能显示「请联系管理员」。
/// 但需要处理的那条订阅的细节（计划、欠款、截止日）只给 <see cref="CanView"/> 的人。
/// </remarks>
public class BillingStandingDto
{
    /// <summary>账户境况。用户不代表任何账户时为 <see cref="BillingStanding.None"/>。</summary>
    public BillingStanding Standing { get; set; }

    /// <summary>能否查看这个账户的账单。</summary>
    public bool CanView { get; set; }

    /// <summary>能否为这个账户付款、换卡。</summary>
    public bool CanManage { get; set; }

    /// <summary>访问门禁是否开着（<c>Payment:Subscription:AccessGate:Enabled</c>）：关着时暂停服务不会挡住任何请求。</summary>
    public bool GateEnabled { get; set; }

    /// <summary>
    /// 门禁对这个用户不生效（平台超级管理员、持有绕过权限码、门禁关着、或他不代表任何账户）。
    /// 为 true 时前端不该显示暂停页面：后端不会拦他。
    /// </summary>
    public bool IsExempt { get; set; }

    /// <summary>需要处理的那条订阅（逾期或已暂停）；没有，或调用者不能查看时为空。下同。</summary>
    public Guid? SubscriptionId { get; set; }

    /// <summary>订阅号。</summary>
    public string? SubscriptionNo { get; set; }

    /// <summary>计划名。</summary>
    public string? PlanName { get; set; }

    /// <summary>订阅状态（<c>PastDue</c> / <c>Suspended</c>）。</summary>
    public SubscriptionStatus? Status { get; set; }

    /// <summary>从何时开始欠费。</summary>
    public DateTime? PastDueSince { get; set; }

    /// <summary>逾期订阅的宽限期截止（之后按 <c>GracePeriodEndAction</c> 暂停或过期）；已暂停的为空。</summary>
    public DateTime? GraceDeadline { get; set; }

    /// <summary>何时暂停服务。</summary>
    public DateTime? SuspendedAt { get; set; }

    /// <summary>补缴要付多少（税前，与 <c>pay-now</c> 建单同一套计价）。</summary>
    public decimal? AmountDue { get; set; }

    /// <summary>币种。</summary>
    public string? Currency { get; set; }
}
