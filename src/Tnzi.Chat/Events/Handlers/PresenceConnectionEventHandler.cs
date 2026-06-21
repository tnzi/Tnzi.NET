namespace Tnzi.Chat.Events.Handlers;

/// <summary>
/// 订阅框架连接事件，驱动 presence 自动在线/离线推送。
/// 刚上线（TotalConnections==1）→ 广播；最后连接断开（WentOffline）→ 写 LastSeenAt 后广播。
/// 处理器静默 catch，不影响连接生命周期。
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

    public PresenceConnectionEventHandler(IPresenceService? presence = null)
    {
        _presence = presence;
    }

    public async Task HandleAsync(UserConnectedEvent @event, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_presence == null) return;
            if (@event.TotalConnections == 1) // 从离线变上线
                await _presence.BroadcastAsync(@event.UserId);
        }
        catch { /* 辅助操作，静默 */ }
    }

    public async Task HandleAsync(UserDisconnectedEvent @event, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_presence == null) return;
            if (@event.WentOffline)
            {
                await _presence.MarkOfflineAsync(@event.UserId);
                await _presence.BroadcastAsync(@event.UserId);
            }
        }
        catch { /* 辅助操作，静默 */ }
    }
}
