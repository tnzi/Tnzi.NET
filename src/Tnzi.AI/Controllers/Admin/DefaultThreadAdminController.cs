namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// Agent 线程管理控制器
/// 提供线程的列表查询、详情、更新标题、删除等管理端点
/// </summary>
[DefaultController]
[Route("admin/threads")]
public class DefaultThreadAdminController : ApiAdminControllerBase
{
    protected readonly IAgentThreadService ThreadService;

    /// <summary>
    /// 初始化线程管理控制器
    /// </summary>
    public DefaultThreadAdminController(IAgentThreadService threadService)
    {
        ThreadService = Check.NotNull(threadService);
    }

    /// <summary>
    /// 创建线程
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<AgentThreadDto>> Create([FromBody] CreateAgentThreadDto input)
    {
        var result = await ThreadService.CreateAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取线程列表（分页 + 筛选）
    /// </summary>
    [HttpPost("query")]
    public virtual async Task<ApiResult<IPagedList<AgentThreadDto>>> GetList([FromBody] ThreadListQueryDto query)
    {
        var result = await ThreadService.GetListAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 根据 ID 获取线程基本信息
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<AgentThreadDto>> GetById(Guid id)
    {
        var result = await ThreadService.GetByIdAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取线程详情（含最近消息）
    /// </summary>
    [HttpGet("{id:guid}/detail")]
    public virtual async Task<ApiResult<AgentThreadDetailDto>> GetDetail(Guid id, [FromQuery] int messageLimit = 50)
    {
        var result = await ThreadService.GetDetailAsync(id, messageLimit);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新线程标题
    /// </summary>
    [HttpPatch("{id:guid}/title")]
    public virtual async Task<ApiResult<AgentThreadDto>> UpdateTitle(Guid id, [FromBody] UpdateThreadTitleDto input)
    {
        var result = await ThreadService.UpdateTitleAsync(id, input.Title);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除线程
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await ThreadService.DeleteAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 导出线程为 JSON 格式
    /// </summary>
    [HttpGet("{id:guid}/export/json")]
    public virtual async Task<ApiResult<ThreadExportDto>> ExportAsJson(Guid id)
    {
        var result = await ThreadService.ExportAsJsonAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 导出线程为 Markdown 格式（文件下载）
    /// </summary>
    [HttpGet("{id:guid}/export/markdown")]
    public virtual async Task<IActionResult> ExportAsMarkdown(Guid id)
    {
        var result = await ThreadService.ExportAsMarkdownAsync(id);
        if (!result.Succeeded)
        {
            return NotFound(result.ToApiResult());
        }

        var bytes = Encoding.UTF8.GetBytes(result.Data!);
        return File(bytes, "text/markdown", $"thread-{id}.md");
    }
}

/// <summary>
/// 更新线程标题请求 DTO
/// </summary>
public class UpdateThreadTitleDto
{
    /// <summary>New title</summary>
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = null!;
}
