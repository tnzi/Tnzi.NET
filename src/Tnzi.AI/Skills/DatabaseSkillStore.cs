namespace Tnzi.AI.Skills;

/// <summary>
/// 数据库技能存储 — 加载当前租户级和用户级技能。
/// Scoped 生命周期，每次请求从数据库查询。
/// </summary>
public class DatabaseSkillStore : ISkillStore
{
    private readonly IRepository<SkillEntity, Guid> _repository;
    private readonly ICurrentUser? _currentUser;
    private readonly ICurrentTenant? _currentTenant;
    private readonly ILogger<DatabaseSkillStore> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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
        try
        {
            var tenantId = _currentTenant?.Id;
            var userId = _currentUser?.Id;

            var entities = await _repository
                .Where(e => e.Enabled &&
                    ((e.Scope == SkillScope.Tenant && e.TenantId == tenantId) ||
                     (e.Scope == SkillScope.User && e.OwnerUserId == userId)))
                .ToListAsync(ct);

            return entities.Select(MapToDefinition).ToList();
        }
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

        try
        {
            var tenantId = _currentTenant?.Id;
            var userId = _currentUser?.Id;

            var entity = await _repository
                .Where(e => e.Enabled && e.Slug == slug &&
                    ((e.Scope == SkillScope.Tenant && e.TenantId == tenantId) ||
                     (e.Scope == SkillScope.User && e.OwnerUserId == userId)))
                .FirstOrDefaultAsync(ct);

            return entity == null ? null : MapToDefinition(entity);
        }
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
        var parameters = DeserializeOrDefault<List<SkillParameter>>(entity.ParametersJson) ?? [];
        var tags = DeserializeOrDefault<List<string>>(entity.TagsJson) ?? [];

        SkillConstraints? constraints = null;
        if (!string.IsNullOrWhiteSpace(entity.ConstraintsJson))
        {
            constraints = DeserializeOrDefault<SkillConstraints>(entity.ConstraintsJson);
        }

        SkillRequirements? requirements = null;
        if (!string.IsNullOrWhiteSpace(entity.RequirementsJson))
        {
            requirements = DeserializeOrDefault<SkillRequirements>(entity.RequirementsJson);
        }

        return new SkillDefinition
        {
            Slug = entity.Slug,
            Scope = entity.Scope,
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
            RequiredModel = constraints?.RequiredModel,
            RequiredProvider = constraints?.RequiredProvider
        };
    }

    private static T? DeserializeOrDefault<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    /// <summary>技能约束内部序列化模型</summary>
    private sealed class SkillConstraints
    {
        public List<string>? AllowedToolGroups { get; set; }
        public string? RequiredModel { get; set; }
        public string? RequiredProvider { get; set; }
    }
}
