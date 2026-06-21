namespace Tnzi.Chat.Services.Interfaces;

public interface IGroupService
{
    Task<Result<ConversationDto>> CreateGroupAsync(CreateGroupDto input);
    Task<Result> AddMembersAsync(Guid conversationId, IEnumerable<Guid> userIds);
    Task<Result> RemoveMemberAsync(Guid conversationId, Guid userId);
    Task<Result> RenameGroupAsync(Guid conversationId, string title);
    Task<Result> DissolveGroupAsync(Guid conversationId);
    Task<Result> LeaveAsync(Guid conversationId);
    Task<Result> UpdateNoticeAsync(Guid conversationId, string? notice);
}
