namespace Tnzi.Chat.Events.Handlers;

/// <summary>
/// Chat SignalR 推送事件处理器
/// </summary>
public class ChatSignalREventHandler :
    IEventHandler<MessageSentEvent>,
    IEventHandler<MessageReadEvent>,
    IEventHandler<MessageRepliedEvent>
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

    public async Task HandleAsync(MessageSentEvent @event, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_messagePush == null || @event.RecipientUserIds.Count == 0) return;

            var payload = new
            {
                messageId = @event.MessageId,
                senderId = @event.SenderId,
                messageType = (int)@event.MessageType,
                title = @event.Title
            };
            await _messagePush.PushToUsersAsync(@event.RecipientUserIds, "Chat.NewMessage", payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR push failed for MessageSent {MessageId}", @event.MessageId);
        }
    }

    public async Task HandleAsync(MessageReadEvent @event, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_messagePush == null || @event.SenderId == @event.UserId) return;

            await _messagePush.PushToUserAsync(@event.SenderId, "Chat.MessageRead",
                new { messageId = @event.MessageId, userId = @event.UserId });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR push failed for MessageRead {MessageId}", @event.MessageId);
        }
    }

    public async Task HandleAsync(MessageRepliedEvent @event, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_messagePush == null || @event.NotifyUserIds.Count == 0) return;

            var payload = new
            {
                replyId = @event.ReplyId,
                messageId = @event.MessageId,
                userId = @event.UserId,
                content = @event.Content.Length > 200 ? @event.Content[..200] + "..." : @event.Content
            };
            await _messagePush.PushToUsersAsync(@event.NotifyUserIds, "Chat.NewReply", payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR push failed for MessageReplied {ReplyId}", @event.ReplyId);
        }
    }
}
