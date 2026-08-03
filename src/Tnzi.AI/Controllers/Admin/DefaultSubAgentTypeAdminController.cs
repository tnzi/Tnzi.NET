namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// 子 Agent 类型管理控制器 - 持久化自定义子 Agent 类型定义
/// </summary>
[DefaultController]
[Route("admin/ai/sub-agent-types")]
[ApiAuthorize(PermissionName = "ai.agent.view")]
public class DefaultSubAgentTypeAdminController : ApiAdminControllerBase
{
    protected readonly ISubAgentTypeService SubAgentTypeService;

    public DefaultSubAgentTypeAdminController(ISubAgentTypeService subAgentTypeService)
    {
        SubAgentTypeService = Check.NotNull(subAgentTypeService);
    }

    /// <summary>
    /// 获取所有子 Agent 类型定义
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<List<SubAgentTypeDto>>> GetAll(CancellationToken ct = default)
    {
        var result = await SubAgentTypeService.GetListAsync(ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建子 Agent 类型定义
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "ai.agent.create")]
    public virtual async Task<ApiResult<SubAgentTypeDto>> Create([FromBody] SubAgentTypeInputDto input, CancellationToken ct = default)
    {
        var result = await SubAgentTypeService.CreateAsync(input, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新子 Agent 类型定义
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "ai.agent.update")]
    public virtual async Task<ApiResult<SubAgentTypeDto>> Update(Guid id, [FromBody] SubAgentTypeInputDto input, CancellationToken ct = default)
    {
        var result = await SubAgentTypeService.UpdateAsync(id, input, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除子 Agent 类型定义
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "ai.agent.delete")]
    public virtual async Task<ApiResult> Delete(Guid id, CancellationToken ct = default)
    {
        var result = await SubAgentTypeService.DeleteAsync(id, ct);
        return result.ToApiResult();
    }
}
