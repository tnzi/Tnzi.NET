namespace Tnzi.AI.Controllers;

/// <summary>
/// 用户端线程控制器 - 已认证用户管理自己的对话线程。
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
    protected readonly ISuggestionService? SuggestionService;
    protected readonly IAgentAttachmentService? AttachmentService;

    public DefaultThreadController(
        IAgentThreadService threadService,
        IMessageFeedbackService feedbackService,
        ISuggestionService? suggestionService = null,
        IAgentAttachmentService? attachmentService = null)
    {
        ThreadService = Check.NotNull(threadService);
        FeedbackService = Check.NotNull(feedbackService);
        SuggestionService = suggestionService;
        AttachmentService = attachmentService;
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
    /// 当前用户线程的关注状态：哪些线程有一轮正在服务端跑、哪些有还没看过的回答，以及两者的条数。
    /// 给侧栏与模式切换上的标识用：比翻列表便宜，也不受分页限制。
    /// </summary>
    [HttpPost("attention")]
    public virtual async Task<ApiResult<ThreadAttentionDto>> GetMyAttention([FromBody] ThreadAttentionQueryDto? query = null)
    {
        query ??= new ThreadAttentionQueryDto();
        query.CreatorId = GetCurrentUserId();
        var result = await ThreadService.GetAttentionAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 标记当前用户看过了这条线程：此前写下的回答都不再算未读。客户端在打开线程、以及线程在屏幕上时一轮结束时调用。
    /// </summary>
    [HttpPost("{id:guid}/read")]
    public virtual async Task<ApiResult> MarkRead(Guid id)
    {
        await EnsureOwnershipAsync(id);
        var result = await ThreadService.MarkReadAsync(id);
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
    /// 往回翻页取当前用户线程的消息：<paramref name="beforeOrder"/> 之前（不含）的最近 <paramref name="limit"/> 条，旧的在前；
    /// 不带 <paramref name="beforeOrder"/> 时取最新的一页。用返回页第一条的 <c>order</c> 作下一次的 <paramref name="beforeOrder"/>。
    /// </summary>
    [HttpGet("{id:guid}/messages")]
    public virtual async Task<ApiResult<ThreadMessagePageDto>> GetMessages(
        Guid id, [FromQuery] int? beforeOrder = null, [FromQuery][Range(1, 200)] int limit = 50)
    {
        await EnsureOwnershipAsync(id);
        var result = await ThreadService.GetMessagesAsync(id, beforeOrder, limit);
        return result.ToApiResult();
    }

    /// <summary>
    /// 读一个附件的内容（用户随消息发的图片 / PDF / 文件，或工具返回的图片）。只有线程的主人能读。
    /// </summary>
    /// <remarks>
    /// 文件（不是图片或 PDF）的媒体类型是客户端声明的，可能是 <c>text/html</c> 之类能在本站执行脚本的类型：
    /// 一律以下载（<c>Content-Disposition: attachment</c>）交出，并加 <c>Content-Security-Policy: sandbox</c>，
    /// 直接打开这个地址也不会在 API 的源上渲染它。
    /// </remarks>
    [HttpGet("{threadId:guid}/attachments/{attachmentId:guid}")]
    public virtual async Task<IActionResult> GetAttachment(Guid threadId, Guid attachmentId, CancellationToken ct = default)
    {
        if (AttachmentService is null)
        {
            return NotFound(ApiResult.Error("Attachment not found.", 404));
        }

        var userId = GetCurrentUserId();
        var result = await AttachmentService.GetContentAsync(threadId, attachmentId, userId, ct);
        if (!result.Succeeded || result.Data is null)
        {
            return NotFound(result.ToApiResult());
        }

        // 图片与 PDF 是按文件头认过的，不会是能在本站执行脚本的类型；nosniff 让浏览器不去另猜
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (ChatAttachmentRules.IsInlineMediaType(result.Data.MediaType))
        {
            return File(result.Data.Content, result.Data.MediaType, result.Data.FileName, enableRangeProcessing: false);
        }

        Response.Headers["Content-Security-Policy"] = "sandbox";
        return File(
            result.Data.Content, result.Data.MediaType, result.Data.FileName ?? ChatAttachmentRules.DefaultFileName, enableRangeProcessing: false);
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
    /// 归档当前用户的线程：它离开「最近对话」（列表按 <c>Status = Active</c> 筛选时），内容不变；
    /// 在里面再发一条消息即取消归档。
    /// </summary>
    [HttpPost("{id:guid}/archive")]
    public virtual async Task<ApiResult<AgentThreadDto>> Archive(Guid id)
    {
        await EnsureOwnershipAsync(id);
        var result = await ThreadService.SetArchivedAsync(id, archived: true);
        return result.ToApiResult();
    }

    /// <summary>
    /// 取消归档当前用户的线程
    /// </summary>
    [HttpPost("{id:guid}/unarchive")]
    public virtual async Task<ApiResult<AgentThreadDto>> Unarchive(Guid id)
    {
        await EnsureOwnershipAsync(id);
        var result = await ThreadService.SetArchivedAsync(id, archived: false);
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
    public virtual async Task<ApiResult<List<string>>> GetSuggestions(Guid threadId, [FromQuery][Range(1, 10)] int count = 3, CancellationToken ct = default)
    {
        await EnsureOwnershipAsync(threadId);

        // 501 而不是 503：服务缺席是这个部署没装这块能力，不是暂时性故障。
        // 503 会让监控与客户端一直重试一件永远不会好的事。
        if (SuggestionService == null)
            return ApiResult<List<string>>.Error("Suggestion service is not available in this deployment.", 501);

        var suggestions = await SuggestionService.GenerateAsync(threadId, count, ct);
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
    private Guid GetCurrentUserId() => AiControllerHelpers.RequireUserId(GetRequiredCurrentUser());

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
