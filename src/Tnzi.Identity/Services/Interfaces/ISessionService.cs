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
}
