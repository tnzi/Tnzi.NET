
namespace Tnzi.SignalR.Services;

/// <summary>
/// SignalR连接管理服务实现（内存版本）
/// 使用 ConcurrentDictionary 实现线程安全的连接管理，消除序列化开销和 SemaphoreSlim 锁。
/// 注意：此实现仅适用于单实例部署。如需分布式（多实例）场景，
/// 需要使用 Redis 等分布式存储替代内存字典。
/// </summary>
public class ConnectionManager : IConnectionManager
{
    /// <summary>
    /// userId -> connectionIds 映射
    /// 使用 ConcurrentDictionary{string, byte} 模拟线程安全的 HashSet
    /// </summary>
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> _userConnections = new();

    /// <summary>
    /// connectionId -> userId 反向索引，用于快速查找
    /// </summary>
    private readonly ConcurrentDictionary<string, Guid> _connectionUsers = new();

    /// <summary>
    /// connectionId -> metadata 元数据映射
    /// </summary>
    private readonly ConcurrentDictionary<string, ConnectionMetadata> _connectionMetadata = new();

    /// <summary>
    /// groupName -> connectionIds 组映射
    /// </summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _groupConnections = new();

    /// <summary>
    /// connectionId -> groupNames 反向组索引
    /// </summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _connectionGroups = new();

    private readonly ILogger<ConnectionManager> _logger;

    /// <summary>
    /// 初始化一个<see cref="ConnectionManager"/>类型的新实例
    /// </summary>
    /// <param name="logger">日志记录器</param>
    public ConnectionManager(ILogger<ConnectionManager> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 获取用户的所有连接ID
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>连接ID集合</returns>
    public Task<IEnumerable<string>> GetUserConnectionsAsync(Guid userId)
    {
        if (_userConnections.TryGetValue(userId, out var connections))
        {
            return Task.FromResult<IEnumerable<string>>(connections.Keys.ToList());
        }

        return Task.FromResult<IEnumerable<string>>(Enumerable.Empty<string>());
    }

    /// <summary>
    /// 添加用户连接
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="connectionId">连接ID</param>
    /// <returns>任务</returns>
    public Task AddConnectionAsync(Guid userId, string connectionId)
    {
        Check.NotNullOrWhiteSpace(connectionId);

        // 并发的"最后一个连接断开"清理会在集合变空时把它整条移出索引。若清理发生在
        // GetOrAdd 之后、TryAdd 之前，新连接就被加进了一个已经脱离索引的孤立集合
        // （用户显示离线却仍有活动连接，且后续断开无法清理）。因此加完后确认索引仍
        // 指向本集合，不一致则重试 —— 重试必然重新登记，不会无限循环。
        ConcurrentDictionary<string, byte> connections;
        while (true)
        {
            connections = _userConnections.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>());
            connections.TryAdd(connectionId, 0);

            if (_userConnections.TryGetValue(userId, out var tracked) && ReferenceEquals(tracked, connections))
            {
                break;
            }
        }

        _connectionUsers.TryAdd(connectionId, userId);

        _logger.LogDebug(
            "Connection added for user {UserId}. ConnectionId: {ConnectionId}, TotalConnections: {Count}",
            userId, connectionId, connections.Count);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 添加带元数据的用户连接
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="connectionId">连接ID</param>
    /// <param name="metadata">连接元数据</param>
    /// <returns>任务</returns>
    public Task AddConnectionAsync(Guid userId, string connectionId, ConnectionMetadata metadata)
    {
        Check.NotNull(metadata);

        // Store metadata
        metadata.ConnectionId = connectionId;
        metadata.UserId = userId;
        _connectionMetadata[connectionId] = metadata;

        // Delegate to base connection tracking
        return AddConnectionAsync(userId, connectionId);
    }

    /// <summary>
    /// 移除用户连接
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="connectionId">连接ID</param>
    /// <returns>任务</returns>
    public Task RemoveConnectionAsync(Guid userId, string connectionId)
    {
        Check.NotNullOrWhiteSpace(connectionId);

        _connectionUsers.TryRemove(connectionId, out _);
        _connectionMetadata.TryRemove(connectionId, out _);

        // Clean up group memberships for this connection
        if (_connectionGroups.TryRemove(connectionId, out var groups))
        {
            foreach (var groupName in groups.Keys)
            {
                if (_groupConnections.TryGetValue(groupName, out var groupConns))
                {
                    groupConns.TryRemove(connectionId, out _);
                    if (groupConns.IsEmpty)
                    {
                        ((ICollection<KeyValuePair<string, ConcurrentDictionary<string, byte>>>)_groupConnections)
                            .Remove(new KeyValuePair<string, ConcurrentDictionary<string, byte>>(groupName, groupConns));
                    }
                }
            }
        }

        if (_userConnections.TryGetValue(userId, out var connections))
        {
            connections.TryRemove(connectionId, out _);

            // 如果用户没有任何连接了，清理条目
            // 使用条件移除：仅当字典值仍为同一个空集合时才移除，
            // 防止 AddConnectionAsync 在 IsEmpty 检查和 TryRemove 之间添加了新连接
            if (connections.IsEmpty)
            {
                ((ICollection<KeyValuePair<Guid, ConcurrentDictionary<string, byte>>>)_userConnections)
                    .Remove(new KeyValuePair<Guid, ConcurrentDictionary<string, byte>>(userId, connections));
            }

            _logger.LogDebug(
                "Connection removed for user {UserId}. ConnectionId: {ConnectionId}, RemainingConnections: {Count}",
                userId, connectionId, connections.Count);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 移除用户的所有连接
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>任务</returns>
    public Task RemoveUserConnectionsAsync(Guid userId)
    {
        if (_userConnections.TryRemove(userId, out var connections))
        {
            // 清理反向索引、元数据和组成员
            foreach (var connectionId in connections.Keys)
            {
                _connectionUsers.TryRemove(connectionId, out _);
                _connectionMetadata.TryRemove(connectionId, out _);

                // Clean up group memberships
                if (_connectionGroups.TryRemove(connectionId, out var groups))
                {
                    foreach (var groupName in groups.Keys)
                    {
                        if (_groupConnections.TryGetValue(groupName, out var groupConns))
                        {
                            groupConns.TryRemove(connectionId, out _);
                            if (groupConns.IsEmpty)
                            {
                                ((ICollection<KeyValuePair<string, ConcurrentDictionary<string, byte>>>)_groupConnections)
                                    .Remove(new KeyValuePair<string, ConcurrentDictionary<string, byte>>(groupName, groupConns));
                            }
                        }
                    }
                }
            }
        }

        _logger.LogDebug("All connections removed for user {UserId}", userId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 检查用户是否在线
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>是否在线</returns>
    public Task<bool> IsUserOnlineAsync(Guid userId)
    {
        var isOnline = _userConnections.TryGetValue(userId, out var connections) && !connections.IsEmpty;
        return Task.FromResult(isOnline);
    }

    /// <summary>
    /// 获取用户的连接数
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>连接数</returns>
    public Task<int> GetConnectionCountAsync(Guid userId)
    {
        if (_userConnections.TryGetValue(userId, out var connections))
        {
            return Task.FromResult(connections.Count);
        }

        return Task.FromResult(0);
    }

    /// <summary>
    /// 获取在线用户数
    /// </summary>
    /// <returns>在线用户数</returns>
    public Task<int> GetOnlineUserCountAsync()
    {
        return Task.FromResult(_userConnections.Count);
    }

    /// <summary>
    /// 获取所有在线用户ID
    /// </summary>
    /// <returns>在线用户ID集合</returns>
    public Task<IEnumerable<Guid>> GetAllOnlineUserIdsAsync()
    {
        return Task.FromResult<IEnumerable<Guid>>(_userConnections.Keys.ToList());
    }

    /// <summary>
    /// 获取总连接数（所有用户的连接总和）
    /// </summary>
    /// <returns>总连接数</returns>
    public Task<int> GetTotalConnectionCountAsync()
    {
        return Task.FromResult(_connectionUsers.Count);
    }

    /// <summary>
    /// 获取连接的元数据
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <returns>连接元数据，不存在时返回 null</returns>
    public Task<ConnectionMetadata?> GetConnectionMetadataAsync(string connectionId)
    {
        Check.NotNullOrWhiteSpace(connectionId);
        _connectionMetadata.TryGetValue(connectionId, out var metadata);
        return Task.FromResult(metadata);
    }

    /// <summary>
    /// 通过连接ID查找用户ID
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <returns>用户ID，不存在时返回 null</returns>
    public Task<Guid?> GetUserIdByConnectionAsync(string connectionId)
    {
        Check.NotNullOrWhiteSpace(connectionId);
        if (_connectionUsers.TryGetValue(connectionId, out var userId))
        {
            return Task.FromResult<Guid?>(userId);
        }
        return Task.FromResult<Guid?>(null);
    }

    /// <summary>
    /// 将连接加入自定义组
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <param name="groupName">组名</param>
    /// <returns>任务</returns>
    public Task AddToGroupAsync(string connectionId, string groupName)
    {
        Check.NotNullOrWhiteSpace(connectionId);
        Check.NotNullOrWhiteSpace(groupName);

        var groupConns = _groupConnections.GetOrAdd(groupName, _ => new ConcurrentDictionary<string, byte>());
        groupConns.TryAdd(connectionId, 0);

        var connGroups = _connectionGroups.GetOrAdd(connectionId, _ => new ConcurrentDictionary<string, byte>());
        connGroups.TryAdd(groupName, 0);

        _logger.LogDebug(
            "Connection {ConnectionId} added to group {GroupName}",
            connectionId, groupName);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 将连接从自定义组移除
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <param name="groupName">组名</param>
    /// <returns>任务</returns>
    public Task RemoveFromGroupAsync(string connectionId, string groupName)
    {
        Check.NotNullOrWhiteSpace(connectionId);
        Check.NotNullOrWhiteSpace(groupName);

        if (_groupConnections.TryGetValue(groupName, out var groupConns))
        {
            groupConns.TryRemove(connectionId, out _);
            if (groupConns.IsEmpty)
            {
                ((ICollection<KeyValuePair<string, ConcurrentDictionary<string, byte>>>)_groupConnections)
                    .Remove(new KeyValuePair<string, ConcurrentDictionary<string, byte>>(groupName, groupConns));
            }
        }

        if (_connectionGroups.TryGetValue(connectionId, out var connGroups))
        {
            connGroups.TryRemove(groupName, out _);
            if (connGroups.IsEmpty)
            {
                ((ICollection<KeyValuePair<string, ConcurrentDictionary<string, byte>>>)_connectionGroups)
                    .Remove(new KeyValuePair<string, ConcurrentDictionary<string, byte>>(connectionId, connGroups));
            }
        }

        _logger.LogDebug(
            "Connection {ConnectionId} removed from group {GroupName}",
            connectionId, groupName);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取指定组的所有连接ID
    /// </summary>
    /// <param name="groupName">组名</param>
    /// <returns>连接ID集合</returns>
    public Task<IEnumerable<string>> GetGroupConnectionsAsync(string groupName)
    {
        Check.NotNullOrWhiteSpace(groupName);
        if (_groupConnections.TryGetValue(groupName, out var connections))
        {
            return Task.FromResult<IEnumerable<string>>(connections.Keys.ToList());
        }
        return Task.FromResult<IEnumerable<string>>([]);
    }

    /// <summary>
    /// 获取连接所属的所有自定义组
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <returns>组名集合</returns>
    public Task<IEnumerable<string>> GetConnectionGroupsAsync(string connectionId)
    {
        Check.NotNullOrWhiteSpace(connectionId);
        if (_connectionGroups.TryGetValue(connectionId, out var groups))
        {
            return Task.FromResult<IEnumerable<string>>(groups.Keys.ToList());
        }
        return Task.FromResult<IEnumerable<string>>([]);
    }

    /// <summary>
    /// 批量获取多个连接的元数据（避免 N+1 查询）
    /// </summary>
    /// <param name="connectionIds">连接ID集合</param>
    /// <returns>connectionId -> metadata 字典</returns>
    public Task<IReadOnlyDictionary<string, ConnectionMetadata>> GetConnectionsMetadataBatchAsync(IEnumerable<string> connectionIds)
    {
        Check.NotNull(connectionIds);

        var result = new Dictionary<string, ConnectionMetadata>();
        foreach (var connectionId in connectionIds)
        {
            if (_connectionMetadata.TryGetValue(connectionId, out var metadata))
            {
                result[connectionId] = metadata;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, ConnectionMetadata>>(result);
    }

    /// <summary>
    /// 批量获取多个连接所属的自定义组（避免 N+1 查询）
    /// </summary>
    /// <param name="connectionIds">连接ID集合</param>
    /// <returns>connectionId -> groupNames 字典</returns>
    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetConnectionsGroupsBatchAsync(IEnumerable<string> connectionIds)
    {
        Check.NotNull(connectionIds);

        var result = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var connectionId in connectionIds)
        {
            if (_connectionGroups.TryGetValue(connectionId, out var groups))
            {
                result[connectionId] = groups.Keys.ToList();
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(result);
    }
}
