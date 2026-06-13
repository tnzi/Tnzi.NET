namespace Tnzi.Identity.Services;

/// <summary>
/// 会话管理服务接口
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// 创建会话
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="deviceInfo">设备信息</param>
    /// <param name="ipAddress">IP地址</param>
    /// <param name="userAgent">UserAgent</param>
    /// <returns>会话ID</returns>
    Task<Guid> CreateSessionAsync(Guid userId, string? deviceInfo, string? ipAddress, string? userAgent);

    /// <summary>
    /// 获取用户的所有会话
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="includeRevoked">是否包含已撤销的会话</param>
    /// <returns>会话列表</returns>
    Task<Result<IEnumerable<UserSessionDto>>> GetUserSessionsAsync(Guid userId, bool includeRevoked = false);

    /// <summary>
    /// 分页查询会话列表 — UserId 可选；不传时返回全局会话列表（按最后活动时间倒序），
    /// 返回的 DTO 含 UserName（批量关联用户表）。
    /// </summary>
    /// <param name="query">查询条件（UserId 可选 + IncludeRevoked + 分页）</param>
    /// <returns>分页会话列表</returns>
    Task<Result<IPagedList<UserSessionDto>>> GetSessionsAsync(SessionQueryDto query);

    /// <summary>
    /// 撤销会话
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    Task<Result> RevokeSessionAsync(Guid sessionId);

    /// <summary>
    /// 撤销用户的所有会话
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="excludeSessionId">排除的会话ID（可选）</param>
    Task<Result> RevokeAllSessionsAsync(Guid userId, Guid? excludeSessionId = null);

    /// <summary>
    /// 更新会话活动时间
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    Task<Result> UpdateActivityTimeAsync(Guid sessionId);

    /// <summary>
    /// 清理超过指定时间未活跃的会话
    /// </summary>
    /// <param name="inactiveThreshold">不活跃时间阈值</param>
    /// <returns>清理的会话数量</returns>
    Task<Result<int>> CleanExpiredSessionsAsync(TimeSpan inactiveThreshold);

    /// <summary>
    /// 获取会话统计信息（活跃会话数、在线用户数、设备分布 Top 5）
    /// </summary>
    /// <returns>会话统计信息</returns>
    Task<Result<SessionStatisticsDto>> GetSessionStatisticsAsync();

    /// <summary>
    /// 获取活跃用户列表（按最后活跃时间倒序，含会话计数和用户名），用于 admin "活跃用户"下拉
    /// </summary>
    /// <param name="top">返回的最大用户数（默认 50）</param>
    /// <returns>活跃用户摘要列表</returns>
    Task<Result<IEnumerable<ActiveUserSummaryDto>>> GetActiveUsersAsync(int top = 50);
}
