namespace Tnzi.Payment.Promotions.Controllers;

/// <summary>
/// 促销管理控制器基类
/// </summary>
[Route("admin/promotions")]
[DefaultController]
[ApiAuthorize(PermissionName = "payment.promotion.view")]
public class DefaultPromotionAdminController : ApiAdminControllerBase
{
    private readonly IPromotionService _promotionService;
    private readonly ICouponIssuanceService _couponIssuanceService;

    public DefaultPromotionAdminController(IPromotionService promotionService, ICouponIssuanceService couponIssuanceService)
    {
        _promotionService = Check.NotNull(promotionService);
        _couponIssuanceService = Check.NotNull(couponIssuanceService);
    }

    protected IPromotionService PromotionService => _promotionService;

    /// <summary>
    /// 发券。拆分前这里是父模块的 <c>ICouponService</c>；铸兑换码与定向发券这两个
    /// 「凭空造出可以抵钱的东西」的方法现在单独成契约，只有本控制器注入它。
    /// </summary>
    protected ICouponIssuanceService CouponIssuanceService => _couponIssuanceService;

    /// <summary>
    /// 创建促销
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "payment.promotion.create")]
    public virtual async Task<ApiResult<PromotionDto>> Create([FromBody] CreatePromotionDto request)
    {
        var result = await _promotionService.CreateAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取促销信息
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<PromotionDto>> Get(Guid id)
    {
        var result = await _promotionService.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 根据代码获取促销
    /// </summary>
    [HttpGet("by-code/{code}")]
    public virtual async Task<ApiResult<PromotionDto>> GetByCode(string code)
    {
        var result = await _promotionService.GetByCodeAsync(code);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取促销列表
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<PromotionDto>>> GetList([FromQuery] PromotionQueryDto query)
    {
        var result = await _promotionService.GetListAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新促销
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "payment.promotion.update")]
    public virtual async Task<ApiResult> Update(Guid id, [FromBody] UpdatePromotionDto request)
    {
        var result = await _promotionService.UpdateAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 停用促销
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    [ApiAuthorize(PermissionName = "payment.promotion.update")]
    public virtual async Task<ApiResult> Deactivate(Guid id)
    {
        var result = await _promotionService.DeactivateAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 同步到Stripe
    /// </summary>
    [HttpPost("{id:guid}/sync-stripe")]
    [ApiAuthorize(PermissionName = "payment.promotion.update")]
    public virtual async Task<ApiResult> SyncToStripe(Guid id)
    {
        var result = await _promotionService.SyncToStripeAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建兑换码
    /// </summary>
    [HttpPost("redemption-codes")]
    [ApiAuthorize(PermissionName = "payment.promotion.create")]
    public virtual async Task<ApiResult<string>> CreateRedemptionCode([FromBody] CreateRedemptionCodeDto request)
    {
        var result = await _couponIssuanceService.CreateRedemptionCodeAsync(request.PromotionId, request.Quantity);
        return result.ToApiResult();
    }

    /// <summary>
    /// 直接给用户发券（客服补偿/运营定向发放，不经兑换码）
    /// </summary>
    [HttpPost("{id:guid}/grant")]
    [ApiAuthorize(PermissionName = "payment.promotion.create")]
    public virtual async Task<ApiResult<UserCouponDto>> Grant(Guid id, [FromBody] GrantCouponDto request)
    {
        var result = await _couponIssuanceService.GrantAsync(id, request.UserId);
        return result.ToApiResult();
    }
}
