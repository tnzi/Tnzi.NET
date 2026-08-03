namespace Tnzi.Payment.Services;

/// <summary>
/// 优惠券服务接口
/// </summary>
public interface ICouponService
{
    /// <summary>
    /// 试算优惠券折扣（只校验与计算，不产生核销记录）。
    /// 支付/订阅在向渠道下单前用它确定实收金额。
    /// </summary>
    Task<Result<CouponPreviewDto>> PreviewAsync(CouponApplyContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// 核销优惠券：写使用记录、原子递增总用量、消耗用户持券。
    /// </summary>
    /// <remarks>
    /// 幂等键为（促销 + 用户 + 业务单号），重复调用返回既有记录而不是报错，
    /// 使调用方在重试链路上无需自行去重。
    /// </remarks>
    Task<Result<CouponUsageDto>> ApplyCouponAsync(CouponApplyContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// 释放已核销的优惠券：回滚使用记录、递减总用量、把持券恢复为可用。
    /// 用于"券已核销但渠道下单失败"的补偿，否则用户的券会凭空消失。
    /// </summary>
    Task<Result> ReleaseCouponAsync(Guid couponUsageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户可用优惠券列表（已领取的持券 + 公开促销）
    /// </summary>
    Task<Result<List<UserCouponDto>>> GetUserAvailableCouponsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户已使用的优惠券列表
    /// </summary>
    Task<Result<List<CouponUsageDto>>> GetUserUsedCouponsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 兑换兑换码：把促销发放给用户（产生持券记录）
    /// </summary>
    Task<Result<UserCouponDto>> RedeemAsync(string code, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查用户是否可以使用首次订阅优惠
    /// </summary>
    Task<Result<bool>> CanUseFirstSubscriptionDiscountAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建兑换码
    /// </summary>
    Task<Result<string>> CreateRedemptionCodeAsync(Guid promotionId, int quantity, CancellationToken cancellationToken = default);

    /// <summary>
    /// 直接向用户发放优惠券（管理员补偿/运营发券，无需兑换码）
    /// </summary>
    Task<Result<UserCouponDto>> GrantAsync(Guid promotionId, Guid userId, CancellationToken cancellationToken = default);
}
