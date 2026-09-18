namespace Tnzi.Identity.Presence.Events.Handlers;

/// <summary>
/// 通用实时推送：把 <see cref="UserPresenceChangedEvent"/> 通过 <c>/hubs/presence</c> 推给<b>同一目录内</b>的已认证连接
/// （<c>Presence.Changed</c>）。开放目录模型下 presence 对目录内任意登录用户可读，故整目录广播不构成隐私回退；
/// 客户端本地按关心的 userId 过滤。仅在加载了 SignalR 时注册。
/// </summary>
/// <remarks>
/// ★ 「目录」在多租户下是一个租户。事件自带 <c>TenantId</c>（总线从 <c>ICurrentTenant</c> 捕获），
/// 有租户时投递到 <c>Tenant_{id}</c> 组（<c>TnziHub</c> 按连接主体的 <c>tenant_id</c> claim 加入），
/// 没有租户（多租户未开启）才 <c>Clients.All</c>。此前一律 All：租户 B 每一次上下线（userId / status / lastSeenAt）
/// 都送到租户 A 的每一个连接，与 Chat 广播刻意拒绝跨租户 All 的标准相反。没有租户 claim 的连接不在任何租户组里，
/// 按租户推送时收不到 —— 关闭方向。
/// </remarks>
public class PresenceRealtimePushHandler : IEventHandler<UserPresenceChangedEvent>
{
    public const string PresenceChangedMethod = "Presence.Changed";

    private readonly IMessagePushService<PresenceHub>? _push;
    private readonly ILogger<PresenceRealtimePushHandler> _logger;

    public PresenceRealtimePushHandler(
        ILogger<PresenceRealtimePushHandler> logger,
        IMessagePushService<PresenceHub>? push = null)
    {
        _logger = Check.NotNull(logger);
        _push = push;
    }

    public async Task HandleAsync(UserPresenceChangedEvent @event, CancellationToken cancellationToken = default)
    {
        if (_push == null) return; // SignalR 未加载 → 无实时

        var payload = new
        {
            userId = @event.UserId,
            status = @event.Status,
            lastSeenAt = @event.LastSeenAt
        };

        // realtime 推送失败即丢弃（重放过期 presence 无意义），只记 Warning。
        try
        {
            if (@event.TenantId is { } tenantId)
            {
                await _push.PushToGroupAsync(HubGroupNames.ForTenant(tenantId), PresenceChangedMethod, payload);
            }
            else
            {
                await _push.PushToAllAsync(PresenceChangedMethod, payload);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Presence realtime push failed for {UserId}", @event.UserId);
        }
    }
}
