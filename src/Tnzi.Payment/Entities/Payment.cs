namespace Tnzi.Payment.Entities;

/// <summary>
/// 支付交易实体
/// </summary>
public class Payment : FullAuditedEntity<Guid>
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
    /// 优惠券ID
    /// </summary>
    public Guid? CouponId { get; set; }

    /// <summary>
    /// 发票ID
    /// </summary>
    public Guid? InvoiceId { get; set; }

    /// <summary>
    /// 发票实体
    /// </summary>
    public virtual Invoice? Invoice { get; set; }

    /// <summary>
    /// 退款记录集合
    /// </summary>
    public virtual ICollection<Refund> Refunds { get; set; } = new List<Refund>();
}
