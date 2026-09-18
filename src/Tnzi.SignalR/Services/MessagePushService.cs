namespace Tnzi.SignalR.Services;

/// <summary>
/// SignalR消息推送服务实现 (泛型版本)
/// 直接实现接口，不继承 ApplicationService（不需要 CurrentUser、EventBus 等功能）
/// </summary>
/// <remarks>
/// <para>
/// ★★★ <b>按用户推送走 SignalR 组，不走 <see cref="IConnectionManager"/>。</b>
/// 此前的实现先到进程内的连接登记表查该用户的连接 id，查不到就静默返回。
/// 登记表是纯内存的（Redis Backplane 转发的是消息，不是登记），于是多实例部署下
/// 连在别的实例上的用户永远查不到 —— Chat 的每一条点对点实时推送有 (N-1)/N 的概率一条不发，
/// 而 REST 已落库、界面显示已发送、日志零 Warning。
/// </para>
/// <para>
/// <c>TnziHub</c> 在连接建立时把每条已认证连接加进
/// <see cref="HubGroupNames.ForUser"/> 组，组由 SignalR 维护并经 backplane 转发；
/// 投递到一个空组是 no-op，所以「用户不在线」的失效方向是多发一次给空组，绝不是一条不发。
/// 代价是按用户推送只对继承 <c>TnziHub</c> 的 Hub 成立 —— 与此前经登记表寻址
/// 时的前提逐字相同（登记也只在那个基类里发生）。
/// </para>
/// </remarks>
/// <typeparam name="THub">Hub 类型</typeparam>
public class MessagePushService<THub> : IMessagePushService<THub>
    where THub : Hub
{
    private readonly IHubContext<THub> _hubContext;

    /// <summary>
    /// 初始化一个<see cref="MessagePushService{THub}"/>类型的新实例
    /// </summary>
    public MessagePushService(IHubContext<THub> hubContext)
    {
        _hubContext = Check.NotNull(hubContext);
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
        await _hubContext.Clients
            .Group(HubGroupNames.ForUser(userId))
            .SendCoreAsync(methodName, args ?? Array.Empty<object>(), default);
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

        // 去重：同一个用户出现两次不该收到两份。
        var groupNames = userIds.Distinct().Select(HubGroupNames.ForUser).ToList();
        if (groupNames.Count == 0)
        {
            return;
        }

        await _hubContext.Clients
            .Groups(groupNames)
            .SendCoreAsync(methodName, args ?? Array.Empty<object>(), default);
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
