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
    /// 获取所有可用技能
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<List<SkillSummaryDto>>> GetAvailable()
    {
        var result = await SkillService.GetAvailableAsync();
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
    /// 创建租户级技能
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<SkillDetailDto>> Create([FromBody] CreateSkillDto input)
    {
        // 管理员默认创建租户级技能
        input.Scope = SkillScope.Tenant;
        var result = await SkillService.CreateAsync(input);
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
}
