namespace Tnzi.Payment.Promotions.Entities;

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

    /// <summary>
    /// 这条核销在「同一用户 + 同一业务单号」上占的槽位：写入那一刻该单上已有核销的最大槽位 + 1（空单为 0）。
    /// </summary>
    /// <remarks>
    /// 它是这张单上券集合的乐观并发版本号，配合 <c>(UserId, BusinessOrderNo, OrderSlot)</c> 唯一索引使用。
    /// 「不可叠加」的判定是先读该单已有的券再决定放不放行，而两笔并发核销（不同的券）互相看不见对方未提交的那一行，
    /// 促销行锁也只串行化同一张券 —— 没有这一列，两张不可叠加的券可以同时落在一张单上。
    /// 两笔核销读到的是同一个券集合，就算出同一个槽位，后提交的那笔撞唯一索引被拒。
    /// 存量行为 null，不参与该索引（索引按非空过滤）。
    /// </remarks>
    public int? OrderSlot { get; set; }
}
