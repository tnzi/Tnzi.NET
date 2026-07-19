namespace Tnzi.Chat.Controllers;

[DefaultController]
[ApiAuthorize]
[ServiceFilter(typeof(ChatAccessGuardFilter))]
[Route("conversations")]
[ApiExplorerSettings(GroupName = "user")]
public class DefaultConversationController : ApiControllerBase
{
    protected readonly IConversationService Conversations;
    protected readonly IGroupService Groups;

    public DefaultConversationController(IConversationService conversations, IGroupService groups)
    {
        Conversations = Check.NotNull(conversations);
        Groups = Check.NotNull(groups);
    }

    [HttpGet]
    public virtual async Task<ApiResult<IReadOnlyList<ConversationListItemDto>>> GetMine()
        => (await Conversations.GetMyConversationsAsync()).ToApiResult();

    [HttpGet("unread-count")]
    public virtual async Task<ApiResult<int>> GetUnreadCount()
        => (await Conversations.GetTotalUnreadAsync()).ToApiResult();

    [HttpPost("direct")]
    public virtual async Task<ApiResult<ConversationDto>> StartDirect([FromBody] StartDirectDto input)
        => (await Conversations.GetOrCreateDirectAsync(input.UserId)).ToApiResult();

    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<ConversationDto>> GetById(Guid id)
        => (await Conversations.GetByIdAsync(id)).ToApiResult();

    [HttpGet("{id:guid}/messages")]
    public virtual async Task<ApiResult<MessageThreadDto>> GetMessages(Guid id, [FromQuery] Guid? before, [FromQuery] int limit = 30)
        => (await Conversations.GetMessagesAsync(id, new MessageThreadQueryDto { Before = before, Limit = limit })).ToApiResult();

    [HttpPost("{id:guid}/messages")]
    public virtual async Task<ApiResult<ChatMessageDto>> Send(Guid id, [FromBody] SendMessageDto input)
        => (await Conversations.SendMessageAsync(id, input)).ToApiResult();

    [HttpPost("{id:guid}/read")]
    public virtual async Task<ApiResult> MarkRead(Guid id)
        => (await Conversations.MarkReadAsync(id)).ToApiResult();

    [HttpPost("{id:guid}/mute")]
    public virtual async Task<ApiResult> Mute(Guid id, [FromBody] MuteRequestDto input)
        => (await Conversations.MuteAsync(id, input.Muted)).ToApiResult();

    [HttpPut("{id:guid}/member-settings")]
    public virtual async Task<ApiResult> UpdateMemberSettings(Guid id, [FromBody] ConversationMemberSettingsDto input)
        => (await Conversations.UpdateMemberSettingsAsync(id, input)).ToApiResult();

    [HttpPost("{id:guid}/clear")]
    public virtual async Task<ApiResult> ClearHistory(Guid id)
        => (await Conversations.ClearHistoryAsync(id)).ToApiResult();

    /// <summary>Per-user delete: wipes my history view and hides the conversation from my list.</summary>
    [HttpPost("{id:guid}/delete-for-me")]
    public virtual async Task<ApiResult> DeleteForMe(Guid id)
        => (await Conversations.DeleteForMeAsync(id)).ToApiResult();

    [HttpGet("{id:guid}/messages/search")]
    public virtual async Task<ApiResult<MessageThreadDto>> SearchMessages(
        Guid id, [FromQuery] string keyword, [FromQuery] Guid? before, [FromQuery] int limit = 30)
        => (await Conversations.SearchMessagesAsync(id, keyword, new MessageThreadQueryDto { Before = before, Limit = limit })).ToApiResult();

    [HttpPut("{id:guid}/notice")]
    public virtual async Task<ApiResult> UpdateNotice(Guid id, [FromBody] UpdateNoticeDto input)
        => (await Groups.UpdateNoticeAsync(id, input.Notice)).ToApiResult();

    [HttpDelete("messages/{messageId:guid}")]
    public virtual async Task<ApiResult> DeleteMessage(Guid messageId)
        => (await Conversations.DeleteMessageAsync(messageId)).ToApiResult();

    // --- groups ---
    [HttpPost("group")]
    public virtual async Task<ApiResult<ConversationDto>> CreateGroup([FromBody] CreateGroupDto input)
        => (await Groups.CreateGroupAsync(input)).ToApiResult();

    [HttpPost("{id:guid}/members")]
    public virtual async Task<ApiResult> AddMembers(Guid id, [FromBody] AddMembersDto input)
        => (await Groups.AddMembersAsync(id, input.UserIds)).ToApiResult();

    [HttpDelete("{id:guid}/members/{userId:guid}")]
    public virtual async Task<ApiResult> RemoveMember(Guid id, Guid userId)
        => (await Groups.RemoveMemberAsync(id, userId)).ToApiResult();

    [HttpPut("{id:guid}")]
    public virtual async Task<ApiResult> Rename(Guid id, [FromBody] RenameGroupDto input)
        => (await Groups.RenameGroupAsync(id, input.Title)).ToApiResult();

    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Dissolve(Guid id)
        => (await Groups.DissolveGroupAsync(id)).ToApiResult();

    [HttpPost("{id:guid}/leave")]
    public virtual async Task<ApiResult> Leave(Guid id)
        => (await Groups.LeaveAsync(id)).ToApiResult();
}
