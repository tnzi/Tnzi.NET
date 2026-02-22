
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
    /// userId → connectionIds 映射
    /// 使用 ConcurrentDictionary{string, byte} 模拟线程安全的 HashSet
    /// </summary>
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> _userConnections = new();

    /// <summary>
    /// connectionId → userId 反向索引，用于快速查找
    /// </summary>
    private readonly ConcurrentDictionary<string, Guid> _connectionUsers = new();

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

        var connections = _userConnections.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>());
        connections.TryAdd(connectionId, 0);
        _connectionUsers.TryAdd(connectionId, userId);

        _logger.LogDebug(
            "Connection added for user {UserId}. ConnectionId: {ConnectionId}, TotalConnections: {Count}",
            userId, connectionId, connections.Count);

        return Task.CompletedTask;
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

        if (_userConnections.TryGetValue(userId, out var connections))
        {
            connections.TryRemove(connectionId, out _);

            // 如果用户没有任何连接了，清理条目
            if (connections.IsEmpty)
            {
                _userConnections.TryRemove(userId, out _);
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
            // 清理反向索引
            foreach (var connectionId in connections.Keys)
            {
                _connectionUsers.TryRemove(connectionId, out _);
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
}
