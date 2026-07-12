namespace Tnzi.System.Hubs;

/// <summary>
/// 设置实时广播 Hub（连接目标，无自定义方法）。
///
/// 全局作用域配置变更时，<see cref="Events.Handlers.SettingChangedEventHandler"/> 经
/// <c>IMessagePushService&lt;SettingsRealtimeHub&gt;</c> 向所有已连接客户端推送
/// <c>Settings.Changed</c>（载荷仅 <c>{ key, isRemoval }</c>，不含配置值/机密）。
/// 客户端据 key 前缀热刷新对应配置（如 <c>Chat:*</c> → 重拉聊天配置，
/// <c>Appearance:AdminTheme</c> → 重载全局主题），免手动刷新页面。
///
/// 仅在 SignalR 模块加载时映射到 <c>/hubs/settings</c>（<c>[Authorize]</c> 保护，
/// WebSocket 走 <c>access_token</c> query）。
/// </summary>
[Authorize]
public class SettingsRealtimeHub : TnziHub
{
}
