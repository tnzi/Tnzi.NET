namespace Tnzi.Payment.Promotions.Controllers;

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
    private readonly ICouponWalletService _couponWalletService;

    public DefaultPromotionController(IPromotionService promotionService, ICouponWalletService couponWalletService)
    {
        _promotionService = Check.NotNull(promotionService);
        _couponWalletService = Check.NotNull(couponWalletService);
    }

    protected IPromotionService PromotionService => _promotionService;

    /// <summary>
    /// 用户券包。拆分前这里是父模块的 <c>ICouponService</c>；那个接口现在只留下
    /// 父模块自己调用的四个支付流程方法，券包的四个方法住在本模块。
    /// </summary>
    protected ICouponWalletService CouponWalletService => _couponWalletService;

    /// <summary>
    /// 验证优惠券（含适用产品/计划范围、首单限定与持券校验）
    /// </summary>
    [HttpPost("validate-coupon")]
    public virtual async Task<ApiResult<CouponValidationResponseDto>> ValidateCoupon([FromBody] ValidateCouponDto request)
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

        return result.Map(Narrow).ToApiResult();
    }

    /// <summary>
    /// 收窄成付款人该看到的那几项。
    /// </summary>
    /// <remarks>
    /// 服务层的结果上挂着完整的促销对象（<c>TotalUsageLimit</c> / <c>UsedCount</c> /
    /// <c>IsPublic</c> / <c>StripeCouponId</c>）—— 运营口径与渠道侧标识，付款人一个都不需要，
    /// 却能从一个只需登录就能调的端点全量读出来：已用次数配上总量就是这次活动的实时进度，
    /// <c>IsPublic</c> 直接说明哪些码值得去猜。
    /// </remarks>
    private static CouponValidationResponseDto Narrow(CouponValidationResultDto result) => new()
    {
        IsValid = result.IsValid,
        CouponCode = result.CouponCode,
        DiscountAmount = result.DiscountAmount,
        ErrorMessage = result.ErrorMessage,
        Coupon = result.Promotion == null ? null : new CouponSummaryDto
        {
            Name = result.Promotion.Name,
            Description = result.Promotion.Description,
            DiscountType = result.Promotion.DiscountType,
            DiscountValue = result.Promotion.DiscountValue,
            Currency = result.Promotion.Currency,
            MinimumOrderAmount = result.Promotion.MinimumOrderAmount,
            MaxDiscountAmount = result.Promotion.MaxDiscountAmount,
            EndTime = result.Promotion.EndTime
        }
    };

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
        var result = await _couponWalletService.GetUserAvailableCouponsAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取已使用的优惠券
    /// </summary>
    [HttpGet("used-coupons")]
    public virtual async Task<ApiResult<List<CouponUsageDto>>> GetUsedCoupons()
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _couponWalletService.GetUserUsedCouponsAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 兑换兑换码
    /// </summary>
    [HttpPost("redeem")]
    public virtual async Task<ApiResult<UserCouponDto>> Redeem([FromBody] RedeemCodeDto request)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _couponWalletService.RedeemAsync(request.Code, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 检查是否可以使用首次订阅优惠
    /// </summary>
    [HttpGet("first-subscription-check")]
    public virtual async Task<ApiResult<bool>> CanUseFirstSubscriptionDiscount()
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _couponWalletService.CanUseFirstSubscriptionDiscountAsync(userId);
        return result.ToApiResult();
    }
}
