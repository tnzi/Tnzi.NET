namespace Tnzi.Payment.Promotions.Services;

/// <summary>
/// 用户券包：我手上有哪些券、用过哪些、拿一个兑换码换一张。
/// </summary>
/// <remarks>
/// <para>
/// 这四个方法拆分前挂在父模块的 <c>ICouponService</c> 上，各自<b>只有一个调用方</b>，
/// 而且都是随本模块整体搬走的 <c>DefaultPromotionController</c>（用户面）。
/// 留在父模块会顺带把 <see cref="UserCouponDto"/> 钉在那里 —— 一个只有券包端点在读的 DTO。
/// </para>
/// <para>
/// <b>为什么与 <see cref="ICouponIssuanceService"/> 分开</b>：这里全是「用我自己的身份读/换」，
/// 那边是「凭管理员权限凭空发出去」。合成一个接口，任何为了显示券包而注入它的地方
/// 都顺手拿到了铸码与发券的能力。分开之后接口边界与两个控制器的授权边界重合。
/// </para>
/// </remarks>
public interface ICouponWalletService
{
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
}
