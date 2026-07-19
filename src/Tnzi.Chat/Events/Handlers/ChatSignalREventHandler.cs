namespace Tnzi.Chat.Events.Handlers;

public class ChatSignalREventHandler :
    IEventHandler<ConversationMessageSentEvent>,
    IEventHandler<ConversationReadEvent>,
    IEventHandler<ConversationChangedEvent>
{
    private readonly IMessagePushService? _messagePush;
    private readonly ILogger<ChatSignalREventHandler> _logger;

    public ChatSignalREventHandler(
        ILogger<ChatSignalREventHandler> logger,
        IMessagePushService? messagePush = null)
    {
        _logger = Check.NotNull(logger);
        _messagePush = messagePush;
    }

    // 注意（realtime fire-and-forget 例外）：这些是 SignalR 实时推送，推送失败应"丢弃即正确"
    // （重放过期推送反而有害），故只把副作用调用 PushToUsersAsync 包进 try/catch 并记 Warning，
    // 而非整个方法体。非推送的持久化副作用绝不适用此例外，须让异常冒泡给总线。见 events.md。

    public async Task HandleAsync(ConversationMessageSentEvent @event, CancellationToken cancellationToken = default)
    {
        if (_messagePush == null || @event.RecipientUserIds.Count == 0) return;
        var payload = new
        {
            conversationId = @event.ConversationId,
            messageId = @event.MessageId,
            senderId = @event.SenderId,
            contentType = (int)@event.ContentType,
            preview = @event.Preview,
            message = @event.Message
        };
        try
        {
            await _messagePush.PushToUsersAsync(@event.RecipientUserIds, "Chat.NewMessage", payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR push failed for ConversationMessageSent {MessageId}", @event.MessageId);
        }
    }

    public async Task HandleAsync(ConversationReadEvent @event, CancellationToken cancellationToken = default)
    {
        if (_messagePush == null || @event.OtherMemberIds.Count == 0) return;
        var payload = new { conversationId = @event.ConversationId, userId = @event.UserId, readAt = @event.ReadAt };
        try
        {
            await _messagePush.PushToUsersAsync(@event.OtherMemberIds, "Chat.MessageRead", payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR push failed for ConversationRead {ConversationId}", @event.ConversationId);
        }
    }

    public async Task HandleAsync(ConversationChangedEvent @event, CancellationToken cancellationToken = default)
    {
        if (_messagePush == null || @event.AffectedUserIds.Count == 0) return;
        var payload = new { conversationId = @event.ConversationId, changeType = (int)@event.ChangeType };
        try
        {
            await _messagePush.PushToUsersAsync(@event.AffectedUserIds, "Chat.ConversationChanged", payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR push failed for ConversationChanged {ConversationId}", @event.ConversationId);
        }
    }
}
