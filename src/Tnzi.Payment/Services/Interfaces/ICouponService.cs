namespace Tnzi.Payment.Services;

/// <summary>
/// 优惠券服务接口
/// </summary>
public interface ICouponService
{
    /// <summary>
    /// 应用优惠券
    /// </summary>
    Task<Result<CouponUsageDto>> ApplyCouponAsync(string couponCode, Guid userId, Guid orderId, decimal orderAmount, Guid? paymentId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户可用优惠券列表
    /// </summary>
    Task<Result<List<UserCouponDto>>> GetUserAvailableCouponsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户已使用的优惠券列表
    /// </summary>
    Task<Result<List<CouponUsageDto>>> GetUserUsedCouponsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 兑换兑换码
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
}
