namespace Tnzi.Payment.Controllers;

/// <summary>
/// 订阅控制器基类
/// </summary>
[ApiAuthorize]
[ApiExplorerSettings(GroupName = "user")]
[Route("subscriptions")]
public abstract class SubscriptionControllerBase : ApiControllerBase
{
    private readonly ISubscriptionService _subscriptionService;

    protected SubscriptionControllerBase(ISubscriptionService subscriptionService)
    {
        _subscriptionService = Check.NotNull(subscriptionService);
    }

    protected ISubscriptionService SubscriptionService => _subscriptionService;

    /// <summary>
    /// 创建订阅
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<SubscriptionDto>> Create([FromBody] CreateSubscriptionDto request)
    {
        var result = await _subscriptionService.CreateSubscriptionAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取订阅信息
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<SubscriptionDto>> Get(Guid id)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _subscriptionService.GetSubscriptionAsync(id, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取我的订阅列表
    /// </summary>
    [HttpGet("my")]
    public virtual async Task<ApiResult<IPagedList<SubscriptionDto>>> GetMySubscriptions()
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _subscriptionService.GetUserSubscriptionsAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取订阅列表
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<SubscriptionDto>>> GetList([FromQuery] SubscriptionQueryDto query)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _subscriptionService.GetSubscriptionListAsync(query, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 取消订阅
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public virtual async Task<ApiResult> Cancel(Guid id, [FromBody] CancelSubscriptionDto request)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _subscriptionService.CancelSubscriptionAsync(id, request, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 恢复订阅
    /// </summary>
    [HttpPost("{id:guid}/resume")]
    public virtual async Task<ApiResult> Resume(Guid id)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _subscriptionService.ResumeSubscriptionAsync(id, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 变更订阅计划
    /// </summary>
    [HttpPost("{id:guid}/change-plan")]
    public virtual async Task<ApiResult<SubscriptionDto>> ChangePlan(Guid id, [FromBody] ChangeSubscriptionDto request)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _subscriptionService.ChangePlanAsync(id, request, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新支付方式
    /// </summary>
    [HttpPost("{id:guid}/payment-method")]
    public virtual async Task<ApiResult> UpdatePaymentMethod(Guid id, [FromBody] UpdatePaymentMethodDto request)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _subscriptionService.UpdatePaymentMethodAsync(id, request.PaymentMethodId, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新自动续费设置
    /// </summary>
    [HttpPost("{id:guid}/auto-renew")]
    public virtual async Task<ApiResult> UpdateAutoRenew(Guid id, [FromBody] UpdateAutoRenewDto request)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _subscriptionService.UpdateAutoRenewAsync(id, request.AutoRenew, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取订阅计划列表
    /// </summary>
    [HttpGet("plans")]
    public virtual async Task<ApiResult<List<SubscriptionPlanDto>>> GetPlans([FromQuery] bool activeOnly = true)
    {
        var result = await _subscriptionService.GetSubscriptionPlansAsync(activeOnly);
        return result.ToApiResult();
    }
}
