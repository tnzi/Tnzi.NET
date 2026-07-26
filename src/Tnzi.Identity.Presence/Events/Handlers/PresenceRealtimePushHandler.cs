namespace Tnzi.Identity.Presence.Events.Handlers;

/// <summary>
/// 通用实时推送：把 <see cref="UserPresenceChangedEvent"/> 通过 <c>/hubs/presence</c> 广播给所有已认证连接
/// （<c>Presence.Changed</c>）。开放目录模型下 presence 对任意登录用户可读，故全量广播不构成隐私回退。
/// 客户端本地按关心的 userId 过滤。仅在加载了 SignalR 时注册。
/// </summary>
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
            await _push.PushToAllAsync(PresenceChangedMethod, payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Presence realtime push failed for {UserId}", @event.UserId);
        }
    }
}
