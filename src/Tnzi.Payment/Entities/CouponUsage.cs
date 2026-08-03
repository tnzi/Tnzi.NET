namespace Tnzi.Payment.Entities;

/// <summary>
/// 优惠券使用记录实体
/// </summary>
public class CouponUsage : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

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

    /// <summary>
    /// 业务订单号。核销幂等键的一部分（同一促销 + 同一用户 + 同一业务单号只允许核销一次），
    /// 支付/订阅的业务单号本来就是字符串，用它比 <see cref="OrderId"/> 更贴合真实调用方。
    /// </summary>
    public string? BusinessOrderNo { get; set; }

    /// <summary>
    /// 消耗掉的用户持券ID（通过兑换码领取的券在核销时被置为已使用）
    /// </summary>
    public Guid? UserCouponId { get; set; }
}
