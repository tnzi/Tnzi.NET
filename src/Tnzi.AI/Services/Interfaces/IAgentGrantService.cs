namespace Tnzi.AI.Services;

/// <summary>
/// 授权资源类别 - 标识一条 grant 属于哪种 junction 实体，供 per-grant CRUD 路由。
/// Grant resource category - identifies which junction entity a grant belongs to, for per-grant CRUD routing.
/// </summary>
public enum GrantResourceType
{
    /// <summary>工具授权（<see cref="AgentToolGrant"/>）。Tool grant.</summary>
    Tool = 0,

    /// <summary>技能授权（<see cref="AgentSkillGrant"/>）。Skill grant.</summary>
    Skill = 1,

    /// <summary>知识库授权（<see cref="AgentKnowledgeGrant"/>）。Knowledge grant.</summary>
    Knowledge = 2
}

/// <summary>
/// Agent 授权服务 - 管理 Agent 与工具/技能/知识库之间的 junction grant 关系。
/// 取代 Agent 上的 <c>ToolGroups</c>/<c>SkillSlugs</c>/<c>KnowledgeBaseIds</c> JSON 列，
/// 提供 (1) 只读投影（feed resolver/DTO）、(2) tri-state reconcile 写入（复刻 UpdateAgentDto PATCH 语义）、
/// (3) per-grant 列表/启用/优先级/删除（治理面）、(4) 反向查询（哪些 Agent 使用某资源）。
/// 这是内部服务（被 AgentService/AgentResolver 调用，非直接面向 Controller），故返回原始类型而非 <c>Result&lt;T&gt;</c>。
/// Internal Agent grant service managing the junction relationships between an Agent and its tools/skills/
/// knowledge bases. Returns primitive types (called by AgentService/AgentResolver, not controllers directly).
/// </summary>
public interface IAgentGrantService
{
    // ---------------------------------------------------------------------
    // Projection (read)
    // ---------------------------------------------------------------------

    /// <summary>
    /// 读取一个 Agent 的全部<b>已启用</b>授权，折叠为扁平投影。
    /// Reads all enabled grants of an Agent, flattened into a projection.
    /// </summary>
    Task<AgentGrantsProjection> GetGrantsAsync(Guid agentId, CancellationToken ct = default);

    /// <summary>
    /// 批量读取多个 Agent 的<b>已启用</b>授权（三类 grant 表各一次 <c>WHERE AgentId IN (...)</c> 查询 + 内存分组），
    /// 消除按页逐 Agent 查询的 N+1。返回字典按 agentId 索引；未授权任何资源的 Agent 返回空投影。
    /// Batched read of enabled grants for multiple Agents (one IN-query per grant table + in-memory grouping),
    /// eliminating per-item N+1. Result is keyed by agentId; agents with no grants get an empty projection.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, AgentGrantsProjection>> GetGrantsAsync(IReadOnlyList<Guid> agentIds, CancellationToken ct = default);

    /// <summary>
    /// 读取一个 Agent 的全部授权（<b>含已禁用</b>），带授权 Id / 启用状态 / 优先级，供治理面按条寻址。
    /// 可见性与 <see cref="GetGrantsAsync(Guid, CancellationToken)"/> 相同（租户调用者看得见共享 Agent 的授权）。
    /// Reads every grant of an agent, disabled ones included, with id / enabled flag / priority, for per-grant governance.
    /// </summary>
    Task<AgentGrantListDto> ListGrantsAsync(Guid agentId, CancellationToken ct = default);

    // ---------------------------------------------------------------------
    // Reconcile (write) - tri-state PATCH semantics
    // ---------------------------------------------------------------------
    //
    // 列表描述的是「应启用的授权集合」。已禁用的授权不在任何列表投影里（AgentDto 读不到它），
    // 所以 reconcile 时它不在目标集中并不表示调用方要删它：禁用条目只有两种去向：目标集含它则重新启用，
    // 否则原样保留；删除禁用条目走 DeleteGrantAsync。否则前端每改一次列表就把治理面刚禁用的授权顺手删掉。
    // The lists describe the set of ENABLED grants. A disabled grant never appears in a list projection, so its absence
    // from the desired set is not a request to remove it: it is re-enabled when the set names it and kept as-is otherwise.
    // Disabled grants are removed through DeleteGrantAsync.

    /// <summary>
    /// 协调工具组授权（仅 <see cref="GrantType.Group"/> 条目）。tri-state：
    /// null = 不变；空 [] = 移除全部已启用条目；有值 = diff（新增缺失 + 删除多余 + 保留两侧都有的现有条目）。
    /// 永不触碰 <see cref="GrantType.Tool"/> 单工具授权。
    /// Reconciles tool-group grants (Group entries only). null = no change; [] = remove every enabled entry; populated = diff over enabled entries.
    /// Never touches Tool-granularity grants.
    /// </summary>
    Task ReconcileToolGroupsAsync(Guid agentId, IReadOnlyList<string>? groups, CancellationToken ct = default);

    /// <summary>
    /// 协调单工具授权（仅 <see cref="GrantType.Tool"/> 条目）。tri-state：
    /// null = 不变；空 [] = 移除全部已启用条目；有值 = diff（新增缺失 + 删除多余 + 保留两侧都有的现有条目）。
    /// 永不触碰 <see cref="GrantType.Group"/> 工具组授权——Group 与 Tool 各自独立 reconcile，互不覆盖。
    /// Reconciles per-tool grants (Tool entries only). null = no change; [] = remove every enabled entry; populated = diff over enabled entries.
    /// Never touches Group-granularity grants.
    /// </summary>
    Task ReconcileToolNamesAsync(Guid agentId, IReadOnlyList<string>? toolNames, CancellationToken ct = default);

    /// <summary>
    /// 协调技能授权。tri-state（同上）。Reconciles skill grants (tri-state).
    /// </summary>
    Task ReconcileSkillsAsync(Guid agentId, IReadOnlyList<string>? slugs, CancellationToken ct = default);

    /// <summary>
    /// 协调知识库授权。tri-state（同上）。Reconciles knowledge grants (tri-state).
    /// </summary>
    Task ReconcileKnowledgeAsync(Guid agentId, IReadOnlyList<Guid>? knowledgeBaseIds, CancellationToken ct = default);

    // ---------------------------------------------------------------------
    // Per-grant CRUD (governance surface)
    // ---------------------------------------------------------------------

    /// <summary>
    /// 设置单条授权的启用状态。返回 false 表示该 grantId 不存在。
    /// Sets a single grant's enabled flag. Returns false when the grant id does not exist.
    /// </summary>
    Task<bool> SetGrantEnabledAsync(GrantResourceType resourceType, Guid grantId, bool enabled, CancellationToken ct = default);

    /// <summary>
    /// 设置单条授权的优先级。返回 false 表示该 grantId 不存在。
    /// Sets a single grant's priority. Returns false when the grant id does not exist.
    /// </summary>
    Task<bool> SetGrantPriorityAsync(GrantResourceType resourceType, Guid grantId, int priority, CancellationToken ct = default);

    /// <summary>
    /// 删除单条授权（软删除）。返回 false 表示该 grantId 不存在。这是移除<b>已禁用</b>授权的唯一途径（reconcile 不动禁用条目）。
    /// Deletes a single grant (soft delete). Returns false when the grant id does not exist. The only way to remove a
    /// disabled grant, since reconcile leaves disabled grants alone.
    /// </summary>
    Task<bool> DeleteGrantAsync(GrantResourceType resourceType, Guid grantId, CancellationToken ct = default);

    // ---------------------------------------------------------------------
    // Reverse query - "which agents use resource X"
    // ---------------------------------------------------------------------

    // 三个反向查询只计已启用授权，且排除已删除的 Agent（Agent 软删除不会级联到 grant 行）。结果按 Agent 名称排序。
    // The reverse queries count enabled grants only and skip deleted agents (an agent's soft delete does not cascade to
    // its grant rows). Results are ordered by agent name.

    /// <summary>启用授权了指定工具键（工具组名或工具名，按键精确匹配）的 Agent。Agents with an enabled grant for the given tool key (exact match).</summary>
    Task<IReadOnlyList<AgentGrantUsageDto>> GetAgentsUsingToolAsync(string toolKey, CancellationToken ct = default);

    /// <summary>启用授权了指定技能 slug 的 Agent。Agents with an enabled grant for the given skill slug.</summary>
    Task<IReadOnlyList<AgentGrantUsageDto>> GetAgentsUsingSkillAsync(string skillSlug, CancellationToken ct = default);

    /// <summary>启用授权了指定知识库的 Agent。Agents with an enabled grant for the given knowledge base.</summary>
    Task<IReadOnlyList<AgentGrantUsageDto>> GetAgentsUsingKnowledgeAsync(Guid knowledgeBaseId, CancellationToken ct = default);
}
