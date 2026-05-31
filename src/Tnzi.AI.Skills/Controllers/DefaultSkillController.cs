namespace Tnzi.AI.Controllers;

/// <summary>
/// 用户端技能控制器 — 提供技能浏览、激活、个人技能 CRUD 功能。
/// </summary>
[DefaultController]
[Route("skills")]
[ApiExplorerSettings(GroupName = "user")]
[ApiAuthorize]
public class DefaultSkillController : ApiControllerBase
{
    protected readonly ISkillService SkillService;

    public DefaultSkillController(ISkillService skillService)
    {
        SkillService = Check.NotNull(skillService);
    }

    /// <summary>
    /// 获取当前用户/租户可用的所有技能
    /// </summary>
    [AllowAnonymous]
    [HttpGet]
    public virtual async Task<ApiResult<List<SkillSummaryDto>>> GetAvailable()
    {
        var result = await SkillService.GetAvailableAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 按 slug 获取技能详情
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{slug}")]
    public virtual async Task<ApiResult<SkillDetailDto>> GetBySlug(string slug)
    {
        var result = await SkillService.GetBySlugAsync(slug);
        return result.ToApiResult();
    }

    /// <summary>
    /// 搜索技能
    /// </summary>
    [AllowAnonymous]
    [HttpGet("search")]
    public virtual async Task<ApiResult<List<SkillSummaryDto>>> Search([FromQuery] string query, [FromQuery][Range(1, 100)] int maxResults = 10)
    {
        var result = await SkillService.SearchAsync(query, maxResults);
        return result.ToApiResult();
    }

    /// <summary>
    /// 激活技能（渲染提示词模板）
    /// </summary>
    [HttpPost("{slug}/activate")]
    public virtual async Task<ApiResult<SkillActivationResult>> Activate(string slug, [FromBody] SkillActivateDto? input = null)
    {
        var result = await SkillService.ActivateAsync(slug, input?.Parameters);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建用户个人技能（强制 Scope=User，自助服务端点不允许创建租户/系统级技能）
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<SkillDetailDto>> Create([FromBody] CreateSkillDto input)
    {
        // Self-service endpoint: always create as User scope regardless of what the caller sends.
        // Tenant/System scope creation must go through the admin endpoint with appropriate permissions.
        input.Scope = SkillScope.User;
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
