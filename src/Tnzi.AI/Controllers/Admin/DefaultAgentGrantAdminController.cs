namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// Agent 授权治理控制器 — 在 <see cref="IAgentGrantService"/> 之上提供 additive 治理面：
/// 反向查询（"哪些 Agent 使用资源 X"）+ 单条授权的启用/优先级控制。
/// 与 AgentDto 的工具组/技能/知识库 wire 契约正交：那条契约仍由 AgentService 投影/reconcile，
/// 这里只暴露 per-binding 治理操作。
/// Agent grant governance controller — additive surface over <see cref="IAgentGrantService"/>:
/// reverse query ("which agents use resource X") + per-grant enable/priority.
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
    /// 反向查询：授权了指定工具组/工具键的 Agent Id 列表。
    /// Agents that have an enabled grant for the given tool group/tool key.
    /// </summary>
    [HttpGet("reverse/tool")]
    public virtual async Task<ApiResult<IReadOnlyList<Guid>>> ReverseByTool([FromQuery] string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            return ApiResult<IReadOnlyList<Guid>>.Error("Query parameter 'key' is required.", 400);

        var agentIds = await GrantService.GetAgentsUsingToolAsync(key, ct);
        return ApiResult<IReadOnlyList<Guid>>.Ok(agentIds);
    }

    /// <summary>
    /// 反向查询：授权了指定技能 slug 的 Agent Id 列表。
    /// Agents that have an enabled grant for the given skill slug.
    /// </summary>
    [HttpGet("reverse/skill")]
    public virtual async Task<ApiResult<IReadOnlyList<Guid>>> ReverseBySkill([FromQuery] string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return ApiResult<IReadOnlyList<Guid>>.Error("Query parameter 'slug' is required.", 400);

        var agentIds = await GrantService.GetAgentsUsingSkillAsync(slug, ct);
        return ApiResult<IReadOnlyList<Guid>>.Ok(agentIds);
    }

    /// <summary>
    /// 反向查询：授权了指定知识库的 Agent Id 列表。
    /// Agents that have an enabled grant for the given knowledge base.
    /// </summary>
    [HttpGet("reverse/knowledge")]
    public virtual async Task<ApiResult<IReadOnlyList<Guid>>> ReverseByKnowledge([FromQuery] Guid knowledgeBaseId, CancellationToken ct = default)
    {
        var agentIds = await GrantService.GetAgentsUsingKnowledgeAsync(knowledgeBaseId, ct);
        return ApiResult<IReadOnlyList<Guid>>.Ok(agentIds);
    }

    /// <summary>
    /// 设置单条授权的启用状态。404 表示该 grantId 在对应资源类别下不存在。
    /// Sets a single grant's enabled flag. 404 when the grant id does not exist for the resource type.
    /// </summary>
    [HttpPost("{grantType}/{grantId:guid}/enabled")]
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
    public virtual async Task<ApiResult> SetPriority(
        GrantResourceType grantType, Guid grantId, [FromBody] SetGrantPriorityDto input, CancellationToken ct = default)
    {
        Check.NotNull(input);

        var updated = await GrantService.SetGrantPriorityAsync(grantType, grantId, input.Priority, ct);
        return updated ? ApiResult.Ok() : ApiResult.Error("Grant not found.", 404);
    }
}
