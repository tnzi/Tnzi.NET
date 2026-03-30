namespace Tnzi.AI.Controllers;

/// <summary>
/// 用户端线程控制器 — 已认证用户管理自己的对话线程。
/// 所有操作自动限定为当前用户（CreatorId 过滤）。
/// </summary>
[DefaultController]
[ApiAuthorize]
[Route("threads")]
[ApiExplorerSettings(GroupName = "user")]
public class DefaultThreadController : ApiControllerBase
{
    protected readonly IAgentThreadService ThreadService;
    protected readonly IMessageFeedbackService FeedbackService;

    public DefaultThreadController(IAgentThreadService threadService, IMessageFeedbackService feedbackService)
    {
        ThreadService = Check.NotNull(threadService);
        FeedbackService = Check.NotNull(feedbackService);
    }

    /// <summary>
    /// 获取当前用户的线程列表（分页 + 筛选）
    /// </summary>
    [HttpPost("query")]
    public virtual async Task<ApiResult<IPagedList<AgentThreadDto>>> GetMyThreads([FromBody] ThreadListQueryDto query)
    {
        var userId = GetCurrentUserId();
        query.CreatorId = userId;
        var result = await ThreadService.GetListAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取当前用户的线程详情（含最近消息）
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<AgentThreadDetailDto>> GetDetail(Guid id, [FromQuery][Range(1, 200)] int messageLimit = 50)
    {
        await EnsureOwnershipAsync(id);
        var result = await ThreadService.GetDetailAsync(id, messageLimit);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新当前用户的线程标题
    /// </summary>
    [HttpPatch("{id:guid}/title")]
    public virtual async Task<ApiResult<AgentThreadDto>> UpdateTitle(Guid id, [FromBody] UpdateThreadTitleDto input)
    {
        await EnsureOwnershipAsync(id);
        var result = await ThreadService.UpdateTitleAsync(id, input.Title);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除当前用户的线程
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        await EnsureOwnershipAsync(id);
        var result = await ThreadService.DeleteAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 导出当前用户的线程为 JSON 格式
    /// </summary>
    [HttpGet("{id:guid}/export/json")]
    public virtual async Task<ApiResult<ThreadExportDto>> ExportAsJson(Guid id)
    {
        await EnsureOwnershipAsync(id);
        var result = await ThreadService.ExportAsJsonAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 导出当前用户的线程为 Markdown 格式（文件下载）
    /// </summary>
    [HttpGet("{id:guid}/export/markdown")]
    public virtual async Task<IActionResult> ExportAsMarkdown(Guid id)
    {
        await EnsureOwnershipAsync(id);
        var result = await ThreadService.ExportAsMarkdownAsync(id);
        if (!result.Succeeded)
        {
            return NotFound(result.ToApiResult());
        }

        var bytes = Encoding.UTF8.GetBytes(result.Data!);
        return File(bytes, "text/markdown", $"thread-{id}.md");
    }

    /// <summary>
    /// 生成对话的后续建议问题
    /// </summary>
    [HttpPost("{threadId:guid}/suggestions")]
    public virtual async Task<ApiResult<List<string>>> GetSuggestions(Guid threadId, [FromQuery] int count = 3, CancellationToken ct = default)
    {
        await EnsureOwnershipAsync(threadId);

        var suggestionService = HttpContext.RequestServices.GetService<ISuggestionService>();
        if (suggestionService == null)
            return ApiResult<List<string>>.Error("Suggestion service not available.", 503);

        var suggestions = await suggestionService.GenerateAsync(threadId, count, ct);
        return ApiResult<List<string>>.Ok(suggestions);
    }

    /// <summary>
    /// 提交消息反馈（👍/👎 + 标签 + 文本）
    /// </summary>
    [HttpPost("{threadId:guid}/messages/{messageId:guid}/feedback")]
    public virtual async Task<ApiResult> SubmitFeedback(Guid threadId, Guid messageId, [FromBody] MessageFeedbackDto input)
    {
        var userId = GetCurrentUserId();
        var result = await FeedbackService.SubmitAsync(threadId, messageId, userId, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 撤回消息反馈
    /// </summary>
    [HttpDelete("{threadId:guid}/messages/{messageId:guid}/feedback")]
    public virtual async Task<ApiResult> RevokeFeedback(Guid threadId, Guid messageId)
    {
        var userId = GetCurrentUserId();
        var result = await FeedbackService.RevokeAsync(threadId, messageId, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取当前用户 ID，未认证时抛出异常
    /// </summary>
    private Guid GetCurrentUserId()
    {
        var userId = GetRequiredCurrentUser().Id;
        if (!userId.HasValue)
        {
            throw new BusinessException("Authentication required", "Unauthorized", 401);
        }
        return userId.Value;
    }

    /// <summary>
    /// 验证当前用户是否为线程所有者（单次查询）
    /// </summary>
    private async Task EnsureOwnershipAsync(Guid threadId)
    {
        var userId = GetCurrentUserId();
        if (!await ThreadService.IsOwnerAsync(threadId, userId))
        {
            throw new BusinessException("Thread not found", ErrorCodes.ThreadNotFound, 404);
        }
    }
}
