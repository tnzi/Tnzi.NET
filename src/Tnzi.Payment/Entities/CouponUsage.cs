namespace Tnzi.Payment.Entities;

/// <summary>
/// 优惠券使用记录实体
/// </summary>
public class CouponUsage : CreationAuditedEntity<Guid>
{
    /// <summary>
    /// 优惠券ID
    /// </summary>
    public Guid CouponId { get; set; }

    /// <summary>
    /// 优惠券实体
    /// </summary>
    public virtual Promotion? Coupon { get; set; }

    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 关联支付ID
    /// </summary>
    public Guid? PaymentId { get; set; }

    /// <summary>
    /// 关联订阅ID
    /// </summary>
    public Guid? SubscriptionId { get; set; }

    /// <summary>
    /// 折扣金额
    /// </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// 业务订单ID
    /// </summary>
    public Guid? OrderId { get; set; }
}
