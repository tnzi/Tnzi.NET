namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// 技能管理控制器（管理员） — 提供全局技能管理功能。
/// </summary>
[DefaultController]
[Route("admin/skills")]
public class DefaultSkillAdminController : ApiAdminControllerBase
{
    protected readonly ISkillService SkillService;

    public DefaultSkillAdminController(ISkillService skillService)
    {
        SkillService = Check.NotNull(skillService);
    }

    /// <summary>
    /// 分页查询技能
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<SkillSummaryDto>>> GetPaged([FromQuery] SkillQueryDto query)
    {
        var result = await SkillService.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 按 slug 获取技能详情
    /// </summary>
    [HttpGet("{slug}")]
    public virtual async Task<ApiResult<SkillDetailDto>> GetBySlug(string slug)
    {
        var result = await SkillService.GetBySlugAsync(slug);
        return result.ToApiResult();
    }

    /// <summary>
    /// 搜索技能
    /// </summary>
    [HttpGet("search")]
    public virtual async Task<ApiResult<List<SkillSummaryDto>>> Search([FromQuery] string query, [FromQuery][Range(1, 100)] int maxResults = 10)
    {
        var result = await SkillService.SearchAsync(query, maxResults);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建租户级技能
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<SkillDetailDto>> Create([FromBody] CreateSkillDto input)
    {
        var dto = new CreateSkillDto
        {
            Slug = input.Slug,
            Scope = SkillScope.Tenant,
            Name = input.Name,
            Description = input.Description,
            Content = input.Content,
            WhenToUse = input.WhenToUse,
            Parameters = input.Parameters,
            AllowedToolGroups = input.AllowedToolGroups,
            AllowedTools = input.AllowedTools,
            DeniedTools = input.DeniedTools,
            RequiredModel = input.RequiredModel,
            RequiredProvider = input.RequiredProvider,
            Requirements = input.Requirements,
            Tags = input.Tags,
            Priority = input.Priority,
            Version = input.Version,
            Author = input.Author,
            Enabled = input.Enabled
        };
        var result = await SkillService.CreateAsync(dto);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新技能
    /// </summary>
    [HttpPut("{id:guid}")]
    public virtual async Task<ApiResult<SkillDetailDto>> Update(Guid id, [FromBody] UpdateSkillDto input)
    {
        var result = await SkillService.UpdateAsync(id, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除技能
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await SkillService.DeleteAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量删除技能
    /// </summary>
    [HttpPost("batch-delete")]
    public virtual async Task<ApiResult<int>> BatchDelete([FromBody] List<Guid> ids)
    {
        var result = await SkillService.BatchDeleteAsync(ids);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量启用技能
    /// </summary>
    [HttpPost("batch-enable")]
    public virtual async Task<ApiResult<int>> BatchEnable([FromBody] List<Guid> ids)
    {
        var result = await SkillService.BatchSetEnabledAsync(ids, true);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量禁用技能
    /// </summary>
    [HttpPost("batch-disable")]
    public virtual async Task<ApiResult<int>> BatchDisable([FromBody] List<Guid> ids)
    {
        var result = await SkillService.BatchSetEnabledAsync(ids, false);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取技能使用统计
    /// </summary>
    [HttpGet("stats")]
    public virtual async Task<ApiResult<SkillUsageStatsDto>> GetUsageStats()
    {
        var result = await SkillService.GetUsageStatsAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取热门技能
    /// </summary>
    [HttpGet("popular")]
    public virtual async Task<ApiResult<List<PopularSkillDto>>> GetPopular([FromQuery][Range(1, 100)] int topN = 10)
    {
        var result = await SkillService.GetPopularSkillsAsync(topN);
        return result.ToApiResult();
    }

    /// <summary>
    /// 导出技能
    /// </summary>
    [HttpGet("export")]
    public virtual async Task<ApiResult<List<SkillExportDto>>> Export([FromQuery] SkillScope? scope = null)
    {
        var result = await SkillService.ExportSkillsAsync(scope);
        return result.ToApiResult();
    }

    /// <summary>
    /// 导入技能
    /// </summary>
    [HttpPost("import")]
    public virtual async Task<ApiResult<SkillImportResultDto>> Import([FromBody] SkillImportRequestDto request)
    {
        var result = await SkillService.ImportSkillsAsync(request.Skills, request.TargetScope);
        return result.ToApiResult();
    }
}
