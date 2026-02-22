namespace Tnzi.Chat.Controllers;

/// <summary>
/// 聊天管理端控制器基类
/// 提供消息管理查询、删除等管理API端点
/// </summary>
[Route("admin/messages")]
public abstract class ChatAdminControllerBase : ApiAdminControllerBase
{
    protected readonly IMessageService MessageService;

    protected ChatAdminControllerBase(IMessageService messageService)
    {
        MessageService = Check.NotNull(messageService);
    }

    /// <summary>
    /// 查询所有消息列表
    /// </summary>
    [HttpPost("query")]
    public virtual async Task<ApiResult<IPagedList<MessageListItemDto>>> GetList([FromBody] AdminMessageQueryDto query)
    {
        var result = await MessageService.GetAdminListAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除消息
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await MessageService.AdminDeleteAsync(id);
        return result.ToApiResult();
    }
}
