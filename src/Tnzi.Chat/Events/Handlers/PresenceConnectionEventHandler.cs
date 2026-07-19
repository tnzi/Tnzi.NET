namespace Tnzi.Chat.Events.Handlers;

/// <summary>
/// 订阅框架连接事件，驱动 presence 自动在线/离线推送。
/// 刚上线（TotalConnections==1）→ 广播；最后连接断开（WentOffline）→ 写 LastSeenAt 后广播。
/// 异常契约：持久化副作用（MarkOfflineAsync 写 LastSeenAt）让异常冒泡给总线做隔离/重试/DLQ；
/// 仅 realtime 推送 BroadcastAsync 按"丢弃即正确"包 try/catch 记 Warning。此前的空 catch 会把
/// 掉线落库失败也静默吞掉。
/// </summary>
/// <remarks>
/// Marked [BackgroundEventHandler] for multi-tenant correctness. The event bus only
/// restores the captured <c>event.TenantId</c> into the ambient <see cref="ICurrentTenant"/>
/// for BACKGROUND handlers (see <c>LocalEventBus.PublishAsync</c>); for synchronous handlers
/// it merely stamps <c>event.TenantId</c> without calling <c>ICurrentTenant.Change()</c>.
/// Running here as a background handler ensures <c>MarkOfflineAsync</c>'s tracked upsert writes
/// to the user's tenant partition rather than the <c>TenantId == null</c> partition.
///
/// Residual disconnect-time caveat: tenant restoration only works if <c>event.TenantId</c> was
/// captured at publish time. On <c>OnDisconnectedAsync</c> the resolution depends on the JWT
/// tenant claim still being reachable via the connection principal (IHttpContextAccessor); if it
/// is not, the captured TenantId is null and the offline upsert still targets the null partition.
/// The connect path (where the request claims are present) is always fixed by this attribute.
/// Background execution also isolates the presence DB writes from the connection-scope DbContext.
/// </remarks>
[BackgroundEventHandler]
public class PresenceConnectionEventHandler
    : IEventHandler<UserConnectedEvent>, IEventHandler<UserDisconnectedEvent>
{
    private readonly IPresenceService? _presence;
    private readonly ILogger<PresenceConnectionEventHandler> _logger;

    public PresenceConnectionEventHandler(
        ILogger<PresenceConnectionEventHandler> logger,
        IPresenceService? presence = null)
    {
        _logger = Check.NotNull(logger);
        _presence = presence;
    }

    public async Task HandleAsync(UserConnectedEvent @event, CancellationToken cancellationToken = default)
    {
        if (_presence == null) return;
        if (@event.TotalConnections != 1) return; // 仅从离线变上线时广播

        // realtime 推送失败即丢弃（重放上线通知无意义），只记 Warning。
        try
        {
            await _presence.BroadcastAsync(@event.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Presence online broadcast failed for {UserId}", @event.UserId);
        }
    }

    public async Task HandleAsync(UserDisconnectedEvent @event, CancellationToken cancellationToken = default)
    {
        if (_presence == null) return;
        if (!@event.WentOffline) return;

        // 持久化副作用：写 LastSeenAt 失败必须冒泡（后台分发已 LogError，并走重试/DLQ），不吞。
        await _presence.MarkOfflineAsync(@event.UserId);

        // realtime 推送失败即丢弃，只记 Warning。
        try
        {
            await _presence.BroadcastAsync(@event.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Presence offline broadcast failed for {UserId}", @event.UserId);
        }
    }
}
