namespace Tnzi.AI.Skills.Controllers;

/// <summary>
/// 用户端技能控制器 - 提供技能浏览、激活、个人技能 CRUD 功能。
/// </summary>
/// <remarks>
/// ★ <b>整个控制器都要求认证，读端点也不例外。</b> 三个读端点（列表 / 详情 / 搜索）
/// 曾带 <c>[AllowAnonymous]</c>，意图是"锁写放读"，但读回来的是技能的<b>完整提示词正文</b>
/// （<c>SkillDetailDto.Content</c>），而搜索端点在关键词命中不足时会对查询串<b>现算一次嵌入</b>
/// （计费调用），既不过配额也不过预算。于是任何人无需登录就能读走全部 System/Tenant 技能正文，
/// 并用一个 GET 驱动嵌入账单。要开放公共技能目录的宿主应当自己覆写本控制器并投影出裁剪过的
/// 只读视图，而不是把框架默认放开。
/// </remarks>
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
    /// 搜索技能
    /// </summary>
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
