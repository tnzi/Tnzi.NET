namespace Tnzi.Chat.Controllers.Admin;

/// <summary>
/// Admin CRUD endpoints for chat session groupings.
/// </summary>
[DefaultController]
[Route("admin/chat-sessions")]
public class DefaultChatSessionAdminController : ApiAdminControllerBase
{
    protected readonly IChatSessionService ChatSessionService;

    public DefaultChatSessionAdminController(IChatSessionService chatSessionService)
    {
        ChatSessionService = Check.NotNull(chatSessionService);
    }

    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<ChatSessionListItemDto>>> GetAll([FromQuery] ChatSessionQueryDto query)
    {
        var result = await ChatSessionService.GetPagedListAsync(query);
        return result.ToApiResult();
    }

    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<ChatSessionDto>> GetById(Guid id)
    {
        var result = await ChatSessionService.GetByIdAsync(id);
        return result.ToApiResult();
    }

    [HttpPost]
    public virtual async Task<ApiResult<ChatSessionDto>> Create([FromBody] CreateChatSessionDto input)
    {
        var result = await ChatSessionService.CreateAsync(input);
        return result.ToApiResult();
    }

    [HttpPut("{id:guid}")]
    public virtual async Task<ApiResult<ChatSessionDto>> Update(Guid id, [FromBody] UpdateChatSessionDto input)
    {
        var result = await ChatSessionService.UpdateAsync(id, input);
        return result.ToApiResult();
    }

    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await ChatSessionService.DeleteAsync(id);
        return result.ToApiResult();
    }

    [HttpDelete("batch")]
    public virtual async Task<ApiResult<int>> DeleteBatch([FromBody] IEnumerable<Guid> ids)
    {
        var result = await ChatSessionService.DeleteBatchAsync(ids);
        return result.ToApiResult();
    }
}
