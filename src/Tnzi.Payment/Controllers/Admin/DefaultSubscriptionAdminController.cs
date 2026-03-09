namespace Tnzi.Payment.Controllers.Admin;

/// <summary>
/// 订阅管理控制器基类
/// </summary>
[Route("admin/subscriptions")]
[DefaultController]
public class DefaultSubscriptionAdminController : ApiAdminControllerBase
{
    private readonly ISubscriptionService _subscriptionService;

    public DefaultSubscriptionAdminController(ISubscriptionService subscriptionService)
    {
        _subscriptionService = Check.NotNull(subscriptionService);
    }

    protected ISubscriptionService SubscriptionService => _subscriptionService;

    /// <summary>
    /// 获取订阅信息
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<SubscriptionDto>> Get(Guid id)
    {
        var result = await _subscriptionService.GetSubscriptionAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取订阅列表
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<SubscriptionDto>>> GetList([FromQuery] SubscriptionQueryDto query)
    {
        var result = await _subscriptionService.GetSubscriptionListAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取订阅计划列表
    /// </summary>
    [HttpGet("plans")]
    public virtual async Task<ApiResult<List<SubscriptionPlanDto>>> GetPlans([FromQuery] bool activeOnly = false)
    {
        var result = await _subscriptionService.GetSubscriptionPlansAsync(activeOnly);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建订阅计划
    /// </summary>
    [HttpPost("plans")]
    public virtual async Task<ApiResult<SubscriptionPlanDto>> CreatePlan([FromBody] SubscriptionPlanDto dto)
    {
        var result = await _subscriptionService.CreatePlanAsync(dto);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新订阅计划
    /// </summary>
    [HttpPut("plans/{id:guid}")]
    public virtual async Task<ApiResult> UpdatePlan(Guid id, [FromBody] SubscriptionPlanDto dto)
    {
        var result = await _subscriptionService.UpdatePlanAsync(id, dto);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除订阅计划
    /// </summary>
    [HttpDelete("plans/{id:guid}")]
    public virtual async Task<ApiResult> DeletePlan(Guid id)
    {
        var result = await _subscriptionService.DeletePlanAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 取消订阅
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public virtual async Task<ApiResult> Cancel(Guid id, [FromBody] CancelSubscriptionDto request)
    {
        var result = await _subscriptionService.CancelSubscriptionAsync(id, request);
        return result.ToApiResult();
    }
}
