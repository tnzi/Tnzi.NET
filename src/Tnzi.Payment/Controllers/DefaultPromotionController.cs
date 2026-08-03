namespace Tnzi.Payment.Controllers;

/// <summary>
/// 促销控制器基类
/// </summary>
[ApiAuthorize]
[ApiExplorerSettings(GroupName = "user")]
[Route("promotions")]
[DefaultController]
public class DefaultPromotionController : ApiControllerBase
{
    private readonly IPromotionService _promotionService;
    private readonly ICouponService _couponService;

    public DefaultPromotionController(IPromotionService promotionService, ICouponService couponService)
    {
        _promotionService = Check.NotNull(promotionService);
        _couponService = Check.NotNull(couponService);
    }

    protected IPromotionService PromotionService => _promotionService;
    protected ICouponService CouponService => _couponService;

    /// <summary>
    /// 验证优惠券（含适用产品/计划范围、首单限定与持券校验）
    /// </summary>
    [HttpPost("validate-coupon")]
    public virtual async Task<ApiResult<CouponValidationResultDto>> ValidateCoupon([FromBody] ValidateCouponDto request)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _promotionService.ValidateCouponAsync(new CouponApplyContext
        {
            CouponCode = request.CouponCode,
            UserId = userId,
            OrderAmount = request.OrderAmount,
            Currency = request.Currency ?? PaymentConstants.DefaultCurrency,
            ProductType = request.ProductType,
            ScopeId = request.ProductId
        });
        return result.ToApiResult();
    }

    /// <summary>
    /// 计算折扣
    /// </summary>
    [HttpPost("calculate-discount")]
    public virtual async Task<ApiResult<DiscountCalculationResultDto>> CalculateDiscount([FromBody] CalculateDiscountDto request)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _promotionService.CalculateDiscountAsync(new CouponApplyContext
        {
            CouponCode = request.CouponCode,
            UserId = userId,
            OrderAmount = request.OrderAmount,
            Currency = request.Currency ?? PaymentConstants.DefaultCurrency,
            ProductType = request.ProductType,
            ScopeId = request.ProductId
        });
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取我的优惠券列表
    /// </summary>
    [HttpGet("my-coupons")]
    public virtual async Task<ApiResult<List<UserCouponDto>>> GetMyCoupons()
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _couponService.GetUserAvailableCouponsAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取已使用的优惠券
    /// </summary>
    [HttpGet("used-coupons")]
    public virtual async Task<ApiResult<List<CouponUsageDto>>> GetUsedCoupons()
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _couponService.GetUserUsedCouponsAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 兑换兑换码
    /// </summary>
    [HttpPost("redeem")]
    public virtual async Task<ApiResult<UserCouponDto>> Redeem([FromBody] RedeemCodeDto request)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _couponService.RedeemAsync(request.Code, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 检查是否可以使用首次订阅优惠
    /// </summary>
    [HttpGet("first-subscription-check")]
    public virtual async Task<ApiResult<bool>> CanUseFirstSubscriptionDiscount()
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _couponService.CanUseFirstSubscriptionDiscountAsync(userId);
        return result.ToApiResult();
    }
}
