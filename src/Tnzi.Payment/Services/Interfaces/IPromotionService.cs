namespace Tnzi.Payment.Services;

/// <summary>
/// 促销服务接口
/// </summary>
public interface IPromotionService
{
    /// <summary>
    /// 创建促销
    /// </summary>
    Task<Result<PromotionDto>> CreateAsync(CreatePromotionDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新促销
    /// </summary>
    Task<Result> UpdateAsync(Guid id, UpdatePromotionDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 停用促销
    /// </summary>
    Task<Result> DeactivateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取促销信息
    /// </summary>
    Task<Result<PromotionDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据代码获取促销
    /// </summary>
    Task<Result<PromotionDto>> GetByCodeAsync(string promotionCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取促销列表
    /// </summary>
    Task<Result<IPagedList<PromotionDto>>> GetListAsync(PromotionQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证优惠券
    /// </summary>
    Task<Result<CouponValidationResultDto>> ValidateCouponAsync(string couponCode, Guid? userId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 计算折扣
    /// </summary>
    Task<Result<DiscountCalculationResultDto>> CalculateDiscountAsync(string couponCode, decimal orderAmount, CancellationToken cancellationToken = default);

    /// <summary>
    /// 同步到Stripe
    /// </summary>
    Task<Result> SyncToStripeAsync(Guid promotionId, CancellationToken cancellationToken = default);
}
