namespace Tnzi.Payment.Controllers.Admin;

/// <summary>
/// 订阅管理控制器基类
/// </summary>
[Route("admin/subscriptions")]
[DefaultController]
[ApiAuthorize(PermissionName = "payment.subscription.view")]
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
    public virtual async Task<ApiResult<List<SubscriptionPlanDto>>> GetPlans([FromQuery] bool activeOnly = false, [FromQuery] string? productCode = null)
    {
        var result = await _subscriptionService.GetSubscriptionPlansAsync(activeOnly, productCode);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建订阅计划
    /// </summary>
    [HttpPost("plans")]
    [ApiAuthorize(PermissionName = "payment.subscription.create")]
    public virtual async Task<ApiResult<SubscriptionPlanDto>> CreatePlan([FromBody] SubscriptionPlanDto dto)
    {
        var result = await _subscriptionService.CreatePlanAsync(dto);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新订阅计划
    /// </summary>
    [HttpPut("plans/{id:guid}")]
    [ApiAuthorize(PermissionName = "payment.subscription.update")]
    public virtual async Task<ApiResult> UpdatePlan(Guid id, [FromBody] SubscriptionPlanDto dto)
    {
        var result = await _subscriptionService.UpdatePlanAsync(id, dto);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除订阅计划
    /// </summary>
    [HttpDelete("plans/{id:guid}")]
    [ApiAuthorize(PermissionName = "payment.subscription.delete")]
    public virtual async Task<ApiResult> DeletePlan(Guid id)
    {
        var result = await _subscriptionService.DeletePlanAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 取消订阅
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [ApiAuthorize(PermissionName = "payment.subscription.update")]
    public virtual async Task<ApiResult> Cancel(Guid id, [FromBody] CancelSubscriptionDto request)
    {
        var result = await _subscriptionService.CancelSubscriptionAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 暂停订阅（运营代客操作）
    /// </summary>
    [HttpPost("{id:guid}/pause")]
    [ApiAuthorize(PermissionName = "payment.subscription.update")]
    public virtual async Task<ApiResult> Pause(Guid id, [FromBody] PauseSubscriptionDto request)
    {
        var result = await _subscriptionService.PauseSubscriptionAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 恢复订阅（运营代客操作）
    /// </summary>
    [HttpPost("{id:guid}/resume")]
    [ApiAuthorize(PermissionName = "payment.subscription.update")]
    public virtual async Task<ApiResult> Resume(Guid id)
    {
        var result = await _subscriptionService.ResumeSubscriptionAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 立即重试扣款：处理逾期欠费工单时最常用的动作，
    /// 此前只能干等下一轮后台扫描。
    /// </summary>
    [HttpPost("{id:guid}/retry-billing")]
    [ApiAuthorize(PermissionName = "payment.subscription.update")]
    public virtual async Task<ApiResult> RetryBilling(Guid id)
    {
        var result = await _subscriptionService.RetryBillingAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新自动续费开关（运营代客操作）
    /// </summary>
    [HttpPost("{id:guid}/auto-renew")]
    [ApiAuthorize(PermissionName = "payment.subscription.update")]
    public virtual async Task<ApiResult> UpdateAutoRenew(Guid id, [FromBody] UpdateAutoRenewDto request)
    {
        var result = await _subscriptionService.UpdateAutoRenewAsync(id, request.AutoRenew);
        return result.ToApiResult();
    }
}
