namespace Tnzi.Payment.Subscriptions.Metadata;

/// <summary>
/// 订阅状态
/// </summary>
/// <remarks>
/// 数值与拆分前<b>一字未动</b>：它是 <c>Payment_Subscription.Status</c> 这一列的存储值，
/// 也是 <c>@tnzi/core</c> 那份镜像枚举的线上格式。搬的是程序集，不是取值。
/// </remarks>
public enum SubscriptionStatus
{
    /// <summary>
    /// 待激活
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 试用中
    /// </summary>
    Trial = 1,

    /// <summary>
    /// 活动中
    /// </summary>
    Active = 2,

    /// <summary>
    /// 待续费
    /// </summary>
    PendingRenewal = 3,

    /// <summary>
    /// 已暂停
    /// </summary>
    Paused = 4,

    /// <summary>
    /// 已取消
    /// </summary>
    Cancelled = 5,

    /// <summary>
    /// 已过期
    /// </summary>
    Expired = 6,

    /// <summary>
    /// 逾期欠费（续费/试用转正扣款失败，宽限期内等待重试，超期则过期或暂停服务）
    /// </summary>
    PastDue = 7,

    /// <summary>
    /// 已暂停服务：宽限期满仍未收到款，且 <c>Payment:Subscription:GracePeriodEndAction = Suspend</c>。
    /// </summary>
    /// <remarks>
    /// ★ <b>不是终态</b>，与 <see cref="Expired"/> 刻意分开：过期是「这条订阅结束了，要用就重新开通」，
    /// 而暂停服务是「账户欠着这一期，付清就原样回来」。它照常占着「同一账户同一产品只有一条有效订阅」的名额
    /// （唯一索引只排除 Cancelled / Expired），所以补缴恢复的是同一条订阅，不会与一条新开通的撞上。
    /// 不再自动扣款（续费扫描只取 Active / PastDue），由管理者在订阅页当场补缴（<c>POST subscriptions/{id}/pay-now</c>）
    /// 或换卡（换卡后当场重试一次）恢复；恢复时新的一期从付款那一刻起算，暂停期间不追收。
    /// 与 <see cref="Paused"/> 也不是一回事：那是客户自己按的暂停，到期自动恢复。
    /// </remarks>
    Suspended = 8
}

/// <summary>
/// 宽限期满仍未收到款时怎么处理（<c>Payment:Subscription:GracePeriodEndAction</c>）。
/// </summary>
public enum GracePeriodEndAction
{
    /// <summary>订阅过期（终态），要用就重新开通。默认，与引入这一项之前相同。</summary>
    Expire = 0,

    /// <summary>暂停服务（<see cref="SubscriptionStatus.Suspended"/>），补缴或换卡后原样恢复。</summary>
    Suspend = 1
}

/// <summary>
/// 一条订阅让它所属的账户处在什么境况（门禁与提示据此判断）。
/// </summary>
public enum BillingStanding
{
    /// <summary>没有在生效的订阅（未开通、已取消、已过期、客户自己暂停中）。</summary>
    None = 0,

    /// <summary>正常：试用中、已付到期内、或到期后取消但本期未走完。</summary>
    Good = 1,

    /// <summary>扣款失败，宽限期内：服务照常，需要提醒管理者更新支付方式。</summary>
    Warning = 2,

    /// <summary>宽限期满仍未付清，服务已暂停。</summary>
    Suspended = 3
}

/// <summary>
/// 订阅计费支付用途（用于将支付完成事件路由回订阅状态机）
/// </summary>
public enum SubscriptionBillingPurpose
{
    /// <summary>
    /// 首次开通付款
    /// </summary>
    Initial = 0,

    /// <summary>
    /// 周期续费
    /// </summary>
    Renewal = 1,

    /// <summary>
    /// 试用转正付款
    /// </summary>
    TrialConversion = 2,

    /// <summary>
    /// 升级补差价
    /// </summary>
    Proration = 3,

    /// <summary>
    /// 当场补缴（管理者在订阅页上为一条逾期欠费或已暂停服务的订阅付款，有人在场，可做 3-D Secure）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="Renewal"/> 分开的理由在失败那一侧：一张补缴单被放弃或过期，不是一次扣款失败：
    /// 不计重试、不发催款信、不动订阅行。成功时：逾期欠费按续费推进一期；已暂停服务从付款那一刻起算新的一期。
    /// </remarks>
    Recovery = 4
}

/// <summary>
/// 计费周期类型
/// </summary>
/// <remarks>
/// 随订阅域一起搬过来：拆分后父模块<b>没有任何一处</b>再用它 —— 它只出现在计划、订阅两张表的
/// <c>CycleType</c> 列，以及「折算月度等值金额」这段 MRR 计算里，后者已随统计供给方搬到本模块。
/// 留在父模块就等于让不做续费的宿主继续携带一个只有续费才有意义的枚举。
/// 注意与 <c>BusinessType.Subscription</c> / <c>ProductType.Subscription</c> 区分：
/// 那两个是**支付域与促销域自己的**枚举成员，只是恰好叫这个名字，一律留在父模块。
/// </remarks>
public enum BillingCycleType
{
    /// <summary>
    /// 按天
    /// </summary>
    Day = 1,

    /// <summary>
    /// 按周
    /// </summary>
    Week = 2,

    /// <summary>
    /// 按月
    /// </summary>
    Month = 3,

    /// <summary>
    /// 按年
    /// </summary>
    Year = 4,

    /// <summary>
    /// 一次性
    /// </summary>
    OneTime = 5
}

/// <summary>
/// 订阅变更类型
/// </summary>
public enum SubscriptionChangeType
{
    /// <summary>
    /// 升级
    /// </summary>
    Upgrade = 1,

    /// <summary>
    /// 降级
    /// </summary>
    Downgrade = 2,

    /// <summary>
    /// 平级变更
    /// </summary>
    CrossGrade = 3
}

/// <summary>
/// 订阅变更状态
/// </summary>
public enum SubscriptionChangeStatus
{
    /// <summary>
    /// 待生效
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 已生效
    /// </summary>
    Applied = 1,

    /// <summary>
    /// 已取消
    /// </summary>
    Cancelled = 2,

    /// <summary>
    /// 等补差款：立即生效的升级已发起收款、尚未确认到账。
    /// 与 <see cref="Pending"/> 刻意分开：到期结算扫描只取 Pending，
    /// 等钱的变更不会因为 EffectiveDate 已到就被当成「到期变更」免费应用。
    /// 收款确认 → Applied；支付失败 / 过期 / 订阅终止 → Cancelled。
    /// </summary>
    AwaitingPayment = 3
}
