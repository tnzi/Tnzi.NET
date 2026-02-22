namespace Tnzi.Payment.Controllers.Admin;

/// <summary>
/// 促销管理控制器基类
/// </summary>
[Route("admin/promotions")]
public abstract class PromotionAdminControllerBase : ApiAdminControllerBase
{
    private readonly IPromotionService _promotionService;
    private readonly ICouponService _couponService;

    protected PromotionAdminControllerBase(IPromotionService promotionService, ICouponService couponService)
    {
        _promotionService = Check.NotNull(promotionService);
        _couponService = Check.NotNull(couponService);
    }

    protected IPromotionService PromotionService => _promotionService;
    protected ICouponService CouponService => _couponService;

    /// <summary>
    /// 创建促销
    /// </summary>
    [HttpPost]
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
    public virtual async Task<ApiResult> Update(Guid id, [FromBody] UpdatePromotionDto request)
    {
        var result = await _promotionService.UpdateAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 停用促销
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    public virtual async Task<ApiResult> Deactivate(Guid id)
    {
        var result = await _promotionService.DeactivateAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 同步到Stripe
    /// </summary>
    [HttpPost("{id:guid}/sync-stripe")]
    public virtual async Task<ApiResult> SyncToStripe(Guid id)
    {
        var result = await _promotionService.SyncToStripeAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建兑换码
    /// </summary>
    [HttpPost("redemption-codes")]
    public virtual async Task<ApiResult<string>> CreateRedemptionCode([FromBody] CreateRedemptionCodeDto request)
    {
        var result = await _couponService.CreateRedemptionCodeAsync(request.PromotionId, request.Quantity);
        return result.ToApiResult();
    }
}
