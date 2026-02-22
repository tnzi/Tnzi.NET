namespace Tnzi.SignalR.Services;

/// <summary>
/// SignalR消息推送服务接口
/// 用于向客户端推送消息的通用接口
/// </summary>
public interface IMessagePushService
{
    /// <summary>
    /// 向指定用户推送消息
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="methodName">Hub方法名</param>
    /// <param name="args">参数</param>
    /// <returns>任务</returns>
    Task PushToUserAsync(Guid userId, string methodName, params object[] args);

    /// <summary>
    /// 向多个用户推送消息
    /// </summary>
    /// <param name="userIds">用户ID集合</param>
    /// <param name="methodName">Hub方法名</param>
    /// <param name="args">参数</param>
    /// <returns>任务</returns>
    Task PushToUsersAsync(IEnumerable<Guid> userIds, string methodName, params object[] args);

    /// <summary>
    /// 向指定组推送消息
    /// </summary>
    /// <param name="groupName">组名</param>
    /// <param name="methodName">Hub方法名</param>
    /// <param name="args">参数</param>
    /// <returns>任务</returns>
    Task PushToGroupAsync(string groupName, string methodName, params object[] args);

    /// <summary>
    /// 向所有连接的客户端推送消息
    /// </summary>
    /// <param name="methodName">Hub方法名</param>
    /// <param name="args">参数</param>
    /// <returns>任务</returns>
    Task PushToAllAsync(string methodName, params object[] args);
}

/// <summary>
/// 泛型消息推送服务接口
/// 用于特定 Hub 类型的消息推送
/// </summary>
/// <typeparam name="THub">Hub 类型</typeparam>
public interface IMessagePushService<THub> : IMessagePushService
    where THub : Hub
{
}


