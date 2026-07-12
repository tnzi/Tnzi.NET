namespace Tnzi.SignalR.Hubs;

/// <summary>
/// SignalR 客户端接口基类
/// 应用可通过继承此接口定义自己的客户端方法
/// </summary>
public interface ITnziHubClient
{
    /// <summary>
    /// 接收系统通知
    /// </summary>
    /// <param name="message">通知消息</param>
    /// <returns>任务</returns>
    Task ReceiveSystemNotification(string message);

    /// <summary>
    /// 用户在线状态变更
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="isOnline">是否在线</param>
    /// <returns>任务</returns>
    Task OnUserOnlineStatusChanged(Guid userId, bool isOnline);

    /// <summary>
    /// 接收通用消息
    /// </summary>
    /// <param name="methodName">方法名</param>
    /// <param name="args">参数</param>
    /// <returns>任务</returns>
    Task ReceiveMessage(string methodName, object[] args);
}
