namespace Tnzi.Payment.Entities;

/// <summary>
/// 订阅实体
/// </summary>
public class Subscription : MultiTenantAuditedEntity<Guid>
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
    /// 客户名称快照（后台续费/催款通知与开票使用，避免跨模块回查 Identity）
    /// </summary>
    public string? CustomerName { get; set; }

    /// <summary>
    /// 客户邮箱快照（后台续费/催款通知与开票使用，避免跨模块回查 Identity）
    /// </summary>
    public string? CustomerEmail { get; set; }

    /// <summary>
    /// 产品代码（订阅归属的产品；null 表示单产品应用）。
    /// 判重维度：同一用户在同一产品下至多一条有效订阅，不同产品可并存。
    /// </summary>
    public string? ProductCode { get; set; }

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
    /// 渠道侧客户标识（用于 off-session 自动扣款，如 Stripe Customer ID）。
    /// 由绑卡链路（<see cref="Services.IPaymentMethodService"/>）写入的快照，
    /// 后台计费直读，避免每次扣款都 join 支付方式表。
    /// </summary>
    public string? ProviderCustomerId { get; set; }

    /// <summary>
    /// 渠道侧已保存的支付方式标识（用于 off-session 自动扣款，如 Stripe PaymentMethod ID）。
    /// 同为绑卡链路写入的快照，权威记录见 <see cref="StoredPaymentMethodId"/>。
    /// </summary>
    public string? PaymentMethodToken { get; set; }

    /// <summary>
    /// 绑定的已保存支付方式ID（用户级，见 <see cref="StoredPaymentMethod"/>）
    /// </summary>
    public Guid? StoredPaymentMethodId { get; set; }

    /// <summary>
    /// 已绑定支付方式的卡组织/类型（展示用快照，如 visa）
    /// </summary>
    public string? PaymentMethodBrand { get; set; }

    /// <summary>
    /// 已绑定支付方式的尾号（展示用快照）
    /// </summary>
    public string? PaymentMethodLast4 { get; set; }

    /// <summary>
    /// 续费/转正扣款连续失败次数（用于宽限期重试与降级 PastDue）
    /// </summary>
    public int RenewalRetryCount { get; set; }

    /// <summary>
    /// 进入逾期欠费（PastDue）的时间，用于宽限期到期判定
    /// </summary>
    public DateTime? PastDueSince { get; set; }

    /// <summary>
    /// 后台计费抢占锁到期时间（多实例下原子 claim，避免重复扣款）
    /// </summary>
    public DateTime? BillingLockedUntil { get; set; }

    /// <summary>
    /// 最近一次已应用到状态机的计费支付流水号（幂等：重复投递的同一支付不再重复推进周期）
    /// </summary>
    public string? LastBillingTradeNo { get; set; }

    /// <summary>
    /// 暂停开始时间。恢复时用它把剩余周期原样还给用户。
    /// </summary>
    /// <remarks>
    /// 没有它就只能在恢复时重算一个完整周期，等于"暂停一天换一个免费周期"——
    /// 在临近扣款日暂停再恢复即可反复白嫖。
    /// </remarks>
    public DateTime? PausedAt { get; set; }

    /// <summary>
    /// 暂停恢复时间（Paused 状态下由后台扫描到期自动恢复；null = 手动恢复）
    /// </summary>
    public DateTime? PausedUntil { get; set; }

    /// <summary>
    /// 已针对哪个计费周期发送过续费提醒（存该周期的 NextBillingTime，避免同一周期重复提醒）
    /// </summary>
    public DateTime? RenewalReminderSentFor { get; set; }

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
