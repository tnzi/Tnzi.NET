namespace Tnzi.Chat.Services.Internal;

/// <summary>
/// 撤回一条消息：本人撤回（<see cref="ConversationService.DeleteMessageAsync"/>）与管理端强制撤回
/// （<see cref="ChatAdminService.DeleteMessageAsync"/>）共用的一段。
/// </summary>
/// <remarks>
/// <para>
/// 软删消息之外还有两件事，少一件撤回就只撤了一半：
/// </para>
/// <list type="bullet">
/// <item>撤回的是会话最后一条消息时，会话的 <see cref="Conversation.LastMessagePreview"/> 还是它的前 100 个字符，
/// 每个成员的会话列表照样显示：群里贴错的密码、管理员强制撤回的骚扰内容都还在那里。
/// 换成 <see cref="RecalledPreview"/>；<c>LastMessageAt</c> 不动，免得会话在列表里往下跳。</item>
/// <item>开着这个会话的成员不会收到任何通知，消息一直留在屏幕上。返回的事件经 SignalR 推给全部在册成员。</item>
/// </list>
/// <para>未读数不回退：撤回之前它确实到过，收件人打开会话即清零。</para>
/// </remarks>
internal static class ChatMessageRecall
{
    /// <summary>撤回的是会话最后一条消息时，列表预览换成的标记。</summary>
    public const string RecalledPreview = "[Message recalled]";

    /// <summary>
    /// 软删消息，必要时改写会话预览，并返回要发布的撤回事件。调用方负责工作单元与发布。
    /// </summary>
    public static async Task<ConversationMessageRecalledEvent> RecallAsync(
        ChatMessage message,
        IRepository<ChatMessage, Guid> messages,
        IRepository<Conversation, Guid> conversations,
        IRepository<ConversationMember, Guid> members,
        CancellationToken cancellationToken)
    {
        Check.NotNull(message);

        await messages.DeleteAsync(message, cancellationToken); // 软删（FullAudited）

        var conversationId = message.ConversationId;
        var messageId = message.Id;
        var sentAt = message.SentAt;

        // 还有比它新的消息时，预览本来就是那一条的，不动。
        var hasNewer = await messages.AsQueryable()
            .AnyAsync(m => m.ConversationId == conversationId && m.Id != messageId && m.SentAt > sentAt, cancellationToken);

        string? preview = null;
        if (!hasNewer)
        {
            preview = RecalledPreview;

            // 只改这一列：整行写回会把并发的改名、成员数变化、解散撤销。裸 SQL 先进工作单元的事务。
            await conversations.EnsureTransactionStartedAsync(cancellationToken);
            await conversations.AsQueryable()
                .Where(c => c.Id == conversationId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastMessagePreview, preview), cancellationToken);
        }

        var recipients = await members.AsQueryable()
            .Where(m => m.ConversationId == conversationId && m.RemovedAt == null)
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);

        return new ConversationMessageRecalledEvent
        {
            ConversationId = conversationId,
            MessageId = messageId,
            Preview = preview,
            RecipientUserIds = recipients
        };
    }
}
