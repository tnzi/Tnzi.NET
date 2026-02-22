namespace Tnzi.Chat.Controllers;

/// <summary>
/// 聊天控制器基类（用户端）
/// 提供消息发送、查询、更新、删除、回复、标记已读等API端点
/// </summary>
[ApiAuthorize]
[Route("messages")]
[ApiExplorerSettings(GroupName = "user")]
public abstract class ChatControllerBase : ApiControllerBase
{
    protected readonly IMessageService MessageService;
    protected readonly IMessageReplyService MessageReplyService;

    protected ChatControllerBase(IMessageService messageService, IMessageReplyService messageReplyService)
    {
        MessageService = Check.NotNull(messageService);
        MessageReplyService = Check.NotNull(messageReplyService);
    }

    /// <summary>
    /// 发送消息
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<MessageDto>> Send([FromBody] CreateMessageDto input)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await MessageService.SendAsync(userId, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取用户消息列表（收件箱）
    /// </summary>
    [HttpPost("query")]
    public virtual async Task<ApiResult<IPagedList<MessageListItemDto>>> GetInbox([FromBody] MessageQueryDto query)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await MessageService.GetUserInboxAsync(userId, query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取消息详情
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<MessageDto>> GetById(Guid id)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await MessageService.GetByIdAsync(id, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新消息（仅发送者可操作）
    /// </summary>
    [HttpPut("{id:guid}")]
    public virtual async Task<ApiResult<MessageDto>> Update(Guid id, [FromBody] UpdateMessageDto input)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await MessageService.UpdateAsync(id, userId, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除消息（仅发送者可操作，软删除）
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await MessageService.DeleteAsync(id, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 标记消息为已读
    /// </summary>
    [HttpPost("{id:guid}/read")]
    public virtual async Task<ApiResult> MarkAsRead(Guid id)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await MessageService.MarkAsReadAsync(id, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量标记全部已读
    /// </summary>
    [HttpPost("read-all")]
    public virtual async Task<ApiResult<int>> MarkAllAsRead()
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await MessageService.MarkAllAsReadAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取未读消息数量
    /// </summary>
    [HttpGet("unread-count")]
    public virtual async Task<ApiResult<int>> GetUnreadCount()
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await MessageService.GetUnreadCountAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 回复消息
    /// </summary>
    [HttpPost("replies")]
    public virtual async Task<ApiResult<MessageReplyDto>> CreateReply([FromBody] CreateMessageReplyDto input)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await MessageReplyService.CreateAsync(userId, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取消息的回复列表（树形结构）
    /// </summary>
    [HttpGet("{messageId:guid}/replies")]
    public virtual async Task<ApiResult<IEnumerable<MessageReplyDto>>> GetReplies(Guid messageId)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await MessageReplyService.GetByMessageIdAsync(messageId, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除回复（仅回复者可操作，软删除）
    /// </summary>
    [HttpDelete("replies/{replyId:guid}")]
    public virtual async Task<ApiResult> DeleteReply(Guid replyId)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await MessageReplyService.DeleteAsync(replyId, userId);
        return result.ToApiResult();
    }
}
