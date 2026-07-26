namespace Tnzi.System.Events.Handlers;

/// <summary>
/// 监听 SettingChangedEvent，做两件事：
/// 1) 触发 SettingConfigurationProvider 重新加载，让 IOptionsMonitor 订阅者拿到最新值；
/// 2) 经 SignalR 向所有已连接客户端广播变更的 key，让在线会话热刷新受影响的配置
///    （chat 配置 / 全局主题等），免手动刷新页面。
///
/// SettingConfigurationSource 可能未注册（应用没接 AddTnziSettings）、SignalR 可能未加载，
/// 两个依赖都是可选注入，缺失时对应分支静默跳过。
/// </summary>
public class SettingChangedEventHandler : IEventHandler<SettingChangedEvent>
{
    private readonly SettingConfigurationSource? _source;
    private readonly IMessagePushService<SettingsRealtimeHub>? _push;
    private readonly ILogger<SettingChangedEventHandler> _logger;

    public SettingChangedEventHandler(
        ILogger<SettingChangedEventHandler> logger,
        SettingConfigurationSource? source = null,
        IMessagePushService<SettingsRealtimeHub>? push = null)
    {
        _logger = Check.NotNull(logger);
        _source = source;
        _push = push;
    }

    public async Task HandleAsync(SettingChangedEvent @event, CancellationToken cancellationToken = default)
    {
        // 仅对 Global 作用域感兴趣 - IConfiguration 与实时广播都只反映部署级全局配置；
        // 租户/用户级配置走 ISettingService 单条读取，不进 IConfiguration 也不广播。
        if (@event.Scope != SettingScope.Global)
            return;

        // 1) 触发 IConfiguration 重载 → IOptionsMonitor 订阅者拿最新值（仅接了 AddTnziSettings 时）。
        // 不吞异常：重载失败应冒泡给事件总线，由其错误隔离 + 重试 + DLQ 兜底。
        if (_source != null)
        {
            _logger.LogDebug("Reloading configuration source after change of '{Key}'", @event.Key);
            await _source.ReloadAsync(cancellationToken);
        }

        // 2) 向所有已连接客户端广播变更的 key（仅 key，不含值），让在线会话热刷新对应配置。
        // 未加载 SignalR 时 _push 为 null，静默跳过。
        if (_push != null)
        {
            await _push.PushToAllAsync("Settings.Changed", new { key = @event.Key, isRemoval = @event.IsRemoval });
        }
    }
}
