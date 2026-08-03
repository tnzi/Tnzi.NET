namespace Tnzi.Payment.Entities;

/// <summary>
/// 用户持有的优惠券（“我的券包”）。
/// 兑换码兑换的产物：兑换只是把促销发放给某个用户，真正核销发生在下单时（<see cref="CouponUsage"/>）。
/// 没有这一层，兑换就只是给兑换码计数器加一，用户什么也没拿到。
/// </summary>
public class UserCoupon : AuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 持有用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 促销（优惠券）ID
    /// </summary>
    public Guid PromotionId { get; set; }

    /// <summary>
    /// 促销实体
    /// </summary>
    public virtual Promotion? Promotion { get; set; }

    /// <summary>
    /// 来源兑换码ID（管理员直接发放时为空）
    /// </summary>
    public Guid? RedemptionCodeId { get; set; }

    /// <summary>
    /// 来源兑换码（冗余保存，兑换码删除后仍可追溯来源）
    /// </summary>
    public string? RedemptionCode { get; set; }

    /// <summary>
    /// 持券状态
    /// </summary>
    public UserCouponStatus Status { get; set; } = UserCouponStatus.Available;

    /// <summary>
    /// 领取时间
    /// </summary>
    public DateTime AcquiredTime { get; set; }

    /// <summary>
    /// 过期时间（取促销结束时间与兑换码失效时间的较早者；null = 不过期）
    /// </summary>
    public DateTime? ExpireTime { get; set; }

    /// <summary>
    /// 使用时间
    /// </summary>
    public DateTime? UsedTime { get; set; }

    /// <summary>
    /// 核销记录ID
    /// </summary>
    public Guid? CouponUsageId { get; set; }

    /// <summary>
    /// 是否处于可用状态（未使用、未作废且未过期）
    /// </summary>
    public bool IsUsable(DateTime utcNow)
        => Status == UserCouponStatus.Available && (ExpireTime == null || ExpireTime > utcNow);
}
