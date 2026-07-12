namespace Tnzi.System.Events.Handlers;

/// <summary>
/// 处理其他实例广播来的配置变更（多实例一致性收端）：
/// 1) 清本实例的按键缓存（MemoryCache 每实例独立；Redis 场景重复清除无害）；
/// 2) 复用 <see cref="SettingChangedEventHandler"/> 的本地逻辑 reload IConfiguration +
///    向连在本实例的 SignalR 客户端广播。
/// 发布实例自己收到回环投递时按 OriginInstanceId 跳过（本地链已处理）。
/// </summary>
public class SettingChangedDistributedEventHandler : IEventHandler<SettingChangedIntegrationEvent>
{
    private readonly SettingChangedEventHandler _localHandler;
    private readonly ICache _cache;
    private readonly ILogger<SettingChangedDistributedEventHandler> _logger;

    public SettingChangedDistributedEventHandler(
        SettingChangedEventHandler localHandler,
        ICache cache,
        ILogger<SettingChangedDistributedEventHandler> logger)
    {
        _localHandler = Check.NotNull(localHandler);
        _cache = Check.NotNull(cache);
        _logger = Check.NotNull(logger);
    }

    public async Task HandleAsync(SettingChangedIntegrationEvent @event, CancellationToken cancellationToken = default)
    {
        if (@event.OriginInstanceId == SettingChangedIntegrationEvent.LocalInstanceId)
            return;

        _logger.LogDebug("Applying distributed setting change for '{Key}' from instance {Origin}", @event.Key, @event.OriginInstanceId);

        await _cache.RemoveAsync($"Setting:{@event.Key}");

        // 本地 handler 只消费 Key/Scope/IsRemoval（NewValue 不参与 reload/广播），可安全复用。
        await _localHandler.HandleAsync(new SettingChangedEvent
        {
            Key = @event.Key,
            Scope = @event.Scope,
            ScopeId = @event.ScopeId,
            NewValue = null,
            IsRemoval = @event.IsRemoval
        }, cancellationToken);
    }
}
