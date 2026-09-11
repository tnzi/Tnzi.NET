namespace Tnzi.Payment.Entities;

/// <summary>
/// 支付交易实体
/// </summary>
public class Payment : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 交易流水号（内部生成）
    /// </summary>
    public string TradeNo { get; set; } = string.Empty;

    /// <summary>
    /// 外部交易流水号（支付渠道返回）
    /// </summary>
    public string? ExternalTradeNo { get; set; }

    /// <summary>
    /// 业务订单号
    /// </summary>
    public string BusinessOrderNo { get; set; } = string.Empty;

    /// <summary>
    /// 业务类型
    /// </summary>
    public BusinessType BusinessType { get; set; }

    /// <summary>
    /// 原始金额
    /// </summary>
    public decimal OriginalAmount { get; set; }

    /// <summary>
    /// 已付金额
    /// </summary>
    public decimal PaidAmount { get; set; }

    /// <summary>
    /// 折扣金额
    /// </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// 税额（由 IPaymentTaxCalculator 计算；价内税时该额度已含在 <see cref="PayableAmount"/> 中）
    /// </summary>
    public decimal TaxAmount { get; set; }

    /// <summary>
    /// 应付金额：向渠道实际发起收款的金额，也是回调到账金额的校验基准。
    /// 价外税 = OriginalAmount - DiscountAmount + TaxAmount；价内税 = OriginalAmount - DiscountAmount。
    /// </summary>
    public decimal PayableAmount { get; set; }

    /// <summary>
    /// 币种（默认USD）
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// 支付状态
    /// </summary>
    public PaymentStatus Status { get; set; }

    /// <summary>
    /// 支付渠道代码
    /// </summary>
    public string ChannelCode { get; set; } = string.Empty;

    /// <summary>
    /// 支付方式
    /// </summary>
    public PaymentMethod PaymentMethod { get; set; }

    /// <summary>
    /// 支付描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 付款用户ID。
    /// 与审计字段 CreatorId 分开：后台 off-session 扣款没有当前用户，CreatorId 为空，
    /// 但账单归属仍必须可追溯（开票、对账、我的订单都依赖它）。
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 客户名称快照（开票与通知使用，避免跨模块回查 Identity）
    /// </summary>
    public string? CustomerName { get; set; }

    /// <summary>
    /// 客户邮箱快照（开票与通知使用，避免跨模块回查 Identity）
    /// </summary>
    public string? CustomerEmail { get; set; }

    /// <summary>
    /// 过期时间
    /// </summary>
    public DateTime? ExpireTime { get; set; }

    /// <summary>
    /// 支付完成时间
    /// </summary>
    public DateTime? PaidTime { get; set; }

    /// <summary>
    /// 渠道响应数据
    /// </summary>
    public string? ChannelResponse { get; set; }

    /// <summary>
    /// 扩展数据（JSON格式）
    /// </summary>
    public string? ExtraData { get; set; }

    /// <summary>
    /// 本次收款用掉的那张促销（优惠券）的 Id。<b>不是外键</b>。
    /// </summary>
    /// <remarks>
    /// 促销表随折扣域搬去了可选子模块 <c>Tnzi.Payment.Promotions</c>，而这一列<b>刻意保留</b>：
    /// 它从来就没有被配置成外键（两侧的 Configuration 里都没提过它），删掉它是一次
    /// <c>DropColumn</c>，会给每一个既有部署换来一条迁移，而拆程序集本身不该动 schema。
    /// 不加载那个包时它恒为 null —— 那台宿主上不存在任何优惠券，没有值可写。
    /// 加载了则由 <c>PaymentService</c> 在建单时写入，用途是一个快速判据：
    /// 支付失败/过期要还券时，先看这一列，为 null 就连问都不用问。
    /// </remarks>
    public Guid? CouponId { get; set; }

    /// <summary>
    /// 发票 ID。<b>不是外键</b>，也从来没有代码写过它。
    /// </summary>
    /// <remarks>
    /// 「支付 ↔ 发票」的外键一直在发票那一侧（<c>Invoice.PaymentId</c> + 一个唯一索引），
    /// 这一列只是个从未被赋值的标量。发票域搬去可选子模块 <c>Tnzi.Payment.Billing</c> 之后，
    /// 它<b>刻意保留</b>：删掉它是一次 <c>DropColumn</c>，会给每一个既有部署换来一条迁移，
    /// 而拆程序集本身不该动 schema。要不要清掉这列是一个独立的、可能有数据损失的决定，
    /// 不该搭本次拆分的便车。导航属性则必须删 —— 那是一条父 → 子的编译期依赖。
    /// </remarks>
    public Guid? InvoiceId { get; set; }

    /// <summary>
    /// 已被退款单占用的额度：待审批 / 审批通过 / 退款中 / 已成功的退款金额之和。
    /// 退款被拒绝、取消或失败后释放。
    /// </summary>
    /// <remarks>
    /// 存在的理由只有一个：**并发超退**。超退守卫此前是「先对退款表求和、再插入新退款」，
    /// 两步之间没有任何互斥，两笔并发的 60 元退款各自读到 sum=0 都判定通过，
    /// 于是一笔 100 元的支付被退了 120 —— 线下渠道当场记成功，那是真实资损。
    /// 求和读不到别人未提交的行，所以无论怎么调整读的位置都堵不上；
    /// 唯一能原子判定的形态是在**一行**上做条件自增
    /// （<c>WHERE ReservedRefundAmount + 金额 &lt;= PaidAmount</c>），与优惠券总量核销同形。
    /// <para>
    /// ★ 释放一律**按退款行重算**而不是做减法：同一笔退款可能被重复判为失败（渠道重投、
    /// 对账扫描与执行路径撞上），做减法会把额度还两次，从此这笔支付可以退出超过它收到的钱。
    /// </para>
    /// <para>
    /// ★ 既有部署迁移后本列为 0，而退款表里可能已有历史行。求和那一道校验**刻意保留**
    /// 正是为此：它在非并发路径上仍然正确，于是「没有回填」的最坏后果只是这些支付上的
    /// 并发防线退回到迁移前的水平，而不是凭空多出一整份可退额度。回填语句见模块文档。
    /// </para>
    /// </remarks>
    public decimal ReservedRefundAmount { get; set; }

    /// <summary>
    /// 退款记录集合
    /// </summary>
    public virtual ICollection<Refund> Refunds { get; set; } = new List<Refund>();
}
