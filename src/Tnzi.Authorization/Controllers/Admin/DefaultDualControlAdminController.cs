namespace Tnzi.Authorization.Controllers.Admin;

/// <summary>
/// 双人授权（四眼原则）管理控制器：审批人的待办面。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>刻意没有「发起」端点。</b>发起必然发生在某个业务动作的上下文里（要带上那次动作的参数快照），
/// 由业务自己的控制器调 <see cref="IDualControlService.RequestAsync"/> —— 与
/// <c>IPasskeyEnrollmentTokenService</c> 不出签发端点是同一条判据：<b>签发是业务决策</b>。
/// 而查阅、批准、拒绝、撤回都是通用动作，不该让每个消费方各写一遍。
/// </para>
/// <para>
/// <b>权限是两层的</b>：类级 <c>authorization.dualControl.view</c> 决定能不能用这个功能，
/// 服务层再按 <c>{Operation}.approve</c> 决定能批哪些动作。粗码控制入口、细码控制范围，
/// 少了后者就变成一个「能批准任何东西」的通行证。
/// </para>
/// </remarks>
[DefaultController]
[Route("admin/dual-control")]
[ApiAuthorize(PermissionName = "authorization.dualControl.view")]
public class DefaultDualControlAdminController : ApiAdminControllerBase
{
    protected readonly IDualControlService DualControlService;

    /// <summary>
    /// 初始化双人授权管理控制器。
    /// </summary>
    /// <param name="dualControlService">双人授权服务</param>
    public DefaultDualControlAdminController(IDualControlService dualControlService)
    {
        DualControlService = Check.NotNull(dualControlService);
    }

    /// <summary>
    /// 分页查询双人授权请求。
    /// </summary>
    /// <param name="query">查询条件</param>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<DualControlRequestDto>>> Query([FromQuery] DualControlQueryDto query)
    {
        var result = await DualControlService.QueryAsync(query, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 查询单个请求。
    /// </summary>
    /// <param name="id">请求标识</param>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<DualControlRequestDto>> Get(Guid id)
    {
        var result = await DualControlService.GetAsync(id, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批准一个请求。
    /// </summary>
    /// <param name="id">请求标识</param>
    /// <param name="input">批准说明（可选）</param>
    [HttpPost("{id:guid}/approve")]
    [ApiAuthorize(PermissionName = "authorization.dualControl.approve")]
    public virtual async Task<ApiResult<DualControlRequestDto>> Approve(Guid id, [FromBody] DualControlDecisionDto? input = null)
    {
        var result = await DualControlService.ApproveAsync(id, input?.Comment, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 拒绝一个请求。
    /// </summary>
    /// <param name="id">请求标识</param>
    /// <param name="input">拒绝理由（可选）</param>
    /// <remarks>
    /// 与批准同一个权限码：两者都是「对这条请求做决定」，能批的人当然也能拒。
    /// 反过来给拒绝单独开一个更松的码，等于让人可以在不被授权批准的情况下阻断别人的动作。
    /// </remarks>
    [HttpPost("{id:guid}/reject")]
    [ApiAuthorize(PermissionName = "authorization.dualControl.approve")]
    public virtual async Task<ApiResult<DualControlRequestDto>> Reject(Guid id, [FromBody] DualControlDecisionDto? input = null)
    {
        var result = await DualControlService.RejectAsync(id, input?.Comment, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 由发起人撤回一个未决请求。
    /// </summary>
    /// <param name="id">请求标识</param>
    /// <remarks>
    /// 不挂 <c>.approve</c>：撤回自己发起的东西不需要审批权。
    /// 服务层保证只有发起人能撤 —— 别人要让它不生效，走拒绝，那会留下是谁拒的。
    /// </remarks>
    [HttpPost("{id:guid}/cancel")]
    public virtual async Task<ApiResult> Cancel(Guid id)
    {
        var result = await DualControlService.CancelAsync(id, HttpContext.RequestAborted);
        return result.ToApiResult();
    }
}
