namespace Tnzi.AI.Skills.Controllers.Admin;

/// <summary>
/// 技能管理控制器（管理员） — 提供全局技能管理功能。
/// </summary>
[DefaultController]
[Route("admin/skills")]
[ApiAuthorize(PermissionName = "ai.skill.view")]
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
    [ApiAuthorize(PermissionName = "ai.skill.create")]
    public virtual async Task<ApiResult<SkillDetailDto>> Create([FromBody] CreateSkillDto input)
    {
        input.Scope = SkillScope.Tenant;
        var result = await SkillService.CreateAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新技能
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "ai.skill.update")]
    public virtual async Task<ApiResult<SkillDetailDto>> Update(Guid id, [FromBody] UpdateSkillDto input)
    {
        var result = await SkillService.UpdateAsync(id, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除技能
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "ai.skill.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await SkillService.DeleteAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量删除技能
    /// </summary>
    [HttpPost("batch-delete")]
    [ApiAuthorize(PermissionName = "ai.skill.delete")]
    public virtual async Task<ApiResult<int>> BatchDelete([FromBody] List<Guid> ids)
    {
        var result = await SkillService.BatchDeleteAsync(ids);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量启用技能
    /// </summary>
    [HttpPost("batch-enable")]
    [ApiAuthorize(PermissionName = "ai.skill.update")]
    public virtual async Task<ApiResult<int>> BatchEnable([FromBody] List<Guid> ids)
    {
        var result = await SkillService.BatchSetEnabledAsync(ids, true);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量禁用技能
    /// </summary>
    [HttpPost("batch-disable")]
    [ApiAuthorize(PermissionName = "ai.skill.update")]
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
    [ApiAuthorize(PermissionName = "ai.skill.create")]
    public virtual async Task<ApiResult<SkillImportResultDto>> Import([FromBody] SkillImportRequestDto request)
    {
        var result = await SkillService.ImportSkillsAsync(request.Skills, request.TargetScope);
        return result.ToApiResult();
    }
}
