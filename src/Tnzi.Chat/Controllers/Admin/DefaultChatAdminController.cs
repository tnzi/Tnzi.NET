using Tnzi.Data;

namespace Tnzi.Chat.Controllers.Admin;

/// <summary>
/// Chat 系统级管理控制器：广播（系统通知）+ 会话/消息/在线状态的全局查询与治理。
/// </summary>
[DefaultController]
[Route("admin/chat")]
[ApiExplorerSettings(GroupName = "admin")]
public class DefaultChatAdminController : ApiAdminControllerBase
{
    protected readonly IBroadcastService Broadcast;
    protected readonly IChatAdminService ChatAdmin;

    public DefaultChatAdminController(IBroadcastService broadcast, IChatAdminService chatAdmin)
    {
        Broadcast = Check.NotNull(broadcast);
        ChatAdmin = Check.NotNull(chatAdmin);
    }

    /// <summary>Broadcast a system notification to roles and/or users.</summary>
    [HttpPost("broadcast")]
    public virtual async Task<ApiResult<int>> SendBroadcast([FromBody] BroadcastDto input)
        => (await Broadcast.BroadcastAsync(input)).ToApiResult();

    /// <summary>Paged broadcast history (most recent first).</summary>
    /// <remarks>
    /// Binds scalar <c>pageIndex</c>/<c>pageSize</c> rather than <c>[FromQuery] PagedQueryDto</c>:
    /// PagedQueryDto carries a recursive <c>FilterGroup? Filter</c> which, when bound from query,
    /// makes the MVC model-metadata walk recurse and abort controller discovery (all routes 404).
    /// </remarks>
    [HttpGet("broadcasts")]
    public virtual async Task<ApiResult<IPagedList<BroadcastLogDto>>> GetBroadcasts([FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20)
        => (await ChatAdmin.GetBroadcastsAsync(new PagedQueryDto { PageIndex = pageIndex, PageSize = pageSize })).ToApiResult();

    /// <summary>Global chat statistics overview (conversations / messages / members / online).</summary>
    [HttpGet("statistics")]
    public virtual async Task<ApiResult<ChatStatisticsDto>> GetStatistics()
        => (await ChatAdmin.GetStatisticsAsync()).ToApiResult();

    /// <summary>Paged listing of all conversations (filter by type / keyword / participant).</summary>
    [HttpPost("conversations/query")]
    public virtual async Task<ApiResult<IPagedList<AdminConversationListItemDto>>> QueryConversations([FromBody] AdminConversationQueryDto query)
        => (await ChatAdmin.GetConversationsAsync(query)).ToApiResult();

    /// <summary>Conversation detail (members + metadata).</summary>
    [HttpGet("conversations/{id}")]
    public virtual async Task<ApiResult<AdminConversationDetailDto>> GetConversation(Guid id)
        => (await ChatAdmin.GetConversationDetailAsync(id)).ToApiResult();

    /// <summary>Read a conversation's messages (admin view, cursor paging, no membership gate).</summary>
    [HttpGet("conversations/{id}/messages")]
    public virtual async Task<ApiResult<MessageThreadDto>> GetConversationMessages(Guid id, [FromQuery] MessageThreadQueryDto query)
        => (await ChatAdmin.GetConversationMessagesAsync(id, query)).ToApiResult();

    /// <summary>Delete (dissolve) a conversation.</summary>
    [HttpDelete("conversations/{id}")]
    public virtual async Task<ApiResult> DeleteConversation(Guid id)
        => (await ChatAdmin.DeleteConversationAsync(id)).ToApiResult();

    /// <summary>Force-recall any message (admin override).</summary>
    [HttpDelete("messages/{messageId}")]
    public virtual async Task<ApiResult> DeleteMessage(Guid messageId)
        => (await ChatAdmin.DeleteMessageAsync(messageId)).ToApiResult();

    /// <summary>User presence overview (effective online distribution + per-user detail).</summary>
    [HttpGet("presence")]
    public virtual async Task<ApiResult<PresenceOverviewDto>> GetPresenceOverview([FromQuery] PresenceOverviewQueryDto query)
        => (await ChatAdmin.GetPresenceOverviewAsync(query)).ToApiResult();
}
