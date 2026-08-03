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
    /// 验证优惠券并试算折扣。
    /// </summary>
    /// <remarks>
    /// 校验覆盖：启用状态、生效时间、总量/每用户次数、最低订单金额、适用产品类型与范围、
    /// 首单限定、以及非公开券是否已被该用户领取。这些条件此前存在于数据模型却从未参与判定。
    /// </remarks>
    Task<Result<CouponValidationResultDto>> ValidateCouponAsync(CouponApplyContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// 计算折扣（校验通过后按促销规则算出折扣金额）
    /// </summary>
    Task<Result<DiscountCalculationResultDto>> CalculateDiscountAsync(CouponApplyContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// 同步到Stripe
    /// </summary>
    Task<Result> SyncToStripeAsync(Guid promotionId, CancellationToken cancellationToken = default);
}
