namespace Tnzi.Identity.Controllers.Admin;

/// <summary>
/// 会话管理控制器
/// 提供会话查询、撤销等API端点，所有方法支持重写
/// </summary>
/// <remarks>
/// ★ 三个撤销端点（单条 / 该用户全部 / 清扫失活）都走 <see cref="ISessionRevocationService"/>：
/// 它是撤销的唯一出口（撤会话 + 删绑定其上的刷新令牌 + 发 <c>SessionsRevokedEvent</c>）。
/// 此前这里直接调 <see cref="ISessionService"/>，只翻会话行 —— 管理员点了「踢下线」，
/// 被踢设备的刷新令牌原样留在库里，<c>EnforceSessionValidation</c> 一关就照常续期，
/// 而事件订阅者（审计、通知）对这次撤销一无所知。
/// 出口缺席时退回只撤会话，与 <c>DefaultUserProfileController</c> 同形。
/// </remarks>
[DefaultController]
[Route("admin/sessions")]
[ApiAuthorize(PermissionName = "session.view")]
public class DefaultSessionAdminController : ApiAdminControllerBase
{
    protected readonly ISessionService SessionService;
    protected readonly ISessionRevocationService? SessionRevocation;
    protected readonly IOptionsMonitor<IdentityOptions>? SessionPolicyOptions;

    /// <summary>
    /// 初始化会话管理控制器
    /// </summary>
    /// <param name="sessionService">会话服务</param>
    /// <param name="sessionRevocation">会话撤销出口（撤会话 + 删刷新令牌 + 发事件）</param>
    /// <param name="identityOptions">会话策略来源（不带阈值的清扫按它定阈值）；缺席时按默认策略</param>
    public DefaultSessionAdminController(
        ISessionService sessionService,
        ISessionRevocationService? sessionRevocation = null,
        IOptionsMonitor<IdentityOptions>? identityOptions = null)
    {
        SessionService = Check.NotNull(sessionService);
        SessionRevocation = sessionRevocation;
        SessionPolicyOptions = identityOptions;
    }

    /// <summary>
    /// 分页查询会话列表 - userId 可选；不传时返回全局会话列表（按最后活动时间倒序，含 userName）
    /// </summary>
    /// <param name="query">查询条件（userId 可选 + includeRevoked + pageIndex/pageSize）</param>
    /// <returns>分页会话列表</returns>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<UserSessionDto>>> GetSessions([FromQuery] SessionQueryDto query)
    {
        var result = await SessionService.GetSessionsAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取用户的所有会话
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="includeRevoked">是否包含已撤销的会话</param>
    /// <returns>会话列表</returns>
    [HttpGet("user/{userId}")]
    public virtual async Task<ApiResult<IEnumerable<UserSessionDto>>> GetUserSessions(Guid userId, [FromQuery] bool includeRevoked = false)
    {
        var result = await SessionService.GetUserSessionsAsync(userId, includeRevoked);
        return result.ToApiResult();
    }

    /// <summary>
    /// 撤销会话
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <returns>操作结果</returns>
    [HttpPost("{sessionId}/revoke")]
    [ApiAuthorize(PermissionName = "session.delete")]
    public virtual async Task<ApiResult> RevokeSession(Guid sessionId)
    {
        if (SessionRevocation != null)
        {
            // 出口返回的是「实际撤销的会话数」：0 = 不存在 / 不在本租户范围内 / 早已撤销，
            // 三者一律 404 —— 区分开就是在告诉对方这个 id 存在，那正是跨租户枚举要的那一位信息。
            var revoked = await SessionRevocation.RevokeSessionAsync(sessionId, SessionRevocationReason.AdminRevoked);
            return revoked > 0 ? Ok() : NotFound("Session not found");
        }

        var result = await SessionService.RevokeSessionAsync(sessionId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 撤销用户的所有会话
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="excludeSessionId">排除的会话ID（可选）</param>
    /// <returns>操作结果</returns>
    [HttpPost("user/{userId}/revoke-all")]
    [ApiAuthorize(PermissionName = "session.delete")]
    public virtual async Task<ApiResult> RevokeAllSessions(Guid userId, [FromBody] Guid? excludeSessionId = null)
    {
        if (SessionRevocation != null)
        {
            // 出口只回「踢掉了几条」，分不出「这个用户没有活动会话」与「这个用户不归你管」。
            // 先经会话服务做一次按范围的读：越界答 404（与不存在同样），范围内的用户即便一条会话都没有也是 200。
            var scoped = await SessionService.GetUserSessionsAsync(userId);
            if (!scoped.Succeeded)
            {
                return Error(scoped.Message ?? "User not found", scoped.Code ?? 404, scoped.ErrorCode);
            }

            await SessionRevocation.RevokeUserSessionsAsync(userId, SessionRevocationReason.AdminRevoked, excludeSessionId);
            return Ok();
        }

        var result = await SessionService.RevokeAllSessionsAsync(userId, excludeSessionId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新会话活动时间
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <returns>操作结果</returns>
    [HttpPost("{sessionId}/update-activity")]
    [ApiAuthorize(PermissionName = "session.update")]
    public virtual async Task<ApiResult> UpdateActivityTime(Guid sessionId)
    {
        var result = await SessionService.UpdateActivityTimeAsync(sessionId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 清理超过指定时间未活跃的会话
    /// </summary>
    /// <param name="inactiveMinutes">不活跃时间阈值（分钟，最小 1）；不传 = 按会话策略，与后台维护任务同一个阈值</param>
    /// <returns>清理的会话数量</returns>
    /// <remarks>
    /// ★ 阈值不接受 0 或负数：那会把「清理失活会话」变成「撤销当前全部会话并删光刷新令牌」——
    /// 另一个动作，不该藏在一个查询参数里。多租户下清扫按当前租户裁剪（见会话服务），
    /// 后台维护任务的同一条清扫没有主体、不裁剪。
    /// <para>
    /// ★ 不传阈值时取 <see cref="SessionLifetime.ResolveInactiveThreshold"/>（闲置超时与刷新令牌周期里更短的那个），
    /// 不是一个写死的分钟数。它曾默认 30 分钟，而管理端的「清理过期会话」从不传阈值、确认框说的是
    /// 「按后端策略清扫」：默认配置下（刷新令牌 7 天、不设闲置超时）点一下，半小时没发过请求的所有用户
    /// 都被踢下线、刷新令牌被删，而后台任务自己清扫时只会动失活超过 7 天的会话。
    /// </para>
    /// </remarks>
    [HttpPost("clean-expired")]
    [ApiAuthorize(PermissionName = "session.delete")]
    public virtual async Task<ApiResult<int>> CleanExpired([FromQuery] int? inactiveMinutes = null)
    {
        if (inactiveMinutes < 1)
        {
            return BadRequest<int>("inactiveMinutes must be at least 1");
        }

        var threshold = inactiveMinutes.HasValue
            ? TimeSpan.FromMinutes(inactiveMinutes.Value)
            : ResolvePolicyThreshold();

        if (SessionRevocation != null)
        {
            var revoked = await SessionRevocation.RevokeInactiveSessionsAsync(threshold);
            return Ok(revoked);
        }

        var result = await SessionService.CleanExpiredSessionsAsync(threshold);
        return result.ToApiResult();
    }

    /// <summary>
    /// 会话策略给出的失活阈值，与后台维护任务的清扫同口径。
    /// 读不到配置（子类没有把选项传给基类）时按默认策略，而不是退回任何更激进的数。
    /// </summary>
    protected TimeSpan ResolvePolicyThreshold()
    {
        var options = SessionPolicyOptions?.CurrentValue ?? new IdentityOptions();
        return SessionLifetime.ResolveInactiveThreshold(
            options.AccountSecurity.SessionTimeoutMinutes,
            options.Jwt.RefreshTokenExpirationDays);
    }

    /// <summary>
    /// 获取会话统计信息
    /// </summary>
    /// <returns>会话统计信息（活跃会话数、在线用户数、设备分布）</returns>
    [HttpGet("statistics")]
    public virtual async Task<ApiResult<SessionStatisticsDto>> GetStatistics()
    {
        var result = await SessionService.GetSessionStatisticsAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取当前活跃用户列表（按最后活跃时间倒序），用于 admin 会话页的"活跃用户"下拉
    /// </summary>
    /// <param name="top">返回的最大用户数（默认 50，上限 500）</param>
    /// <returns>活跃用户摘要列表（含 userName + sessionCount + lastActivityTime）</returns>
    [HttpGet("active-users")]
    public virtual async Task<ApiResult<IEnumerable<ActiveUserSummaryDto>>> GetActiveUsers([FromQuery] int top = 50)
    {
        var result = await SessionService.GetActiveUsersAsync(top);
        return result.ToApiResult();
    }

    // 注意：会话管理不是CRUD操作，不提供钩子方法
    // 如需扩展，请使用重写方法
}
