namespace Tnzi.Payment.Subscriptions.Controllers;

/// <summary>
/// 订阅控制器基类
/// </summary>
[ApiAuthorize]
[ApiExplorerSettings(GroupName = "user")]
[Route("subscriptions")]
[DefaultController]
public class DefaultSubscriptionController : ApiControllerBase
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly ISubscriptionAccessGate? _accessGate;
    private readonly ISubscriptionStorageMeter? _storageMeter;

    /// <param name="subscriptionService">订阅服务</param>
    /// <param name="accessGate">访问门禁（本模块恒注册；可选只是为了不打断按旧签名继承本控制器的消费方）</param>
    /// <param name="storageMeter">存储计量（本模块恒注册；可选的理由同上）</param>
    public DefaultSubscriptionController(ISubscriptionService subscriptionService, ISubscriptionAccessGate? accessGate = null, ISubscriptionStorageMeter? storageMeter = null)
    {
        _subscriptionService = Check.NotNull(subscriptionService);
        _accessGate = accessGate;
        _storageMeter = storageMeter;
    }

    protected ISubscriptionService SubscriptionService => _subscriptionService;

    /// <summary>
    /// 创建订阅。返回订阅本体与首期支付凭据，前端据此直接拉起收银台。
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<SubscriptionCreateResultDto>> Create([FromBody] CreateSubscriptionDto request)
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
    /// 获取我的订阅列表（分页；不带参数时为最新 100 条）
    /// </summary>
    [HttpGet("my")]
    public virtual async Task<ApiResult<IPagedList<SubscriptionDto>>> GetMySubscriptions([FromQuery] OwnerListQueryDto query)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _subscriptionService.GetUserSubscriptionsAsync(userId, query);
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
    /// 暂停订阅
    /// </summary>
    [HttpPost("{id:guid}/pause")]
    public virtual async Task<ApiResult> Pause(Guid id, [FromBody] PauseSubscriptionDto request)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _subscriptionService.PauseSubscriptionAsync(id, request, userId);
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
    /// 立即重试扣款（逾期欠费的订阅换卡后主动挽回）
    /// </summary>
    [HttpPost("{id:guid}/retry-billing")]
    public virtual async Task<ApiResult> RetryBilling(Guid id)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _subscriptionService.RetryBillingAsync(id, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 当前用户所代表的计费账户此刻的境况：外壳启动时与收到 402 之后读它，决定显示提醒横幅、暂停页面还是什么都不显示。
    /// 任何已登录用户都能读；需要处理的那条订阅的细节只给能查看账单的人。门禁永远放行这个端点。
    /// </summary>
    [HttpGet("standing")]
    public virtual async Task<ApiResult<BillingStandingDto>> GetStanding([FromQuery] string? productCode = null)
    {
        if (_accessGate == null)
            return ApiResult<BillingStandingDto>.Error("The subscription access gate is not registered.", 501);

        var userId = GetRequiredCurrentUser().Id!.Value;
        var standing = await _accessGate.GetStandingAsync(userId, productCode, HttpContext.RequestAborted);
        return ApiResult<BillingStandingDto>.Ok(standing);
    }

    /// <summary>
    /// 当前用户所代表的计费账户的存储用量：最近一次每日样本、计划包含量、本期至今按平均用量累计的超额（需要能查看账单）。
    /// 计划不计量存储时 <c>metered = false</c>。
    /// </summary>
    [HttpGet("storage-usage")]
    public virtual async Task<ApiResult<StorageUsageDto>> GetStorageUsage([FromQuery] string? productCode = null)
    {
        if (_storageMeter == null)
            return ApiResult<StorageUsageDto>.Error("Storage metering is not registered.", 501);

        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _storageMeter.GetUsageAsync(userId, productCode, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 当场补缴（逾期欠费或已暂停服务的订阅）：返回收银台单，前端用渠道组件确认（含 3-D Secure）。付清后订阅立即恢复。
    /// </summary>
    [HttpPost("{id:guid}/pay-now")]
    public virtual async Task<ApiResult<SubscriptionPayNowResultDto>> PayNow(Guid id)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _subscriptionService.PayNowAsync(id, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 变更订阅计划（含按比例计费）
    /// </summary>
    [HttpPost("{id:guid}/change-plan")]
    public virtual async Task<ApiResult<SubscriptionChangeDto>> ChangePlan(Guid id, [FromBody] ChangeSubscriptionPlanDto request)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _subscriptionService.ChangeSubscriptionPlanAsync(id, request, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 预览计划变更（按比例计费）
    /// </summary>
    [HttpGet("{id:guid}/change-plan-preview")]
    public virtual async Task<ApiResult<SubscriptionChangeDto>> ChangePlanPreview(Guid id, [FromQuery] Guid newPlanId)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _subscriptionService.GetPlanChangePreviewAsync(id, newPlanId, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 绑定/更新本订阅使用的支付方式；订阅处于逾期欠费时会立即重试一次扣款
    /// </summary>
    [HttpPost("{id:guid}/payment-method")]
    public virtual async Task<ApiResult<SubscriptionDto>> UpdatePaymentMethod(Guid id, [FromBody] AttachPaymentMethodDto request)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _subscriptionService.UpdatePaymentMethodAsync(id, request, userId);
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
    public virtual async Task<ApiResult<List<SubscriptionPlanDto>>> GetPlans([FromQuery] bool activeOnly = true, [FromQuery] string? productCode = null)
    {
        // 用户端带税：开通页要写「含税 / 另加税」与一期应付额
        var result = await _subscriptionService.GetSubscriptionPlansAsync(activeOnly, productCode, includeTax: true, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 取消待生效的计划变更
    /// </summary>
    /// <remarks>
    /// ★ 模板必须是<b>相对</b>的。此前写成 <c>~/subscription-changes/{id}/cancel</c>：ASP.NET Core 对以 <c>~/</c>
    /// 或 <c>/</c> 开头的方法级模板整条覆盖控制器模板，而 <c>api</c> 前缀是 <c>RoutePrefixConvention</c>
    /// 只写进控制器选择器的 —— 那个端点于是成了全仓唯一没有 <c>/api</c> 的路由，
    /// <c>@tnzi/core</c> 带着 baseUrl 去调必 404，一次降级之后用户在界面上就再也撤不回来。
    /// <c>changes</c> 不是 guid，与 <c>{id}/cancel</c> 不撞。
    /// </remarks>
    [HttpPost("changes/{id:guid}/cancel")]
    public virtual async Task<ApiResult> CancelPendingChange(Guid id)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _subscriptionService.CancelPendingChangeAsync(id, userId);
        return result.ToApiResult();
    }
}
