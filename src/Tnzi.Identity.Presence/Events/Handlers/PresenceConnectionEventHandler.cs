namespace Tnzi.Identity.Presence.Events.Handlers;

/// <summary>
/// 订阅框架连接事件，驱动 presence 自动上/下线。
/// 刚上线（TotalConnections==1）→ 清 auto-away/记活动并广播；最后连接断开（WentOffline）→ 写 LastSeenAt 后广播。
/// 异常契约：持久化副作用（MarkActive/MarkOffline）让异常冒泡给总线做隔离/重试/DLQ；
/// 仅 realtime 推送 NotifyChanged 按"丢弃即正确"包 try/catch 记 Warning。
/// </summary>
/// <remarks>
/// Marked [BackgroundEventHandler] for multi-tenant correctness. The event bus only restores the
/// captured <c>event.TenantId</c> into the ambient <see cref="ICurrentTenant"/> for BACKGROUND
/// handlers; running here as a background handler ensures the presence upsert writes to the user's
/// tenant partition. Background execution also isolates the presence DB writes from the
/// connection-scope DbContext.
/// </remarks>
[BackgroundEventHandler]
public class PresenceConnectionEventHandler
    : IEventHandler<UserConnectedEvent>, IEventHandler<UserDisconnectedEvent>
{
    private readonly IPresenceService _presence;
    private readonly ILogger<PresenceConnectionEventHandler> _logger;

    public PresenceConnectionEventHandler(
        ILogger<PresenceConnectionEventHandler> logger,
        IPresenceService presence)
    {
        _logger = Check.NotNull(logger);
        _presence = Check.NotNull(presence);
    }

    public async Task HandleAsync(UserConnectedEvent @event, CancellationToken cancellationToken = default)
    {
        if (@event.TotalConnections != 1) return; // 仅从离线变上线时处理

        // 持久化副作用：上线即清空闲标记 + 记活动，失败必须冒泡（后台分发已 LogError 并走重试/DLQ）。
        await _presence.MarkActiveAsync(@event.UserId);

        // realtime 推送失败即丢弃（重放上线通知无意义），只记 Warning。
        try
        {
            await _presence.NotifyChangedAsync(@event.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Presence online broadcast failed for {UserId}", @event.UserId);
        }
    }

    public async Task HandleAsync(UserDisconnectedEvent @event, CancellationToken cancellationToken = default)
    {
        if (!@event.WentOffline) return;

        // 持久化副作用：写 LastSeenAt 失败必须冒泡，不吞。
        await _presence.MarkOfflineAsync(@event.UserId);

        try
        {
            await _presence.NotifyChangedAsync(@event.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Presence offline broadcast failed for {UserId}", @event.UserId);
        }
    }
}
