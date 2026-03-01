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

    /// <summary>
    /// 批量删除消息
    /// </summary>
    [HttpPost("batch-delete")]
    public virtual async Task<ApiResult<int>> BatchDelete([FromBody] List<Guid> ids)
    {
        var result = await MessageService.AdminBatchDeleteAsync(ids);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取消息统计
    /// </summary>
    [HttpGet("statistics")]
    public virtual async Task<ApiResult<ChatStatisticsDto>> GetStatistics([FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null)
    {
        var result = await MessageService.GetStatisticsAsync(startDate, endDate);
        return result.ToApiResult();
    }
}
