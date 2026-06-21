namespace Tnzi.Chat.Services.Interfaces;

public interface IConversationService
{
    // --- Task 6 (A) ---
    Task<Result<ConversationDto>> GetOrCreateDirectAsync(Guid otherUserId);
    Task<Result<ChatMessageDto>> SendMessageAsync(Guid conversationId, SendMessageDto input);
    Task<Result<MessageThreadDto>> GetMessagesAsync(Guid conversationId, MessageThreadQueryDto query);

    // --- Task 7 (B) ---
    Task<Result<IReadOnlyList<ConversationListItemDto>>> GetMyConversationsAsync();
    Task<Result<ConversationDto>> GetByIdAsync(Guid conversationId);
    Task<Result<int>> GetTotalUnreadAsync();
    Task<Result> MarkReadAsync(Guid conversationId);
    Task<Result> MuteAsync(Guid conversationId, bool muted);
    Task<Result> DeleteMessageAsync(Guid messageId);

    // --- Task 7 (C) ---
    Task<Result> UpdateMemberSettingsAsync(Guid conversationId, ConversationMemberSettingsDto settings);
    Task<Result> ClearHistoryAsync(Guid conversationId);
    Task<Result<MessageThreadDto>> SearchMessagesAsync(Guid conversationId, string keyword, MessageThreadQueryDto query);
}
