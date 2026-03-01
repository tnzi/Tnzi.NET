namespace Tnzi.Identity.Controllers.Admin;

/// <summary>
/// 会话管理控制器基类
/// 提供会话查询、撤销等API端点，所有方法支持重写
/// </summary>
public abstract class SessionAdminControllerBase : ApiAdminControllerBase
{
    protected readonly ISessionService SessionService;

    /// <summary>
    /// 初始化会话管理控制器基类
    /// </summary>
    /// <param name="sessionService">会话服务</param>
    protected SessionAdminControllerBase(ISessionService sessionService)
    {
        SessionService = Check.NotNull(sessionService);
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
    public virtual async Task<ApiResult> RevokeSession(Guid sessionId)
    {
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
    public virtual async Task<ApiResult> RevokeAllSessions(Guid userId, [FromBody] Guid? excludeSessionId = null)
    {
        var result = await SessionService.RevokeAllSessionsAsync(userId, excludeSessionId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新会话活动时间
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <returns>操作结果</returns>
    [HttpPost("{sessionId}/update-activity")]
    public virtual async Task<ApiResult> UpdateActivityTime(Guid sessionId)
    {
        var result = await SessionService.UpdateActivityTimeAsync(sessionId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 清理超过指定时间未活跃的会话
    /// </summary>
    /// <param name="inactiveMinutes">不活跃时间阈值（分钟，默认30分钟）</param>
    /// <returns>清理的会话数量</returns>
    [HttpPost("clean-expired")]
    public virtual async Task<ApiResult<int>> CleanExpired([FromQuery] int inactiveMinutes = 30)
    {
        var result = await SessionService.CleanExpiredSessionsAsync(TimeSpan.FromMinutes(inactiveMinutes));
        return result.ToApiResult();
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

    // 注意：会话管理不是CRUD操作，不提供钩子方法
    // 如需扩展，请使用重写方法
}
