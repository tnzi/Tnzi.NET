namespace Tnzi.SignalR.Services;

/// <summary>
/// SignalR连接管理服务接口
/// </summary>
public interface IConnectionManager
{
    /// <summary>
    /// 获取用户的所有连接ID
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>连接ID集合</returns>
    Task<IEnumerable<string>> GetUserConnectionsAsync(Guid userId);

    /// <summary>
    /// 添加用户连接
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="connectionId">连接ID</param>
    /// <returns>任务</returns>
    Task AddConnectionAsync(Guid userId, string connectionId);

    /// <summary>
    /// 移除用户连接
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="connectionId">连接ID</param>
    /// <returns>任务</returns>
    Task RemoveConnectionAsync(Guid userId, string connectionId);

    /// <summary>
    /// 移除用户的所有连接
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>任务</returns>
    Task RemoveUserConnectionsAsync(Guid userId);

    /// <summary>
    /// 检查用户是否在线
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>是否在线</returns>
    Task<bool> IsUserOnlineAsync(Guid userId);

    /// <summary>
    /// 获取用户的连接数
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>连接数</returns>
    Task<int> GetConnectionCountAsync(Guid userId);

    /// <summary>
    /// 获取在线用户数
    /// </summary>
    /// <returns>在线用户数</returns>
    Task<int> GetOnlineUserCountAsync();
}
