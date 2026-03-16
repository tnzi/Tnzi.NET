namespace Tnzi.AI.Controllers;

/// <summary>
/// 用户端线程控制器 — 已认证用户管理自己的对话线程。
/// 所有操作自动限定为当前用户（CreatorId 过滤）。
/// </summary>
[DefaultController]
[Route("threads")]
[ApiExplorerSettings(GroupName = "user")]
public class DefaultThreadController : ApiControllerBase
{
    protected readonly IAgentThreadService ThreadService;

    public DefaultThreadController(IAgentThreadService threadService)
    {
        ThreadService = Check.NotNull(threadService);
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
    public virtual async Task<ApiResult<AgentThreadDetailDto>> GetDetail(Guid id, [FromQuery] int messageLimit = 50)
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
    /// 验证当前用户是否为线程所有者
    /// </summary>
    private async Task EnsureOwnershipAsync(Guid threadId)
    {
        var userId = GetCurrentUserId();
        var result = await ThreadService.GetByIdAsync(threadId);
        if (!result.Succeeded)
        {
            throw new BusinessException("Thread not found", ErrorCodes.ThreadNotFound, 404);
        }

        // GetByIdAsync 不返回 CreatorId，使用 GetListAsync 验证所有权
        var query = new ThreadListQueryDto { CreatorId = userId, PageSize = 1 };
        var ownerCheck = await ThreadService.GetListAsync(query);
        if (!ownerCheck.Succeeded || !ownerCheck.Data!.Items.Any(t => t.Id == threadId))
        {
            throw new BusinessException("Thread not found", ErrorCodes.ThreadNotFound, 404);
        }
    }
}
