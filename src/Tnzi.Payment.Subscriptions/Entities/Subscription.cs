namespace Tnzi.Payment.Subscriptions.Entities;

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
    /// 开通这条订阅的经办人。归属看 <see cref="BillingAccountType"/> / <see cref="BillingAccountId"/>。
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 这条订阅属于哪个计费账户：种类（见 <see cref="BillingAccountTypes"/>）。
    /// </summary>
    /// <remarks>
    /// 「同一账户同一产品只有一条有效订阅」、谁能取消 / 换卡 / 补缴、付款回流核对的付款方，都按账户算：
    /// 单客户部署里任何一位管理者开通的就是整个机构的订阅，另一位管理者也能替它付款。
    /// </remarks>
    public string BillingAccountType { get; set; } = string.Empty;

    /// <summary>
    /// 计费账户 Id（与 <see cref="BillingAccountType"/> 成对）。
    /// </summary>
    public string BillingAccountId { get; set; } = string.Empty;

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
    /// 由绑卡链路（<see cref="Tnzi.Payment.Services.IPaymentMethodService"/>）写入的快照，
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
    /// 进入暂停服务（<see cref="SubscriptionStatus.Suspended"/>）的时间；恢复后清空。
    /// </summary>
    public DateTime? SuspendedAt { get; set; }

    /// <summary>
    /// 仍可付款的那张补缴单的流水号（<c>POST subscriptions/{id}/pay-now</c> 建的）；付清 / 失败 / 过期后清空。
    /// </summary>
    /// <remarks>
    /// 有它才能在管理者再点一次「立即付款」时把同一张单的付款凭据还给他，而不是再建一张：
    /// 两张同时可付的补缴单，付掉两张就是同一期收两次（与 <see cref="SubscriptionChange.PaymentTradeNo"/> 同一个理由）。
    /// </remarks>
    public string? RecoveryPaymentTradeNo { get; set; }

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

    // ★ 这里曾有一条 `ICollection<Payment> Payments` 死导航，本次拆分把它删掉了。
    //   它没有反向导航、两侧的 EntityTypeConfiguration 都没提过它、全仓也没有一行往里加过东西。
    //   但 EF 的约定会据此在**支付表**上生成一个影子列 SubscriptionId、一条外键
    //   FK_Payment_Payment_Subscription_SubscriptionId 与一条索引 —— 于是父模块那张表的形状
    //   会取决于「有没有加载本包」。删掉是 DropForeignKey + DropIndex + DropColumn，
    //   无数据损失（那一列从来没有被写过），但**消费方需要生成并应用一条迁移**。
    //   要按订阅查支付，走 Payment.BusinessOrderNo == Subscription.SubscriptionNo
    //   （那是这条关联真正的、一直在用的键）。

    /// <summary>
    /// 生成订阅流水号
    /// </summary>
    public static string GenerateSubscriptionNo()
    {
        return $"SUB{IdHelper.NextId()}";
    }
}
