namespace Tnzi.AI.Skills;

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
    public async Task<SkillDefinition?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;

        // Check per-request cache first (populated by prior GetAllAsync call)
        if (_cachedResults != null)
            return _cachedResults.FirstOrDefault(s => string.Equals(s.Slug, slug, StringComparison.OrdinalIgnoreCase));

        try
        {
            var tenantId = _currentTenant?.Id;
            var userId = _currentUser?.Id;

            var entity = await _repository
                .Where(e => e.Enabled && e.Slug.ToLower() == slug.ToLower() &&
                    ((e.Scope == SkillScope.Tenant && e.TenantId == tenantId) ||
                     (e.Scope == SkillScope.User && e.OwnerUserId == userId
                        && (e.TenantId == null || e.TenantId == tenantId))))
                .FirstOrDefaultAsync(ct);

            return entity == null ? null : MapToDefinition(entity);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load skill '{Slug}' from database.", slug);
            return null;
        }
    }

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
