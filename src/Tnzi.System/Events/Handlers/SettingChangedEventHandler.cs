namespace Tnzi.System.Events.Handlers;

/// <summary>
/// 监听 SettingChangedEvent，触发 SettingConfigurationProvider 重新加载，
/// 让 IOptionsMonitor 订阅者 (BackgroundService 等) 拿到最新值。
///
/// SettingConfigurationSource 可能未注册（应用没接 AddTnziSettings），handler 此时 no-op。
/// </summary>
public class SettingChangedEventHandler : IEventHandler<SettingChangedEvent>
{
    private readonly SettingConfigurationSource? _source;
    private readonly ILogger<SettingChangedEventHandler> _logger;

    public SettingChangedEventHandler(
        ILogger<SettingChangedEventHandler> logger,
        SettingConfigurationSource? source = null)
    {
        _logger = Check.NotNull(logger);
        _source = source;
    }

    public async Task HandleAsync(SettingChangedEvent @event, CancellationToken cancellationToken = default)
    {
        if (_source == null)
            return;

        // 仅对 Global 作用域感兴趣 — IConfiguration 不区分租户/用户，租户级/用户级配置走 ISettingService 单条读取。
        if (@event.Scope != SettingScope.Global)
            return;

        try
        {
            await _source.ReloadAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reload SettingConfigurationProvider after change of '{Key}'", @event.Key);
        }
    }
}
