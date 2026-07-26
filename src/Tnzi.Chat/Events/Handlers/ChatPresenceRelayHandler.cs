namespace Tnzi.Chat.Events.Handlers;

/// <summary>
/// 订阅 Presence 模块的 <see cref="UserPresenceChangedEvent"/>，把某用户的在线状态变化推给
/// "共享非 System 会话"的联系人（<c>Chat.PresenceChanged</c>）。原 <c>PresenceService.BroadcastAsync</c>
/// 的 chat 专属扇出逻辑迁移至此——Presence 机制与 Chat 解耦后，Presence 只负责发事件，Chat 负责
/// 按自己的会话联系人图投递。
/// </summary>
public class ChatPresenceRelayHandler : IEventHandler<UserPresenceChangedEvent>
{
    public const string PresenceChangedMethod = "Chat.PresenceChanged";

    private readonly IRepository<ConversationMember, Guid> _memberRepository;
    private readonly IRepository<Conversation, Guid> _conversationRepository;
    private readonly IMessagePushService? _push;
    private readonly ILogger<ChatPresenceRelayHandler> _logger;

    public ChatPresenceRelayHandler(
        ILogger<ChatPresenceRelayHandler> logger,
        IRepository<ConversationMember, Guid> memberRepository,
        IRepository<Conversation, Guid> conversationRepository,
        IMessagePushService? push = null)
    {
        _logger = Check.NotNull(logger);
        _memberRepository = Check.NotNull(memberRepository);
        _conversationRepository = Check.NotNull(conversationRepository);
        _push = push;
    }

    public async Task HandleAsync(UserPresenceChangedEvent @event, CancellationToken cancellationToken = default)
    {
        if (_push == null) return; // SignalR 未加载 → 无实时

        var userId = @event.UserId;

        // 联系人 = 与该用户共享非 System 会话的其他成员（distinct）。
        var myConvIds = (await _memberRepository.ToListAsync(m => m.UserId == userId && m.RemovedAt == null, cancellationToken))
            .Select(m => m.ConversationId).ToHashSet();
        if (myConvIds.Count == 0) return;

        // 仅向"共享非 System 会话"的联系人推送（System 会话是每用户独立通知会话，不应外溢 presence）。
        var nonSystemConvIds = (await _conversationRepository.ToListAsync(
                c => myConvIds.Contains(c.Id) && c.Type != ConversationType.System, cancellationToken))
            .Select(c => c.Id).ToHashSet();
        if (nonSystemConvIds.Count == 0) return;

        var contacts = (await _memberRepository.ToListAsync(
                m => nonSystemConvIds.Contains(m.ConversationId) && m.UserId != userId && m.RemovedAt == null, cancellationToken))
            .Select(m => m.UserId).Distinct().ToList();
        if (contacts.Count == 0) return;

        var payload = new UserPresenceDto { UserId = userId, Status = @event.Status, LastSeenAt = @event.LastSeenAt };

        // realtime 推送失败即丢弃（重放过期 presence 无意义），只把 push 调用包 try/catch 记 Warning
        // （与 ChatSignalREventHandler 一致；非推送的 DB 读取失败让异常冒泡给总线做隔离/重试）。
        try
        {
            await _push.PushToUsersAsync(contacts, PresenceChangedMethod, payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chat presence relay push failed for {UserId}", userId);
        }
    }
}
