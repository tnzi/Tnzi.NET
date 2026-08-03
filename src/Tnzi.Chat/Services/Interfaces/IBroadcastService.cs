namespace Tnzi.Chat.Services;

/// <summary>
/// 系统通知发送服务。业务模块可注入本接口，在处理业务时给指定用户或角色发送系统通知
/// （落地到接收者的「系统通知」会话，不依赖当前登录用户，可在后台任务/事件处理器中调用）。
/// </summary>
public interface IBroadcastService
{
    /// <summary>给指定用户发送纯文本系统通知。</summary>
    Task<Result<int>> BroadcastToUsersAsync(IEnumerable<Guid> userIds, string content);

    /// <summary>给某角色的全部用户发送纯文本系统通知（需 Identity 模块解析角色成员）。</summary>
    Task<Result<int>> BroadcastToRoleAsync(Guid roleId, string content);

    /// <summary>管理端广播（All/Roles/Users 组合），记录广播历史。</summary>
    Task<Result<int>> BroadcastAsync(BroadcastDto input);

    /// <summary>给指定用户发送富系统通知（标题/链接/分类 + 审计来源）。</summary>
    Task<Result<int>> NotifyUsersAsync(IEnumerable<Guid> userIds, ChatNotification notification);

    /// <summary>给某角色的全部用户发送富系统通知（需 Identity 模块解析角色成员）。</summary>
    Task<Result<int>> NotifyRoleAsync(Guid roleId, ChatNotification notification);
}
