namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// Agent 人格管理控制器
/// </summary>
[DefaultController]
[Route("admin/ai/personas")]
[ApiAuthorize(PermissionName = "ai.persona.view")]
public class DefaultAgentPersonaAdminController : ApiAdminControllerBase
{
    protected readonly IAgentPersonaService PersonaService;

    public DefaultAgentPersonaAdminController(IAgentPersonaService personaService)
    {
        PersonaService = Check.NotNull(personaService);
    }

    /// <summary>
    /// 创建人格
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<AgentPersonaDto>> Create([FromBody] CreateAgentPersonaDto input, CancellationToken ct = default)
    {
        var result = await PersonaService.CreateAsync(input, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新人格
    /// </summary>
    [HttpPut("{id:guid}")]
    public virtual async Task<ApiResult<AgentPersonaDto>> Update(Guid id, [FromBody] UpdateAgentPersonaDto input, CancellationToken ct = default)
    {
        var result = await PersonaService.UpdateAsync(id, input, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除人格
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id, CancellationToken ct = default)
    {
        var result = await PersonaService.DeleteAsync(id, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 根据 ID 获取人格
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<AgentPersonaDto>> GetById(Guid id, CancellationToken ct = default)
    {
        var result = await PersonaService.GetByIdAsync(id, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 根据 Slug 获取人格
    /// </summary>
    [HttpGet("by-slug/{slug}")]
    public virtual async Task<ApiResult<AgentPersonaDto>> GetBySlug(string slug, CancellationToken ct = default)
    {
        var result = await PersonaService.GetBySlugAsync(slug, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 查询人格列表
    /// </summary>
    [HttpPost("query")]
    public virtual async Task<ApiResult<IPagedList<AgentPersonaDto>>> GetList([FromBody] AgentPersonaQueryDto query, CancellationToken ct = default)
    {
        var result = await PersonaService.GetListAsync(query, ct);
        return result.ToApiResult();
    }
}
