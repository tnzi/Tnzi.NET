namespace Tnzi.Chat.Controllers;

/// <summary>
/// 草稿消息控制器
/// 提供草稿保存、查询、发送、删除等API端点
/// </summary>
[DefaultController]
[ApiAuthorize]
[Route("chat/drafts")]
[ApiExplorerSettings(GroupName = "chat")]
public class DefaultDraftController : ApiControllerBase
{
    protected readonly IDraftMessageService DraftMessageService;

    public DefaultDraftController(IDraftMessageService draftMessageService)
    {
        DraftMessageService = Check.NotNull(draftMessageService);
    }

    /// <summary>
    /// 保存草稿（创建或更新）
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<MessageDto>> Save([FromBody] SaveDraftDto input)
    {
        var result = await DraftMessageService.SaveDraftAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取当前用户的草稿列表
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<List<MessageDto>>> GetMyDrafts()
    {
        var result = await DraftMessageService.GetMyDraftsAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 发送草稿
    /// </summary>
    [HttpPost("{id:guid}/send")]
    public virtual async Task<ApiResult<MessageDto>> SendDraft(Guid id)
    {
        var result = await DraftMessageService.SendDraftAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除草稿
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await DraftMessageService.DeleteDraftAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取当前用户草稿数量
    /// </summary>
    [HttpGet("count")]
    public virtual async Task<ApiResult<int>> GetDraftCount()
    {
        var result = await DraftMessageService.GetDraftCountAsync();
        return result.ToApiResult();
    }
}
