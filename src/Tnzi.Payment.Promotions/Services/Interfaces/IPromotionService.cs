namespace Tnzi.Payment.Promotions.Services;

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
    /// 这个用户现在能不能用「仅限首次订阅」的券。
    /// </summary>
    /// <remarks>
    /// ★ <b>这是 <see cref="ValidateCouponAsync"/> 里那道首单守卫用的同一个判定</b>，预检端点
    /// （<c>GET /promotions/first-subscription-check</c>）也走它 —— 两边此前是两份判据
    /// （预检查「用没用过首单券」，核销查「有没有订阅记录」），老订户被收银台先承诺再拒绝，
    /// 用过一张首单券但从没订成的人则被界面藏掉入口。判据向续费域的
    /// <see cref="ISubscriptionHistoryProbe"/> 提问；探针缺席（没装续费包）= 没有人订阅过 = 对所有人成立。
    /// 服务间调用，返回裸 <see cref="bool"/>。
    /// </remarks>
    Task<bool> IsFirstSubscriptionEligibleAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 计算折扣（校验通过后按促销规则算出折扣金额）
    /// </summary>
    Task<Result<DiscountCalculationResultDto>> CalculateDiscountAsync(CouponApplyContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// 同步到Stripe
    /// </summary>
    /// <remarks>
    /// 走 <see cref="IPaymentChannelCouponSync"/>，实现在可选子模块 <c>Tnzi.Payment.Stripe</c>。
    /// 未加载时返回 501 并指名要加载的包；同步本身受 <c>Payment:Promotion:EnableStripeCouponSync</c> 控制。
    /// </remarks>
    Task<Result> SyncToStripeAsync(Guid promotionId, CancellationToken cancellationToken = default);
}
