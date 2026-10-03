namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// Agent 授权治理控制器 - 在 <see cref="IAgentGrantService"/> 之上提供 additive 治理面：
/// 反向查询（"哪些 Agent 使用资源 X"）+ 按 Agent 列出全部授权（含已禁用）+ 单条授权的启用/优先级/删除。
/// 与 AgentDto 的工具组/技能/知识库 wire 契约正交：那条契约仍由 AgentService 投影/reconcile，
/// 这里只暴露 per-binding 治理操作。
/// Agent grant governance controller - additive surface over <see cref="IAgentGrantService"/>:
/// reverse query ("which agents use resource X"), the full grant list of an agent (disabled grants included),
/// and per-grant enable / priority / delete.
/// </summary>
[DefaultController]
[Route("admin/agents/grants")]
[ApiAuthorize(PermissionName = "ai.agent.view")]
public class DefaultAgentGrantAdminController : ApiAdminControllerBase
{
    protected readonly IAgentGrantService GrantService;

    public DefaultAgentGrantAdminController(IAgentGrantService grantService)
    {
        GrantService = Check.NotNull(grantService);
    }

    /// <summary>
    /// 反向查询：持有指定工具组/工具键已启用授权的（未删除）Agent，按名称排序。
    /// Non-deleted agents holding an enabled grant for the given tool group/tool key, ordered by name.
    /// </summary>
    [HttpGet("reverse/tool")]
    public virtual async Task<ApiResult<IReadOnlyList<AgentGrantUsageDto>>> ReverseByTool([FromQuery] string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            return ApiResult<IReadOnlyList<AgentGrantUsageDto>>.Error("Query parameter 'key' is required.", 400);

        var agents = await GrantService.GetAgentsUsingToolAsync(key, ct);
        return ApiResult<IReadOnlyList<AgentGrantUsageDto>>.Ok(agents);
    }

    /// <summary>
    /// 反向查询：持有指定技能 slug 已启用授权的（未删除）Agent，按名称排序。
    /// Non-deleted agents holding an enabled grant for the given skill slug, ordered by name.
    /// </summary>
    [HttpGet("reverse/skill")]
    public virtual async Task<ApiResult<IReadOnlyList<AgentGrantUsageDto>>> ReverseBySkill([FromQuery] string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return ApiResult<IReadOnlyList<AgentGrantUsageDto>>.Error("Query parameter 'slug' is required.", 400);

        var agents = await GrantService.GetAgentsUsingSkillAsync(slug, ct);
        return ApiResult<IReadOnlyList<AgentGrantUsageDto>>.Ok(agents);
    }

    /// <summary>
    /// 反向查询：持有指定知识库已启用授权的（未删除）Agent，按名称排序。
    /// Non-deleted agents holding an enabled grant for the given knowledge base, ordered by name.
    /// </summary>
    [HttpGet("reverse/knowledge")]
    public virtual async Task<ApiResult<IReadOnlyList<AgentGrantUsageDto>>> ReverseByKnowledge([FromQuery] Guid knowledgeBaseId, CancellationToken ct = default)
    {
        var agents = await GrantService.GetAgentsUsingKnowledgeAsync(knowledgeBaseId, ct);
        return ApiResult<IReadOnlyList<AgentGrantUsageDto>>.Ok(agents);
    }

    /// <summary>
    /// 一个 Agent 的全部授权（含已禁用），带授权 Id，供启停 / 删除端点寻址。
    /// Every grant of an agent, disabled ones included, with the grant ids the enable / delete endpoints address.
    /// </summary>
    [HttpGet("agent/{agentId:guid}")]
    public virtual async Task<ApiResult<AgentGrantListDto>> ListForAgent(Guid agentId, CancellationToken ct = default)
    {
        var grants = await GrantService.ListGrantsAsync(agentId, ct);
        return ApiResult<AgentGrantListDto>.Ok(grants);
    }

    /// <summary>
    /// 设置单条授权的启用状态。404 表示该 grantId 在对应资源类别下不存在。
    /// Sets a single grant's enabled flag. 404 when the grant id does not exist for the resource type.
    /// </summary>
    [HttpPost("{grantType}/{grantId:guid}/enabled")]
    [ApiAuthorize(PermissionName = "ai.agent.update")]
    public virtual async Task<ApiResult> SetEnabled(
        GrantResourceType grantType, Guid grantId, [FromBody] SetGrantEnabledDto input, CancellationToken ct = default)
    {
        Check.NotNull(input);

        var updated = await GrantService.SetGrantEnabledAsync(grantType, grantId, input.Enabled, ct);
        return updated ? ApiResult.Ok() : ApiResult.Error("Grant not found.", 404);
    }

    /// <summary>
    /// 设置单条授权的优先级。404 表示该 grantId 在对应资源类别下不存在。
    /// Sets a single grant's priority. 404 when the grant id does not exist for the resource type.
    /// </summary>
    [HttpPost("{grantType}/{grantId:guid}/priority")]
    [ApiAuthorize(PermissionName = "ai.agent.update")]
    public virtual async Task<ApiResult> SetPriority(
        GrantResourceType grantType, Guid grantId, [FromBody] SetGrantPriorityDto input, CancellationToken ct = default)
    {
        Check.NotNull(input);

        var updated = await GrantService.SetGrantPriorityAsync(grantType, grantId, input.Priority, ct);
        return updated ? ApiResult.Ok() : ApiResult.Error("Grant not found.", 404);
    }

    /// <summary>
    /// 删除单条授权（软删除）。移除<b>已禁用</b>授权只能走这里：Agent 更新的列表字段只描述已启用的授权。
    /// 404 表示该 grantId 在对应资源类别下不存在。
    /// Deletes a single grant (soft delete). The only way to remove a disabled grant: the list fields of an agent
    /// update describe enabled grants only. 404 when the grant id does not exist for the resource type.
    /// </summary>
    [HttpDelete("{grantType}/{grantId:guid}")]
    [ApiAuthorize(PermissionName = "ai.agent.update")]
    public virtual async Task<ApiResult> Delete(GrantResourceType grantType, Guid grantId, CancellationToken ct = default)
    {
        var deleted = await GrantService.DeleteGrantAsync(grantType, grantId, ct);
        return deleted ? ApiResult.Ok() : ApiResult.Error("Grant not found.", 404);
    }
}
