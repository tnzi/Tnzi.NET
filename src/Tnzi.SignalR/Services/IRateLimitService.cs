namespace Tnzi.SignalR.Services;

/// <summary>
/// 速率限制服务接口
/// </summary>
public interface IRateLimitService
{
    /// <summary>
    /// 检查用户是否可以建立新连接
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>是否允许</returns>
    Task<bool> CheckConnectionLimitAsync(Guid userId);

    /// <summary>
    /// 检查用户是否可以发送消息
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>是否允许</returns>
    Task<bool> CheckMessageRateLimitAsync(Guid userId);

    /// <summary>
    /// 记录消息发送
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>任务</returns>
    Task RecordMessageAsync(Guid userId);

    /// <summary>
    /// 检查用户是否被封禁
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>是否被封禁</returns>
    Task<bool> IsUserBannedAsync(Guid userId);

    /// <summary>
    /// 封禁用户
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="duration">封禁时长</param>
    /// <returns>任务</returns>
    Task BanUserAsync(Guid userId, TimeSpan duration);
}
