namespace Tnzi.AI.Services;

/// <summary>
/// Agent 授权服务实现 - 管理 Agent ↔ 工具/技能/知识库的 junction grant。
/// 见 <see cref="IAgentGrantService"/>。reconcile 复刻原 UpdateAgentDto 的 tri-state PATCH 语义，
/// 删除走仓储软删除（<c>DeleteAsync</c>），保留两侧都存在的现有条目以不丢失 admin 设置的 Priority/IsEnabled；
/// 已禁用的条目不在目标集中时原样保留（列表 = 应启用的授权集合）。
/// Agent grant service - manages the Agent ↔ tool/skill/knowledge junction grants. See <see cref="IAgentGrantService"/>.
/// Reconcile replicates the legacy UpdateAgentDto tri-state PATCH semantics; removals use the repository's
/// soft-delete (<c>DeleteAsync</c>), and grants present in both old and new sets are left untouched so that
/// admin-set Priority/IsEnabled are preserved.
/// </summary>
public class AgentGrantService : ApplicationService, IAgentGrantService
{
    private readonly IRepository<AgentToolGrant, Guid> _toolGrants;
    private readonly IRepository<AgentSkillGrant, Guid> _skillGrants;
    private readonly IRepository<AgentKnowledgeGrant, Guid> _knowledgeGrants;
    private readonly ICurrentTenant? _currentTenant;
    private readonly IOptions<MultiTenancyOptions>? _multiTenancyOptions;

    public AgentGrantService(
        IServiceProvider serviceProvider,
        IRepository<AgentToolGrant, Guid> toolGrants,
        IRepository<AgentSkillGrant, Guid> skillGrants,
        IRepository<AgentKnowledgeGrant, Guid> knowledgeGrants,
        ICurrentTenant? currentTenant = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
        : base(serviceProvider)
    {
        _toolGrants = Check.NotNull(toolGrants);
        _skillGrants = Check.NotNull(skillGrants);
        _knowledgeGrants = Check.NotNull(knowledgeGrants);
        _currentTenant = currentTenant;
        _multiTenancyOptions = multiTenancyOptions;
    }

    /// <summary>
    /// 读取侧的可见性：租户调用者还要看得见宿主级共享 Agent（YAML 定义）挂着的授权，
    /// 否则共享 Agent 在租户里解析出来没有任何工具。每次现算：租户上下文可在请求内被切换。
    /// 写入（reconcile / 启停 / 优先级）照常走全局过滤器 —— 租户不能改共享定义的授权。
    /// </summary>
    private SharedAgentScope ReadScope => SharedAgentScope.Resolve(_multiTenancyOptions, _currentTenant, CurrentUser);

    // =====================================================================
    // Projection (read)
    // =====================================================================

    public async Task<AgentGrantsProjection> GetGrantsAsync(Guid agentId, CancellationToken ct = default)
    {
        // 工具授权：一次查询拿全部已启用条目，内存中拆分 Group/Tool 并各自排序。
        var scope = ReadScope;
        var toolGrants = await scope.ApplyToAgentChildren(_toolGrants)
            .Where(g => g.AgentId == agentId && g.IsEnabled)
            .ToListAsync(ct);

        var toolGroups = toolGrants
            .Where(g => g.GrantType == GrantType.Group)
            .OrderByDescending(g => g.Priority)
            .ThenBy(g => g.CreationTime)
            .Select(g => g.ToolKey)
            .ToList();

        var toolNames = toolGrants
            .Where(g => g.GrantType == GrantType.Tool)
            .OrderByDescending(g => g.Priority)
            .ThenBy(g => g.CreationTime)
            .Select(g => g.ToolKey)
            .ToList();

        var skillGrants = await scope.ApplyToAgentChildren(_skillGrants)
            .Where(g => g.AgentId == agentId && g.IsEnabled)
            .ToListAsync(ct);

        var skillSlugs = skillGrants
            .OrderByDescending(g => g.Priority)
            .ThenBy(g => g.CreationTime)
            .Select(g => g.SkillSlug)
            .ToList();

        var knowledgeGrants = await scope.ApplyToAgentChildren(_knowledgeGrants)
            .Where(g => g.AgentId == agentId && g.IsEnabled)
            .ToListAsync(ct);

        var knowledgeBaseIds = knowledgeGrants
            .OrderByDescending(g => g.Priority)
            .ThenBy(g => g.CreationTime)
            .Select(g => g.KnowledgeBaseId)
            .ToList();

        return new AgentGrantsProjection
        {
            ToolGroups = toolGroups,
            ToolNames = toolNames,
            SkillSlugs = skillSlugs,
            KnowledgeBaseIds = knowledgeBaseIds
        };
    }

    public async Task<IReadOnlyDictionary<Guid, AgentGrantsProjection>> GetGrantsAsync(
        IReadOnlyList<Guid> agentIds, CancellationToken ct = default)
    {
        Check.NotNull(agentIds);

        var result = new Dictionary<Guid, AgentGrantsProjection>();
        if (agentIds.Count == 0) return result;

        var ids = agentIds.Distinct().ToList();

        // 三类 grant 表各一次 WHERE AgentId IN (...) 查询，避免按页逐 Agent 的 N+1。
        var scope = ReadScope;
        var toolGrants = await scope.ApplyToAgentChildren(_toolGrants)
            .Where(g => ids.Contains(g.AgentId) && g.IsEnabled)
            .ToListAsync(ct);
        var skillGrants = await scope.ApplyToAgentChildren(_skillGrants)
            .Where(g => ids.Contains(g.AgentId) && g.IsEnabled)
            .ToListAsync(ct);
        var knowledgeGrants = await scope.ApplyToAgentChildren(_knowledgeGrants)
            .Where(g => ids.Contains(g.AgentId) && g.IsEnabled)
            .ToListAsync(ct);

        // 内存按 AgentId 分组。
        var toolsByAgent = toolGrants.ToLookup(g => g.AgentId);
        var skillsByAgent = skillGrants.ToLookup(g => g.AgentId);
        var knowledgeByAgent = knowledgeGrants.ToLookup(g => g.AgentId);

        foreach (var id in ids)
        {
            var agentTools = toolsByAgent[id];
            result[id] = new AgentGrantsProjection
            {
                ToolGroups = OrderedKeys(agentTools.Where(g => g.GrantType == GrantType.Group), g => g.ToolKey),
                ToolNames = OrderedKeys(agentTools.Where(g => g.GrantType == GrantType.Tool), g => g.ToolKey),
                SkillSlugs = OrderedKeys(skillsByAgent[id], g => g.SkillSlug),
                KnowledgeBaseIds = OrderedKeys(knowledgeByAgent[id], g => g.KnowledgeBaseId)
            };
        }

        return result;
    }

    /// <summary>按 Priority 降序、再按创建时间升序投影出键列表（与 <see cref="GetGrantsAsync(Guid, CancellationToken)"/> 排序一致）。</summary>
    private static List<TKey> OrderedKeys<TGrant, TKey>(IEnumerable<TGrant> grants, Func<TGrant, TKey> keySelector)
        where TGrant : MultiTenantAuditedEntity<Guid>, IGrantEnableState
        => grants
            .OrderByDescending(g => g.Priority)
            .ThenBy(g => g.CreationTime)
            .Select(keySelector)
            .ToList();

    public async Task<AgentGrantListDto> ListGrantsAsync(Guid agentId, CancellationToken ct = default)
    {
        // 与 GetGrantsAsync 同一可见性，但不过滤 IsEnabled：治理面要看得见被禁用的条目才能重新启用或删除它。
        var scope = ReadScope;
        var toolGrants = await scope.ApplyToAgentChildren(_toolGrants)
            .Where(g => g.AgentId == agentId)
            .ToListAsync(ct);
        var skillGrants = await scope.ApplyToAgentChildren(_skillGrants)
            .Where(g => g.AgentId == agentId)
            .ToListAsync(ct);
        var knowledgeGrants = await scope.ApplyToAgentChildren(_knowledgeGrants)
            .Where(g => g.AgentId == agentId)
            .ToListAsync(ct);

        return new AgentGrantListDto
        {
            ToolGroups = ToGrantDtos(toolGrants.Where(g => g.GrantType == GrantType.Group), g => g.ToolKey),
            ToolNames = ToGrantDtos(toolGrants.Where(g => g.GrantType == GrantType.Tool), g => g.ToolKey),
            Skills = ToGrantDtos(skillGrants, g => g.SkillSlug),
            KnowledgeBases = ToGrantDtos(knowledgeGrants, g => g.KnowledgeBaseId.ToString())
        };
    }

    /// <summary>按与投影相同的顺序（Priority 降序、创建时间升序）映射为治理视图。</summary>
    private static List<AgentGrantDto> ToGrantDtos<TGrant>(IEnumerable<TGrant> grants, Func<TGrant, string> keySelector)
        where TGrant : MultiTenantAuditedEntity<Guid>, IGrantEnableState
        => grants
            .OrderByDescending(g => g.Priority)
            .ThenBy(g => g.CreationTime)
            .Select(g => new AgentGrantDto { Id = g.Id, Key = keySelector(g), IsEnabled = g.IsEnabled, Priority = g.Priority })
            .ToList();

    // =====================================================================
    // Reconcile (write) - tri-state PATCH semantics
    // =====================================================================

    public async Task ReconcileToolGroupsAsync(Guid agentId, IReadOnlyList<string>? groups, CancellationToken ct = default)
    {
        if (groups == null) return; // null → 不变

        // 当前 Group 授权（仅 GrantType=Group，永不触碰单工具 Tool 授权）。
        var current = await _toolGrants
            .ToListAsync(g => g.AgentId == agentId && g.GrantType == GrantType.Group, ct);

        var desired = Distinct(groups);
        var desiredSet = desired.ToHashSet(StringComparer.Ordinal);

        // 删除：当前已启用、目标无。禁用条目不在目标集中 = 原样保留（见接口上的 reconcile 说明）。
        var toRemove = current.Where(g => g.IsEnabled && !desiredSet.Contains(g.ToolKey)).ToList();
        foreach (var grant in toRemove)
            await _toolGrants.DeleteAsync(grant, ct);

        // 重新启用：目标包含、但现有条目被禁用 → 把"授权 X"的意图落实（否则 GetGrantsAsync 永远过滤掉它）。
        // 保留 Priority，仅翻转 IsEnabled。
        await ReEnableDisabledAsync(_toolGrants, current, g => desiredSet.Contains(g.ToolKey), ct);

        // 新增：目标有、当前无（保留两侧都有的现有条目，不动其 Priority/IsEnabled）。
        var existingKeys = current.Select(g => g.ToolKey).ToHashSet(StringComparer.Ordinal);
        var toInsert = desired
            .Where(key => !existingKeys.Contains(key))
            .Select(key => new AgentToolGrant
            {
                AgentId = agentId,
                GrantType = GrantType.Group,
                ToolKey = key,
                IsEnabled = true,
                Priority = 0
            })
            .ToList();

        if (toInsert.Count > 0)
            await _toolGrants.InsertManyAsync(toInsert, ct);
    }

    public async Task ReconcileToolNamesAsync(Guid agentId, IReadOnlyList<string>? toolNames, CancellationToken ct = default)
    {
        if (toolNames == null) return; // null → 不变

        // 当前单工具授权（仅 GrantType=Tool，永不触碰工具组 Group 授权）。
        var current = await _toolGrants
            .ToListAsync(g => g.AgentId == agentId && g.GrantType == GrantType.Tool, ct);

        var desired = Distinct(toolNames);
        var desiredSet = desired.ToHashSet(StringComparer.Ordinal);

        // 删除：当前已启用、目标无。禁用条目不在目标集中 = 原样保留（见接口上的 reconcile 说明）。
        var toRemove = current.Where(g => g.IsEnabled && !desiredSet.Contains(g.ToolKey)).ToList();
        foreach (var grant in toRemove)
            await _toolGrants.DeleteAsync(grant, ct);

        // 重新启用：目标包含、但现有条目被禁用（见 ReconcileToolGroupsAsync 说明）。
        await ReEnableDisabledAsync(_toolGrants, current, g => desiredSet.Contains(g.ToolKey), ct);

        // 新增：目标有、当前无（保留两侧都有的现有条目，不动其 Priority/IsEnabled）。
        var existingKeys = current.Select(g => g.ToolKey).ToHashSet(StringComparer.Ordinal);
        var toInsert = desired
            .Where(key => !existingKeys.Contains(key))
            .Select(key => new AgentToolGrant
            {
                AgentId = agentId,
                GrantType = GrantType.Tool,
                ToolKey = key,
                IsEnabled = true,
                Priority = 0
            })
            .ToList();

        if (toInsert.Count > 0)
            await _toolGrants.InsertManyAsync(toInsert, ct);
    }

    public async Task ReconcileSkillsAsync(Guid agentId, IReadOnlyList<string>? slugs, CancellationToken ct = default)
    {
        if (slugs == null) return;

        var current = await _skillGrants.ToListAsync(g => g.AgentId == agentId, ct);
        var desired = Distinct(slugs);
        var desiredSet = desired.ToHashSet(StringComparer.Ordinal);

        // 删除：当前已启用、目标无（禁用条目原样保留）。
        var toRemove = current.Where(g => g.IsEnabled && !desiredSet.Contains(g.SkillSlug)).ToList();
        foreach (var grant in toRemove)
            await _skillGrants.DeleteAsync(grant, ct);

        // 重新启用被禁用但仍在目标集中的现有条目（见 ReconcileToolGroupsAsync 说明）。
        await ReEnableDisabledAsync(_skillGrants, current, g => desiredSet.Contains(g.SkillSlug), ct);

        var existingKeys = current.Select(g => g.SkillSlug).ToHashSet(StringComparer.Ordinal);
        var toInsert = desired
            .Where(slug => !existingKeys.Contains(slug))
            .Select(slug => new AgentSkillGrant
            {
                AgentId = agentId,
                SkillSlug = slug,
                IsEnabled = true,
                Priority = 0
            })
            .ToList();

        if (toInsert.Count > 0)
            await _skillGrants.InsertManyAsync(toInsert, ct);
    }

    public async Task ReconcileKnowledgeAsync(Guid agentId, IReadOnlyList<Guid>? knowledgeBaseIds, CancellationToken ct = default)
    {
        if (knowledgeBaseIds == null) return;

        var current = await _knowledgeGrants.ToListAsync(g => g.AgentId == agentId, ct);
        var desired = knowledgeBaseIds.Distinct().ToList();
        var desiredSet = desired.ToHashSet();

        // 删除：当前已启用、目标无（禁用条目原样保留）。
        var toRemove = current.Where(g => g.IsEnabled && !desiredSet.Contains(g.KnowledgeBaseId)).ToList();
        foreach (var grant in toRemove)
            await _knowledgeGrants.DeleteAsync(grant, ct);

        // 重新启用被禁用但仍在目标集中的现有条目（见 ReconcileToolGroupsAsync 说明）。
        await ReEnableDisabledAsync(_knowledgeGrants, current, g => desiredSet.Contains(g.KnowledgeBaseId), ct);

        var existingKeys = current.Select(g => g.KnowledgeBaseId).ToHashSet();
        var toInsert = desired
            .Where(kbId => !existingKeys.Contains(kbId))
            .Select(kbId => new AgentKnowledgeGrant
            {
                AgentId = agentId,
                KnowledgeBaseId = kbId,
                IsEnabled = true,
                Priority = 0
            })
            .ToList();

        if (toInsert.Count > 0)
            await _knowledgeGrants.InsertManyAsync(toInsert, ct);
    }

    // =====================================================================
    // Per-grant CRUD (governance surface)
    // =====================================================================

    public async Task<bool> SetGrantEnabledAsync(GrantResourceType resourceType, Guid grantId, bool enabled, CancellationToken ct = default)
    {
        switch (resourceType)
        {
            case GrantResourceType.Tool:
                return await MutateAsync(_toolGrants, grantId, g => g.IsEnabled = enabled, ct);
            case GrantResourceType.Skill:
                return await MutateAsync(_skillGrants, grantId, g => g.IsEnabled = enabled, ct);
            case GrantResourceType.Knowledge:
                return await MutateAsync(_knowledgeGrants, grantId, g => g.IsEnabled = enabled, ct);
            default:
                return false;
        }
    }

    public async Task<bool> SetGrantPriorityAsync(GrantResourceType resourceType, Guid grantId, int priority, CancellationToken ct = default)
    {
        switch (resourceType)
        {
            case GrantResourceType.Tool:
                return await MutateAsync(_toolGrants, grantId, g => g.Priority = priority, ct);
            case GrantResourceType.Skill:
                return await MutateAsync(_skillGrants, grantId, g => g.Priority = priority, ct);
            case GrantResourceType.Knowledge:
                return await MutateAsync(_knowledgeGrants, grantId, g => g.Priority = priority, ct);
            default:
                return false;
        }
    }

    public async Task<bool> DeleteGrantAsync(GrantResourceType resourceType, Guid grantId, CancellationToken ct = default)
    {
        switch (resourceType)
        {
            case GrantResourceType.Tool:
                return await DeleteOneAsync(_toolGrants, grantId, ct);
            case GrantResourceType.Skill:
                return await DeleteOneAsync(_skillGrants, grantId, ct);
            case GrantResourceType.Knowledge:
                return await DeleteOneAsync(_knowledgeGrants, grantId, ct);
            default:
                return false;
        }
    }

    private static async Task<bool> DeleteOneAsync<TGrant>(IRepository<TGrant, Guid> repository, Guid grantId, CancellationToken ct)
        where TGrant : class, IEntity<Guid>
    {
        var grant = await repository.GetAsync(grantId, ct);
        if (grant == null) return false;

        await repository.DeleteAsync(grant, ct);
        return true;
    }

    private static async Task<bool> MutateAsync<TGrant>(
        IRepository<TGrant, Guid> repository, Guid grantId, Action<TGrant> mutate, CancellationToken ct)
        where TGrant : class, IEntity<Guid>
    {
        var grant = await repository.GetAsync(grantId, ct);
        if (grant == null) return false;

        mutate(grant);
        await repository.UpdateAsync(grant, ct);
        return true;
    }

    // =====================================================================
    // Reverse query - "which agents use resource X"
    // =====================================================================

    public async Task<IReadOnlyList<AgentGrantUsageDto>> GetAgentsUsingToolAsync(string toolKey, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(toolKey);
        // 经导航投影：Agent 的软删除过滤随 join 生效，已删除 Agent 的遗留 grant 行不会被算作「在用」。
        var rows = await _toolGrants
            .Where(g => g.ToolKey == toolKey && g.IsEnabled && g.Agent != null)
            .Select(g => new AgentGrantUsageDto { AgentId = g.AgentId, AgentName = g.Agent!.Name, AgentIsEnabled = g.Agent.IsEnabled })
            .ToListAsync(ct);
        return DistinctByAgent(rows);
    }

    public async Task<IReadOnlyList<AgentGrantUsageDto>> GetAgentsUsingSkillAsync(string skillSlug, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(skillSlug);
        var rows = await _skillGrants
            .Where(g => g.SkillSlug == skillSlug && g.IsEnabled && g.Agent != null)
            .Select(g => new AgentGrantUsageDto { AgentId = g.AgentId, AgentName = g.Agent!.Name, AgentIsEnabled = g.Agent.IsEnabled })
            .ToListAsync(ct);
        return DistinctByAgent(rows);
    }

    public async Task<IReadOnlyList<AgentGrantUsageDto>> GetAgentsUsingKnowledgeAsync(Guid knowledgeBaseId, CancellationToken ct = default)
    {
        var rows = await _knowledgeGrants
            .Where(g => g.KnowledgeBaseId == knowledgeBaseId && g.IsEnabled && g.Agent != null)
            .Select(g => new AgentGrantUsageDto { AgentId = g.AgentId, AgentName = g.Agent!.Name, AgentIsEnabled = g.Agent.IsEnabled })
            .ToListAsync(ct);
        return DistinctByAgent(rows);
    }

    /// <summary>同一 Agent 可能经工具组与单工具两条授权命中同一键，按 Agent 去重后按名称排序。</summary>
    private static List<AgentGrantUsageDto> DistinctByAgent(IEnumerable<AgentGrantUsageDto> rows)
        => rows
            .GroupBy(r => r.AgentId)
            .Select(g => g.First())
            .OrderBy(r => r.AgentName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.AgentId)
            .ToList();

    // =====================================================================
    // Helpers
    // =====================================================================

    /// <summary>
    /// 把 <paramref name="current"/> 中"被禁用但仍在目标集中"（<paramref name="isDesired"/> 为 true）的条目重新启用并更新。
    /// reconcile 语义 = "这个集合就是应当启用的授权"，故禁用态条目若仍被请求必须翻转回启用，否则 <c>GetGrantsAsync</c>（仅投影启用条目）会静默丢弃用户的授权意图。
    /// Re-enables any grant in <paramref name="current"/> that is disabled yet still in the desired set, then updates it.
    /// </summary>
    private static async Task ReEnableDisabledAsync<TGrant>(
        IRepository<TGrant, Guid> repository,
        IReadOnlyList<TGrant> current,
        Func<TGrant, bool> isDesired,
        CancellationToken ct)
        where TGrant : MultiTenantAuditedEntity<Guid>, IGrantEnableState
    {
        foreach (var grant in current)
        {
            if (grant.IsEnabled || !isDesired(grant)) continue;
            grant.IsEnabled = true;
            await repository.UpdateAsync(grant, ct);
        }
    }

    /// <summary>大小写敏感去重并保持首次出现顺序。Case-sensitive de-dup preserving first-seen order.</summary>
    private static List<string> Distinct(IReadOnlyList<string> values)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>(values.Count);
        foreach (var v in values)
        {
            if (seen.Add(v))
                result.Add(v);
        }
        return result;
    }
}
