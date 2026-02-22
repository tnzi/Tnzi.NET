
namespace Tnzi.SignalR.Services;

/// <summary>
/// SignalR消息推送服务实现 (泛型版本)
/// 直接实现接口，不继承 ApplicationService（不需要 CurrentUser、EventBus 等功能）
/// </summary>
/// <typeparam name="THub">Hub 类型</typeparam>
public class MessagePushService<THub> : IMessagePushService<THub>
    where THub : Hub
{
    private readonly IHubContext<THub> _hubContext;
    private readonly IConnectionManager _connectionManager;
    private readonly ILogger<MessagePushService<THub>> _logger;

    /// <summary>
    /// 初始化一个<see cref="MessagePushService{THub}"/>类型的新实例
    /// </summary>
    public MessagePushService(IHubContext<THub> hubContext, IConnectionManager connectionManager, ILogger<MessagePushService<THub>> logger)
    {
        _hubContext = Check.NotNull(hubContext);
        _connectionManager = Check.NotNull(connectionManager);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 向指定用户推送消息
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="methodName">Hub方法名</param>
    /// <param name="args">参数</param>
    /// <returns>任务</returns>
    public async Task PushToUserAsync(Guid userId, string methodName, params object[] args)
    {
        Check.NotNullOrWhiteSpace(methodName);
        var connections = await _connectionManager.GetUserConnectionsAsync(userId);
        var connectionIds = connections.ToList();

        if (connectionIds.Count > 0)
        {
            await _hubContext.Clients.Clients(connectionIds).SendCoreAsync(methodName, args ?? Array.Empty<object>(), default);
        }
    }

    /// <summary>
    /// 向多个用户推送消息
    /// </summary>
    /// <param name="userIds">用户ID集合</param>
    /// <param name="methodName">Hub方法名</param>
    /// <param name="args">参数</param>
    /// <returns>任务</returns>
    public async Task PushToUsersAsync(IEnumerable<Guid> userIds, string methodName, params object[] args)
    {
        Check.NotNull(userIds);
        Check.NotNullOrWhiteSpace(methodName);
        var allConnections = new List<string>();
        foreach (var userId in userIds)
        {
            var connections = await _connectionManager.GetUserConnectionsAsync(userId);
            allConnections.AddRange(connections);
        }

        if (allConnections.Count > 0)
        {
            await _hubContext.Clients.Clients(allConnections).SendCoreAsync(methodName, args ?? Array.Empty<object>(), default);
        }
    }

    /// <summary>
    /// 向指定组推送消息
    /// </summary>
    /// <param name="groupName">组名</param>
    /// <param name="methodName">Hub方法名</param>
    /// <param name="args">参数</param>
    /// <returns>任务</returns>
    public async Task PushToGroupAsync(string groupName, string methodName, params object[] args)
    {
        Check.NotNullOrWhiteSpace(groupName);
        Check.NotNullOrWhiteSpace(methodName);
        await _hubContext.Clients.Group(groupName).SendCoreAsync(methodName, args ?? Array.Empty<object>(), default);
    }

    /// <summary>
    /// 向所有连接的客户端推送消息
    /// </summary>
    /// <param name="methodName">Hub方法名</param>
    /// <param name="args">参数</param>
    /// <returns>任务</returns>
    public async Task PushToAllAsync(string methodName, params object[] args)
    {
        Check.NotNullOrWhiteSpace(methodName);
        await _hubContext.Clients.All.SendCoreAsync(methodName, args ?? Array.Empty<object>(), default);
    }
}

// 注意：应用程序需要为自己的 Hub 注册具体的 MessagePushService
// 示例：
// services.AddScoped<IMessagePushService<ChatHub>, MessagePushService<ChatHub>>();
// services.AddScoped<IMessagePushService>(sp => sp.GetRequiredService<IMessagePushService<ChatHub>>());
