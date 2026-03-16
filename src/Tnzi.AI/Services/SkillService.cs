namespace Tnzi.AI.Services;

/// <summary>
/// 技能管理服务实现
/// </summary>
public class SkillService : ApplicationService, ISkillService
{
    private static readonly Regex SlugPattern = new(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IRepository<SkillEntity, Guid> _repository;
    private readonly ISkillRegistry _registry;
    private readonly ISkillTemplateEngine _templateEngine;
    private readonly FileSystemSkillStore _fileStore;

    public SkillService(
        IServiceProvider serviceProvider,
        IRepository<SkillEntity, Guid> repository,
        ISkillRegistry registry,
        ISkillTemplateEngine templateEngine,
        FileSystemSkillStore fileStore)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _registry = Check.NotNull(registry);
        _templateEngine = Check.NotNull(templateEngine);
        _fileStore = Check.NotNull(fileStore);
    }

    public async Task<Result<List<SkillSummaryDto>>> GetAvailableAsync()
    {
        var skills = await _registry.GetAvailableSkillsAsync();
        var dtos = skills.Select(MapToSummaryDto).ToList();
        return Ok(dtos);
    }

    public async Task<Result<SkillDetailDto>> GetBySlugAsync(string slug)
    {
        Check.NotNullOrWhiteSpace(slug);

        var skill = await _registry.GetBySlugAsync(slug);
        if (skill == null)
            return Fail<SkillDetailDto>("Skill not found.", 404, ErrorCodes.SkillNotFound);

        return Ok(MapToDetailDto(skill, null));
    }

    public async Task<Result<List<SkillSummaryDto>>> SearchAsync(string query, int maxResults = 10)
    {
        Check.NotNullOrWhiteSpace(query);

        var skills = await _registry.SearchAsync(query, maxResults);
        var dtos = skills.Select(MapToSummaryDto).ToList();
        return Ok(dtos);
    }

    public async Task<Result<SkillActivationResult>> ActivateAsync(string slug, Dictionary<string, string>? parameters = null)
    {
        Check.NotNullOrWhiteSpace(slug);

        var skill = await _registry.GetBySlugAsync(slug);
        if (skill == null)
            return Fail<SkillActivationResult>("Skill not found.", 404, ErrorCodes.SkillNotFound);

        if (!skill.Enabled)
            return Fail<SkillActivationResult>("Skill is disabled.", 400, ErrorCodes.SkillDisabled);

        var renderResult = _templateEngine.Render(skill, parameters);
        if (!renderResult.Success)
        {
            var errorMsg = string.Join("; ", renderResult.Errors);
            return Fail<SkillActivationResult>($"Skill activation failed: {errorMsg}", 400, ErrorCodes.SkillActivationFailed);
        }

        var warnings = new List<string>(renderResult.Errors);
        if (renderResult.UnusedParameters.Count > 0)
            warnings.Add($"Unused parameters: {string.Join(", ", renderResult.UnusedParameters)}");

        return Ok(new SkillActivationResult
        {
            Slug = skill.Slug,
            Name = skill.Name,
            RenderedContent = renderResult.RenderedContent,
            AllowedToolGroups = skill.AllowedToolGroups,
            RequiredModel = skill.RequiredModel,
            RequiredProvider = skill.RequiredProvider,
            Warnings = warnings
        });
    }

    public async Task<Result<SkillDetailDto>> CreateAsync(CreateSkillDto input)
    {
        Check.NotNull(input);

        // Validate slug format
        if (string.IsNullOrWhiteSpace(input.Slug) || input.Slug.Length > 64 || !SlugPattern.IsMatch(input.Slug))
            return Fail<SkillDetailDto>("Slug must be 1-64 lowercase letters, digits, or hyphens (cannot start or end with a hyphen).", 400, ErrorCodes.SkillInvalidSlug);

        // Check slug doesn't conflict with system (FileSystem) skills
        var systemSkill = await _fileStore.GetBySlugAsync(input.Slug);
        if (systemSkill != null)
            return Fail<SkillDetailDto>($"Slug '{input.Slug}' is reserved by a system skill.", 409, ErrorCodes.SkillSlugConflict);

        // Determine owner
        Guid? ownerUserId = null;
        if (input.Scope == SkillScope.User)
        {
            var userId = CurrentUser?.Id;
            if (!userId.HasValue)
                return Fail<SkillDetailDto>("Authentication required to create a user-scoped skill.", 401, ErrorCodes.SkillUnauthorized);
            ownerUserId = userId.Value;
        }

        // Check for duplicate slug in same scope/tenant/user
        var duplicate = await _repository.AnyAsync(e =>
            e.Slug == input.Slug &&
            e.Scope == input.Scope &&
            e.OwnerUserId == ownerUserId);

        if (duplicate)
            return Fail<SkillDetailDto>($"A skill with slug '{input.Slug}' already exists in this scope.", 409, ErrorCodes.SkillSlugConflict);

        var entity = new SkillEntity
        {
            Slug = input.Slug,
            Scope = input.Scope,
            OwnerUserId = ownerUserId,
            Name = input.Name,
            Description = input.Description,
            Content = input.Content,
            WhenToUse = input.WhenToUse,
            ParametersJson = SerializeOrDefault(input.Parameters, "[]"),
            ConstraintsJson = BuildConstraintsJson(input.AllowedToolGroups, input.RequiredModel, input.RequiredProvider),
            RequirementsJson = input.Requirements != null ? JsonSerializer.Serialize(input.Requirements, JsonOptions) : null,
            TagsJson = input.Tags != null ? JsonSerializer.Serialize(input.Tags, JsonOptions) : null,
            Priority = input.Priority,
            Version = input.Version,
            Author = input.Author,
            Enabled = input.Enabled
        };

        await _repository.InsertAsync(entity);

        _registry.InvalidateCache();

        return Ok(MapToDetailDto(MapEntityToDefinition(entity), entity.Id, entity.OwnerUserId, entity.CreationTime, entity.LastModificationTime));
    }

    public async Task<Result<SkillDetailDto>> UpdateAsync(Guid id, UpdateSkillDto input)
    {
        Check.NotNull(input);

        var entity = await _repository.GetAsync(id);
        if (entity == null)
            return Fail<SkillDetailDto>("Skill not found.", 404, ErrorCodes.SkillNotFound);

        // Ownership check for user-scoped skills
        if (entity.Scope == SkillScope.User)
        {
            var userId = CurrentUser?.Id;
            if (entity.OwnerUserId != userId)
                return Fail<SkillDetailDto>("Access denied.", 403, ErrorCodes.SkillUnauthorized);
        }

        if (input.Name != null) entity.Name = input.Name;
        if (input.Description != null) entity.Description = input.Description;
        if (input.Content != null) entity.Content = input.Content;
        if (input.WhenToUse != null) entity.WhenToUse = input.WhenToUse;
        if (input.Parameters != null) entity.ParametersJson = JsonSerializer.Serialize(input.Parameters, JsonOptions);
        if (input.AllowedToolGroups != null || input.RequiredModel != null || input.RequiredProvider != null)
            entity.ConstraintsJson = BuildConstraintsJson(input.AllowedToolGroups, input.RequiredModel, input.RequiredProvider);
        if (input.Requirements != null) entity.RequirementsJson = JsonSerializer.Serialize(input.Requirements, JsonOptions);
        if (input.Tags != null) entity.TagsJson = JsonSerializer.Serialize(input.Tags, JsonOptions);
        if (input.Priority.HasValue) entity.Priority = input.Priority.Value;
        if (input.Version != null) entity.Version = input.Version;
        if (input.Author != null) entity.Author = input.Author;
        if (input.Enabled.HasValue) entity.Enabled = input.Enabled.Value;

        await _repository.UpdateAsync(entity);

        _registry.InvalidateCache();

        return Ok(MapToDetailDto(MapEntityToDefinition(entity), entity.Id, entity.OwnerUserId, entity.CreationTime, entity.LastModificationTime));
    }

    public async Task<Result> DeleteAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (entity == null)
            return Fail("Skill not found.", 404, ErrorCodes.SkillNotFound);

        // Ownership check for user-scoped skills
        if (entity.Scope == SkillScope.User)
        {
            var userId = CurrentUser?.Id;
            if (entity.OwnerUserId != userId)
                return Fail("Access denied.", 403, ErrorCodes.SkillUnauthorized);
        }

        await _repository.DeleteAsync(entity);

        _registry.InvalidateCache();

        return Ok();
    }

    // -------------------------------------------------------------------------
    // Mapping helpers
    // -------------------------------------------------------------------------

    private static SkillSummaryDto MapToSummaryDto(SkillDefinition skill) => new()
    {
        Slug = skill.Slug,
        Scope = skill.Scope,
        Name = skill.Name,
        Description = skill.Description,
        WhenToUse = skill.WhenToUse,
        Tags = skill.Tags,
        Priority = skill.Priority,
        Version = skill.Version,
        Author = skill.Author,
        Enabled = skill.Enabled,
        Source = skill.Source
    };

    private static SkillDetailDto MapToDetailDto(SkillDefinition skill, Guid? entityId, Guid? ownerUserId = null, DateTime creationTime = default, DateTime? lastModificationTime = null) => new()
    {
        Id = entityId ?? Guid.Empty,
        Slug = skill.Slug,
        Scope = skill.Scope,
        Name = skill.Name,
        Description = skill.Description,
        Content = skill.Content,
        WhenToUse = skill.WhenToUse,
        Parameters = skill.Parameters,
        AllowedToolGroups = skill.AllowedToolGroups,
        RequiredModel = skill.RequiredModel,
        RequiredProvider = skill.RequiredProvider,
        Requirements = skill.Requirements,
        Tags = skill.Tags,
        Priority = skill.Priority,
        Version = skill.Version,
        Author = skill.Author,
        Enabled = skill.Enabled,
        Source = skill.Source,
        OwnerUserId = ownerUserId,
        CreationTime = creationTime,
        LastModificationTime = lastModificationTime
    };

    private static SkillDetailDto MapToDetailDto(SkillDefinition skill, Guid? entityId) =>
        MapToDetailDto(skill, entityId, null, default, null);

    private static SkillDefinition MapEntityToDefinition(SkillEntity entity)
    {
        return new SkillDefinition
        {
            Slug = entity.Slug,
            Scope = entity.Scope,
            Name = entity.Name,
            Description = entity.Description,
            Content = entity.Content,
            WhenToUse = entity.WhenToUse,
            Parameters = DeserializeOrDefault<List<SkillParameter>>(entity.ParametersJson) ?? [],
            Tags = DeserializeOrDefault<List<string>>(entity.TagsJson) ?? [],
            Requirements = DeserializeOrDefault<SkillRequirements>(entity.RequirementsJson),
            Priority = entity.Priority,
            Version = entity.Version,
            Author = entity.Author,
            Enabled = entity.Enabled,
            Source = SkillSource.Database,
            AllowedToolGroups = ParseConstraintField<List<string>>(entity.ConstraintsJson, "allowedToolGroups"),
            RequiredModel = ParseConstraintField<string>(entity.ConstraintsJson, "requiredModel"),
            RequiredProvider = ParseConstraintField<string>(entity.ConstraintsJson, "requiredProvider")
        };
    }

    private static string SerializeOrDefault<T>(T? value, string defaultJson) where T : class =>
        value != null ? JsonSerializer.Serialize(value, JsonOptions) : defaultJson;

    private static string? BuildConstraintsJson(List<string>? allowedToolGroups, string? requiredModel, string? requiredProvider)
    {
        if (allowedToolGroups == null && requiredModel == null && requiredProvider == null)
            return null;

        var obj = new Dictionary<string, object?>();
        if (allowedToolGroups != null) obj["allowedToolGroups"] = allowedToolGroups;
        if (requiredModel != null) obj["requiredModel"] = requiredModel;
        if (requiredProvider != null) obj["requiredProvider"] = requiredProvider;

        return JsonSerializer.Serialize(obj, JsonOptions);
    }

    private static T? DeserializeOrDefault<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch (JsonException) { return default; }
    }

    private static T? ParseConstraintField<T>(string? constraintsJson, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(constraintsJson)) return default;
        try
        {
            using var doc = JsonDocument.Parse(constraintsJson);
            if (doc.RootElement.TryGetProperty(fieldName, out var prop))
                return JsonSerializer.Deserialize<T>(prop.GetRawText(), JsonOptions);
        }
        catch (JsonException) { }
        return default;
    }
}
