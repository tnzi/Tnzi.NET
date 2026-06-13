namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// 子 Agent 类型管理控制器 — 持久化自定义子 Agent 类型定义
/// </summary>
[DefaultController]
[Route("admin/ai/sub-agent-types")]
public class DefaultSubAgentTypeAdminController : ApiAdminControllerBase
{
    protected readonly IRepository<SubAgentType, Guid> Repository;
    protected readonly ISubAgentRegistry SubAgentRegistry;

    public DefaultSubAgentTypeAdminController(
        IRepository<SubAgentType, Guid> repository,
        ISubAgentRegistry subAgentRegistry)
    {
        Repository = Check.NotNull(repository);
        SubAgentRegistry = Check.NotNull(subAgentRegistry);
    }

    /// <summary>
    /// 获取所有子 Agent 类型定义
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<List<SubAgentTypeDto>>> GetAll(CancellationToken ct = default)
    {
        var entities = await Repository.AsQueryable()
            .OrderBy(e => e.Name)
            .ToListAsync(ct);

        return ApiResult<List<SubAgentTypeDto>>.Ok(entities.MapToList<SubAgentTypeDto>());
    }

    /// <summary>
    /// 创建子 Agent 类型定义
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<SubAgentTypeDto>> Create([FromBody] SubAgentTypeInputDto input, CancellationToken ct = default)
    {
        Check.NotNull(input);

        var entity = input.MapTo<SubAgentType>();
        await Repository.InsertAsync(entity, ct);
        await ReloadRegistryAsync(ct);

        return ApiResult<SubAgentTypeDto>.Ok(entity.MapTo<SubAgentTypeDto>());
    }

    /// <summary>
    /// 更新子 Agent 类型定义
    /// </summary>
    [HttpPut("{id:guid}")]
    public virtual async Task<ApiResult<SubAgentTypeDto>> Update(Guid id, [FromBody] SubAgentTypeInputDto input, CancellationToken ct = default)
    {
        Check.NotNull(input);

        var entity = await Repository.GetAsync(id, ct);
        if (entity == null)
            return ApiResult<SubAgentTypeDto>.Error("Sub-agent type not found.", 404);

        entity.Name = input.Name;
        entity.Description = input.Description;
        entity.ToolGroups = input.ToolGroups;
        entity.ExcludedToolGroups = input.ExcludedToolGroups;
        entity.MaxTurns = input.MaxTurns;
        entity.Instructions = input.Instructions;
        entity.DefaultModel = input.DefaultModel;
        entity.DefaultApprovalMode = input.DefaultApprovalMode;
        entity.CapabilityTags = input.CapabilityTags;
        entity.IsEnabled = input.IsEnabled;

        await Repository.UpdateAsync(entity, ct);
        await ReloadRegistryAsync(ct);

        return ApiResult<SubAgentTypeDto>.Ok(entity.MapTo<SubAgentTypeDto>());
    }

    /// <summary>
    /// 删除子 Agent 类型定义
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id, CancellationToken ct = default)
    {
        var entity = await Repository.GetAsync(id, ct);
        if (entity == null)
            return ApiResult.Error("Sub-agent type not found.", 404);

        await Repository.DeleteAsync(entity, ct);
        SubAgentRegistry.Unregister(entity.Name);

        return ApiResult.Ok();
    }

    /// <summary>
    /// 从数据库重新加载所有已启用的类型到注册表
    /// </summary>
    private async Task ReloadRegistryAsync(CancellationToken ct)
    {
        await SubAgentRegistry.LoadFromStoreAsync(Repository, ct);
    }
}
