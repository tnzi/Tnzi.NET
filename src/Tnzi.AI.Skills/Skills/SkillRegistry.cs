namespace Tnzi.AI.Skills;

/// <summary>
/// 技能注册表 - 合并 FileSystem（和未来的 Database）双源，按三层作用域优先级排重，支持关键词搜索。
/// </summary>
/// <remarks>
/// <para>作用域优先级：System &gt; Tenant &gt; User（数值越小优先级越高）。</para>
/// <para>同一作用域内 Priority 数值越大优先级越高。</para>
/// <para>Scoped 生命周期；自身不缓存，委托给各 Store 的缓存机制。</para>
/// </remarks>
public class SkillRegistry : ISkillRegistry
{
    private readonly FileSystemSkillStore _fileStore;
    private readonly ISkillSearchService _searchService;
    private readonly ILogger<SkillRegistry> _logger;
    private readonly DatabaseSkillStore? _dbStore;
    private readonly ICurrentUser? _currentUser;
    private readonly ICurrentTenant? _currentTenant;

    // Scoped 生命周期内缓存 merged 结果，避免同一请求重复 merge
    private IReadOnlyList<SkillDefinition>? _mergedCache;

    public SkillRegistry(
        FileSystemSkillStore fileStore,
        ISkillSearchService searchService,
        ILogger<SkillRegistry> logger,
        DatabaseSkillStore? dbStore = null,
        ICurrentUser? currentUser = null,
        ICurrentTenant? currentTenant = null)
    {
        _fileStore = Check.NotNull(fileStore);
        _searchService = Check.NotNull(searchService);
        _logger = Check.NotNull(logger);
        _dbStore = dbStore;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SkillDefinition>> GetAvailableSkillsAsync(CancellationToken ct = default)
    {
        if (_mergedCache != null)
            return _mergedCache;

        var fileSkills = await _fileStore.GetAllAsync(ct);
        var dbSkills = _dbStore != null ? await _dbStore.GetAllAsync(ct) : [];

        // FileSystemSkillStore 返回所有技能（包括 DataPath 中其他租户/用户的）
        // 按当前上下文过滤：System scope 全量返回，Tenant/User scope 按 ID 匹配
        var tenantId = _currentTenant?.Id;
        var userId = _currentUser?.Id;
        var filteredFileSkills = fileSkills.Where(s => IsAccessible(s, tenantId, userId));

        // DatabaseSkillStore 已内部按 tenant/user 过滤，无需额外处理
        var merged = MergeByPriority(filteredFileSkills.Concat(dbSkills));
        _mergedCache = merged;
        return merged;
    }

    /// <inheritdoc/>
    public async Task<SkillDefinition?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;

        // Parse optional scope prefix: "system:", "tenant:", "user:"
        var (scopeFilter, bareSlug) = ParseScopePrefix(slug);

        // Direct lookup: query each store directly instead of loading all skills
        SkillDefinition? fileResult = null;
        SkillDefinition? dbResult = null;

        // FileSystem store: System scope + DataPath scoped skills
        fileResult = await _fileStore.GetBySlugAsync(bareSlug, ct);
        // 过滤：确保当前用户有权访问此技能
        if (fileResult != null && !IsAccessible(fileResult, _currentTenant?.Id, _currentUser?.Id))
            fileResult = null;
        if (fileResult != null && scopeFilter.HasValue && fileResult.Scope != scopeFilter.Value)
            fileResult = null;

        // Database store: Tenant/User scope (already filtered internally)
        if (scopeFilter is null or SkillScope.Tenant or SkillScope.User)
            dbResult = _dbStore != null ? await _dbStore.GetBySlugAsync(bareSlug, ct) : null;

        // If only one returned, use it
        if (fileResult == null) return dbResult;
        if (dbResult == null) return fileResult;

        // Both returned - pick by scope priority (System=0 > Tenant=1 > User=2)
        return fileResult.Scope <= dbResult.Scope ? fileResult : dbResult;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SkillDefinition>> SearchAsync(
        string query, int maxResults = 10, CancellationToken ct = default)
    {
        var all = await GetAvailableSkillsAsync(ct);
        return await _searchService.SearchAsync(all, query, maxResults, ct);
    }

    /// <inheritdoc/>
    public void InvalidateCache()
    {
        _fileStore.InvalidateCache();
        _mergedCache = null;
    }

    // -------------------------------------------------------------------------
    // Merge logic
    // -------------------------------------------------------------------------

    /// <summary>
    /// 按 slug 去重，同一 slug 多来源时：作用域 System &gt; Tenant &gt; User；
    /// 同作用域内 Priority 越大越优先。
    /// </summary>
    private static List<SkillDefinition> MergeByPriority(IEnumerable<SkillDefinition> skills)
    {
        // Group by slug (case-insensitive), then pick winner per group
        var result = new Dictionary<string, SkillDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var skill in skills)
        {
            if (!result.TryGetValue(skill.Slug, out var existing))
            {
                result[skill.Slug] = skill;
                continue;
            }

            // Prefer lower Scope ordinal (System=0 beats Tenant=1 beats User=2)
            if (skill.Scope < existing.Scope)
            {
                result[skill.Slug] = skill;
            }
            else if (skill.Scope == existing.Scope && skill.Priority > existing.Priority)
            {
                result[skill.Slug] = skill;
            }
        }

        return [.. result.Values];
    }

    /// <summary>
    /// 检查技能是否对当前租户/用户可访问。
    /// System scope 始终可访问；Tenant scope 需匹配 tenantId（无租户模块时不可见）；
    /// User scope 匹配 userId（租户可选：有租户则额外匹配 tenantId）。
    /// </summary>
    private static bool IsAccessible(SkillDefinition skill, Guid? currentTenantId, Guid? currentUserId)
    {
        return skill.Scope switch
        {
            SkillScope.System => true,

            // Tenant scope: 需要租户模块启用且 ID 匹配
            SkillScope.Tenant => currentTenantId.HasValue
                                 && skill.TenantId.HasValue
                                 && skill.TenantId == currentTenantId,

            // User scope: 匹配 userId；如果技能关联了 tenantId 则还需匹配租户
            SkillScope.User => currentUserId.HasValue
                               && skill.OwnerUserId.HasValue
                               && skill.OwnerUserId == currentUserId
                               && (!skill.TenantId.HasValue || skill.TenantId == currentTenantId),

            _ => true
        };
    }

    /// <summary>
    /// Parse optional scope prefix from slug: "system:", "tenant:", "user:"
    /// </summary>
    private static (SkillScope? Scope, string BareSlug) ParseScopePrefix(string slug)
    {
        if (slug.StartsWith("system:", StringComparison.OrdinalIgnoreCase))
            return (SkillScope.System, slug["system:".Length..]);

        if (slug.StartsWith("tenant:", StringComparison.OrdinalIgnoreCase))
            return (SkillScope.Tenant, slug["tenant:".Length..]);

        if (slug.StartsWith("user:", StringComparison.OrdinalIgnoreCase))
            return (SkillScope.User, slug["user:".Length..]);

        return (null, slug);
    }
}
