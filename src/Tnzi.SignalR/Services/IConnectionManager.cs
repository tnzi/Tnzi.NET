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

    /// <summary>
    /// 获取所有在线用户ID
    /// </summary>
    /// <returns>在线用户ID集合</returns>
    Task<IEnumerable<Guid>> GetAllOnlineUserIdsAsync() => Task.FromResult<IEnumerable<Guid>>([]);

    /// <summary>
    /// 获取总连接数（所有用户的连接总和）
    /// </summary>
    /// <returns>总连接数</returns>
    Task<int> GetTotalConnectionCountAsync() => Task.FromResult(0);

    /// <summary>
    /// 添加带元数据的用户连接
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="connectionId">连接ID</param>
    /// <param name="metadata">连接元数据</param>
    /// <returns>任务</returns>
    Task AddConnectionAsync(Guid userId, string connectionId, ConnectionMetadata metadata)
        => AddConnectionAsync(userId, connectionId);

    /// <summary>
    /// 获取连接的元数据
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <returns>连接元数据，不存在时返回 null</returns>
    Task<ConnectionMetadata?> GetConnectionMetadataAsync(string connectionId) => Task.FromResult<ConnectionMetadata?>(null);

    /// <summary>
    /// 通过连接ID查找用户ID
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <returns>用户ID，不存在时返回 null</returns>
    Task<Guid?> GetUserIdByConnectionAsync(string connectionId) => Task.FromResult<Guid?>(null);

    /// <summary>
    /// 将连接加入自定义组
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <param name="groupName">组名</param>
    /// <returns>任务</returns>
    Task AddToGroupAsync(string connectionId, string groupName) => Task.CompletedTask;

    /// <summary>
    /// 将连接从自定义组移除
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <param name="groupName">组名</param>
    /// <returns>任务</returns>
    Task RemoveFromGroupAsync(string connectionId, string groupName) => Task.CompletedTask;

    /// <summary>
    /// 获取指定组的所有连接ID
    /// </summary>
    /// <param name="groupName">组名</param>
    /// <returns>连接ID集合</returns>
    Task<IEnumerable<string>> GetGroupConnectionsAsync(string groupName) => Task.FromResult<IEnumerable<string>>([]);

    /// <summary>
    /// 获取连接所属的所有自定义组
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <returns>组名集合</returns>
    Task<IEnumerable<string>> GetConnectionGroupsAsync(string connectionId) => Task.FromResult<IEnumerable<string>>([]);

    /// <summary>
    /// 批量获取多个连接的元数据（避免 N+1 查询）
    /// </summary>
    /// <param name="connectionIds">连接ID集合</param>
    /// <returns>connectionId -> metadata 字典</returns>
    Task<IReadOnlyDictionary<string, ConnectionMetadata>> GetConnectionsMetadataBatchAsync(IEnumerable<string> connectionIds)
    {
        return Task.FromResult<IReadOnlyDictionary<string, ConnectionMetadata>>(
            new Dictionary<string, ConnectionMetadata>());
    }

    /// <summary>
    /// 批量获取多个连接所属的自定义组（避免 N+1 查询）
    /// </summary>
    /// <param name="connectionIds">连接ID集合</param>
    /// <returns>connectionId -> groupNames 字典</returns>
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetConnectionsGroupsBatchAsync(IEnumerable<string> connectionIds)
    {
        return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(
            new Dictionary<string, IReadOnlyList<string>>());
    }
}
