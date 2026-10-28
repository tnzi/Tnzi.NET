namespace Tnzi.AI.Skills.Services;

/// <summary>
/// 数据库技能存储 - 加载当前租户级和用户级技能。
/// Scoped 生命周期，每次请求从数据库查询。
/// </summary>
public class DatabaseSkillStore : ISkillStore
{
    private readonly IRepository<SkillEntity, Guid> _repository;
    private readonly ICurrentUser? _currentUser;
    private readonly ICurrentTenant? _currentTenant;
    private readonly ILogger<DatabaseSkillStore> _logger;

    // Scoped 生命周期内缓存查询结果，避免同一请求多次 DB 查询
    private List<SkillDefinition>? _cachedResults;

    public DatabaseSkillStore(
        IRepository<SkillEntity, Guid> repository,
        ILogger<DatabaseSkillStore> logger,
        ICurrentUser? currentUser = null,
        ICurrentTenant? currentTenant = null)
    {
        _repository = Check.NotNull(repository);
        _logger = Check.NotNull(logger);
        _currentUser = currentUser;
        _currentTenant = currentTenant;
    }

    /// <inheritdoc/>
    public async Task<List<SkillDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        // Per-request cache: Scoped lifetime ensures cleanup after request ends
        if (_cachedResults != null)
            return _cachedResults;

        try
        {
            var tenantId = _currentTenant?.Id;
            var userId = _currentUser?.Id;

            // User-scope rows are now tenant-stamped (TenantId == current tenant);
            // older rows persisted before the fix carry null TenantId, so accept both
            // null and the current tenant for backward compatibility.
            var entities = await _repository
                .Where(e => e.Enabled &&
                    ((e.Scope == SkillScope.Tenant && e.TenantId == tenantId) ||
                     (e.Scope == SkillScope.User && e.OwnerUserId == userId
                        && (e.TenantId == null || e.TenantId == tenantId))))
                .ToListAsync(ct);

            _cachedResults = entities.Select(MapToDefinition).ToList();
            return _cachedResults;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load skills from database.");
            return [];
        }
    }

    /// <inheritdoc/>
    public Task<SkillDefinition?> GetBySlugAsync(string slug, CancellationToken ct = default)
        => GetBySlugAsync(slug, scope: null, ct);

    /// <summary>
    /// 按 slug 取当前调用者可见的那一份；<paramref name="scope"/> 非空时只在该作用域里找。
    /// </summary>
    /// <remarks>
    /// 同一个 slug 可以同时有一份 Tenant 技能与调用者自己的 User 技能：按 <see cref="SkillPrecedence"/> 选（Tenant 胜过 User，
    /// 同作用域 Priority 大者胜），与列表合并同一把尺子。★ 此前不排序地取第一行、也不看作用域前缀：
    /// <c>tenant:x</c> 能拿到调用者的个人版，不带前缀时拿到哪一份取决于行在库里的顺序，列表里显示的是租户版而激活的是个人版。
    /// </remarks>
    public async Task<SkillDefinition?> GetBySlugAsync(string slug, SkillScope? scope, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;

        // Check per-request cache first (populated by prior GetAllAsync call)
        if (_cachedResults != null)
            return SkillPrecedence.PickWinner(_cachedResults.Where(s =>
                string.Equals(s.Slug, slug, StringComparison.OrdinalIgnoreCase) && (scope == null || s.Scope == scope)));

        try
        {
            var tenantId = _currentTenant?.Id;
            var userId = _currentUser?.Id;

            var entities = await _repository
                .Where(e => e.Enabled && e.Slug.ToLower() == slug.ToLower() &&
                    ((e.Scope == SkillScope.Tenant && e.TenantId == tenantId) ||
                     (e.Scope == SkillScope.User && e.OwnerUserId == userId
                        && (e.TenantId == null || e.TenantId == tenantId))))
                .WhereIf(e => e.Scope == scope!.Value, scope.HasValue)
                .ToListAsync(ct);

            return SkillPrecedence.PickWinner(entities.Select(MapToDefinition));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load skill '{Slug}' from database.", slug);
            return null;
        }
    }

    /// <summary>丢掉本请求已经读过的结果：同一请求里写过数据库技能之后，下一次读要看到写入。</summary>
    internal void ClearRequestCache() => _cachedResults = null;

    // -------------------------------------------------------------------------
    // Mapping
    // -------------------------------------------------------------------------

    private static SkillDefinition MapToDefinition(SkillEntity entity)
    {
        var parameters = SkillJsonHelper.DeserializeOrDefault<List<SkillParameter>>(entity.ParametersJson) ?? [];
        var tags = SkillJsonHelper.DeserializeOrDefault<List<string>>(entity.TagsJson) ?? [];

        SkillConstraints? constraints = null;
        if (!string.IsNullOrWhiteSpace(entity.ConstraintsJson))
            constraints = SkillJsonHelper.DeserializeOrDefault<SkillConstraints>(entity.ConstraintsJson);

        SkillRequirements? requirements = null;
        if (!string.IsNullOrWhiteSpace(entity.RequirementsJson))
            requirements = SkillJsonHelper.DeserializeOrDefault<SkillRequirements>(entity.RequirementsJson);

        return new SkillDefinition
        {
            Slug = entity.Slug,
            Scope = entity.Scope,
            TenantId = entity.TenantId,
            OwnerUserId = entity.OwnerUserId,
            Name = entity.Name,
            Description = entity.Description,
            Content = entity.Content,
            WhenToUse = entity.WhenToUse,
            Parameters = parameters,
            Tags = tags,
            Requirements = requirements,
            Priority = entity.Priority,
            Version = entity.Version,
            Author = entity.Author,
            Enabled = entity.Enabled,
            Source = SkillSource.Database,
            AllowedToolGroups = constraints?.AllowedToolGroups,
            AllowedTools = constraints?.AllowedTools,
            DeniedTools = constraints?.DeniedTools,
            RequiredModel = constraints?.RequiredModel,
            RequiredProvider = constraints?.RequiredProvider
        };
    }

    /// <summary>Skill constraints internal serialization model</summary>
    private sealed class SkillConstraints
    {
        public List<string>? AllowedToolGroups { get; set; }
        public List<string>? AllowedTools { get; set; }
        public List<string>? DeniedTools { get; set; }
        public string? RequiredModel { get; set; }
        public string? RequiredProvider { get; set; }
    }
}
