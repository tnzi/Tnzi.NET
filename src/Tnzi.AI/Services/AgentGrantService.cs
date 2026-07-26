namespace Tnzi.AI.Services;

/// <summary>
/// Agent 授权服务实现 - 管理 Agent ↔ 工具/技能/知识库的 junction grant。
/// 见 <see cref="IAgentGrantService"/>。reconcile 复刻原 UpdateAgentDto 的 tri-state PATCH 语义，
/// 删除走仓储软删除（<c>DeleteAsync</c>），保留两侧都存在的现有条目以不丢失 admin 设置的 Priority/IsEnabled。
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

    public AgentGrantService(
        IServiceProvider serviceProvider,
        IRepository<AgentToolGrant, Guid> toolGrants,
        IRepository<AgentSkillGrant, Guid> skillGrants,
        IRepository<AgentKnowledgeGrant, Guid> knowledgeGrants)
        : base(serviceProvider)
    {
        _toolGrants = Check.NotNull(toolGrants);
        _skillGrants = Check.NotNull(skillGrants);
        _knowledgeGrants = Check.NotNull(knowledgeGrants);
    }

    // =====================================================================
    // Projection (read)
    // =====================================================================

    public async Task<AgentGrantsProjection> GetGrantsAsync(Guid agentId, CancellationToken ct = default)
    {
        // 工具授权：一次查询拿全部已启用条目，内存中拆分 Group/Tool 并各自排序。
        var toolGrants = await _toolGrants
            .ToListAsync(g => g.AgentId == agentId && g.IsEnabled, ct);

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

        var skillGrants = await _skillGrants
            .ToListAsync(g => g.AgentId == agentId && g.IsEnabled, ct);

        var skillSlugs = skillGrants
            .OrderByDescending(g => g.Priority)
            .ThenBy(g => g.CreationTime)
            .Select(g => g.SkillSlug)
            .ToList();

        var knowledgeGrants = await _knowledgeGrants
            .ToListAsync(g => g.AgentId == agentId && g.IsEnabled, ct);

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
        var toolGrants = await _toolGrants
            .ToListAsync(g => ids.Contains(g.AgentId) && g.IsEnabled, ct);
        var skillGrants = await _skillGrants
            .ToListAsync(g => ids.Contains(g.AgentId) && g.IsEnabled, ct);
        var knowledgeGrants = await _knowledgeGrants
            .ToListAsync(g => ids.Contains(g.AgentId) && g.IsEnabled, ct);

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

        // 删除：当前有、目标无。
        var toRemove = current.Where(g => !desiredSet.Contains(g.ToolKey)).ToList();
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

        // 删除：当前有、目标无。
        var toRemove = current.Where(g => !desiredSet.Contains(g.ToolKey)).ToList();
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

        var toRemove = current.Where(g => !desiredSet.Contains(g.SkillSlug)).ToList();
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

        var toRemove = current.Where(g => !desiredSet.Contains(g.KnowledgeBaseId)).ToList();
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

    public async Task<IReadOnlyList<Guid>> GetAgentsUsingToolAsync(string toolKey, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(toolKey);
        var grants = await _toolGrants.ToListAsync(g => g.ToolKey == toolKey && g.IsEnabled, ct);
        return grants.Select(g => g.AgentId).Distinct().ToList();
    }

    public async Task<IReadOnlyList<Guid>> GetAgentsUsingSkillAsync(string skillSlug, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(skillSlug);
        var grants = await _skillGrants.ToListAsync(g => g.SkillSlug == skillSlug && g.IsEnabled, ct);
        return grants.Select(g => g.AgentId).Distinct().ToList();
    }

    public async Task<IReadOnlyList<Guid>> GetAgentsUsingKnowledgeAsync(Guid knowledgeBaseId, CancellationToken ct = default)
    {
        var grants = await _knowledgeGrants.ToListAsync(g => g.KnowledgeBaseId == knowledgeBaseId && g.IsEnabled, ct);
        return grants.Select(g => g.AgentId).Distinct().ToList();
    }

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
