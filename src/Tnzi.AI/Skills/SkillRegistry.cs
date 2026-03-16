namespace Tnzi.AI.Skills;

/// <summary>
/// 技能注册表 — 合并 FileSystem（和未来的 Database）双源，按三层作用域优先级排重，支持关键词搜索。
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

    public SkillRegistry(
        FileSystemSkillStore fileStore,
        ISkillSearchService searchService,
        ILogger<SkillRegistry> logger,
        DatabaseSkillStore? dbStore = null)
    {
        _fileStore = Check.NotNull(fileStore);
        _searchService = Check.NotNull(searchService);
        _logger = Check.NotNull(logger);
        _dbStore = dbStore;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SkillDefinition>> GetAvailableSkillsAsync(CancellationToken ct = default)
    {
        var fileSkills = await _fileStore.GetAllAsync(ct);
        var dbSkills = _dbStore != null ? await _dbStore.GetAllAsync(ct) : [];

        var merged = MergeByPriority(fileSkills.Concat(dbSkills));
        return merged;
    }

    /// <inheritdoc/>
    public async Task<SkillDefinition?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        // Parse optional scope prefix: "system:", "tenant:", "user:"
        SkillScope? scopeFilter = null;
        var bareSlug = slug;

        if (slug.StartsWith("system:", StringComparison.OrdinalIgnoreCase))
        {
            scopeFilter = SkillScope.System;
            bareSlug = slug.Substring("system:".Length);
        }
        else if (slug.StartsWith("tenant:", StringComparison.OrdinalIgnoreCase))
        {
            scopeFilter = SkillScope.Tenant;
            bareSlug = slug.Substring("tenant:".Length);
        }
        else if (slug.StartsWith("user:", StringComparison.OrdinalIgnoreCase))
        {
            scopeFilter = SkillScope.User;
            bareSlug = slug.Substring("user:".Length);
        }

        var all = await GetAvailableSkillsAsync(ct);

        IEnumerable<SkillDefinition> filtered = all.Where(s =>
            string.Equals(s.Slug, bareSlug, StringComparison.OrdinalIgnoreCase));

        if (scopeFilter.HasValue)
        {
            filtered = filtered.Where(s => s.Scope == scopeFilter.Value);
        }

        // Scope priority: System(0) > Tenant(1) > User(2) — OrderBy ascending returns System first
        return filtered.OrderBy(s => s.Scope).FirstOrDefault();
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
}
