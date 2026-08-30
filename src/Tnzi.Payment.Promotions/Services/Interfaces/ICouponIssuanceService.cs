namespace Tnzi.Payment.Promotions.Services;

/// <summary>
/// 发券：铸一批兑换码，或者直接把一张券记到某个用户名下。
/// </summary>
/// <remarks>
/// 两个方法拆分前挂在父模块的 <c>ICouponService</c> 上，调用方都只有一个，
/// 就是随本模块整体搬走的 <c>DefaultPromotionAdminController</c>。
/// 与 <see cref="ICouponWalletService"/> 分开是刻意的：这两个方法凭空造出可以抵钱的东西，
/// 只应由带 <c>payment.promotion.create</c> 的管理端点调用，
/// 不该因为某处要显示一下券包就一并被注入进去。
/// </remarks>
public interface ICouponIssuanceService
{
    /// <summary>
    /// 创建兑换码
    /// </summary>
    Task<Result<string>> CreateRedemptionCodeAsync(Guid promotionId, int quantity, CancellationToken cancellationToken = default);

    /// <summary>
    /// 直接向用户发放优惠券（管理员补偿/运营发券，无需兑换码）
    /// </summary>
    Task<Result<UserCouponDto>> GrantAsync(Guid promotionId, Guid userId, CancellationToken cancellationToken = default);
}
