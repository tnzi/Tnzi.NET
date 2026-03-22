namespace Tnzi.AI.Services;

/// <summary>
/// 技能管理服务实现
/// </summary>
public class SkillService : ApplicationService, ISkillService
{
    private static readonly Regex SlugPattern = new(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?$", RegexOptions.Compiled);

    private readonly IRepository<SkillEntity, Guid> _repository;
    private readonly ISkillRegistry _registry;
    private readonly ISkillTemplateEngine _templateEngine;
    private readonly FileSystemSkillStore _fileStore;
    private readonly ISkillRequirementsValidator? _requirementsValidator;

    public SkillService(
        IServiceProvider serviceProvider,
        IRepository<SkillEntity, Guid> repository,
        ISkillRegistry registry,
        ISkillTemplateEngine templateEngine,
        FileSystemSkillStore fileStore,
        ISkillRequirementsValidator? requirementsValidator = null)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _registry = Check.NotNull(registry);
        _templateEngine = Check.NotNull(templateEngine);
        _fileStore = Check.NotNull(fileStore);
        _requirementsValidator = requirementsValidator;
    }

    public async Task<Result<List<SkillSummaryDto>>> GetAvailableAsync()
    {
        var skills = await _registry.GetAvailableSkillsAsync();
        var dtos = skills.MapToList<SkillSummaryDto>();
        return Ok(dtos);
    }

    public async Task<Result<SkillDetailDto>> GetBySlugAsync(string slug)
    {
        Check.NotNullOrWhiteSpace(slug);

        var skill = await _registry.GetBySlugAsync(slug);
        if (skill == null)
            return Fail<SkillDetailDto>("Skill not found.", 404, ErrorCodes.SkillNotFound);

        return Ok(skill.MapTo<SkillDetailDto>());
    }

    public async Task<Result<List<SkillSummaryDto>>> SearchAsync(string query, int maxResults = 10)
    {
        Check.NotNullOrWhiteSpace(query);

        var skills = await _registry.SearchAsync(query, maxResults);
        var dtos = skills.MapToList<SkillSummaryDto>();
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

        // Validate requirements (bins, envs, configs, os, toolGroups)
        if (_requirementsValidator != null)
        {
            var validation = _requirementsValidator.ValidateRequirements(skill);
            if (!validation.IsValid)
                return Fail<SkillActivationResult>($"Skill requirements not met: {validation.GetFailureReason()}", 400, ErrorCodes.SkillActivationFailed);
        }

        var renderResult = _templateEngine.Render(skill, parameters);
        if (!renderResult.Success)
        {
            var errorMsg = string.Join("; ", renderResult.Errors);
            return Fail<SkillActivationResult>($"Skill activation failed: {errorMsg}", 400, ErrorCodes.SkillActivationFailed);
        }

        var warnings = new List<string>(renderResult.Errors);
        if (renderResult.UnusedParameters.Count > 0)
            warnings.Add($"Unused parameters: {string.Join(", ", renderResult.UnusedParameters)}");

        // Publish usage tracking event
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new AI.Events.SkillActivatedEvent
            {
                Slug = skill.Slug,
                Scope = skill.Scope,
                Source = skill.Source,
                ActivatedAt = DateTime.UtcNow,
                UserId = CurrentUser?.Id
            });
        }

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
            ParametersJson = SkillJsonHelper.SerializeOrDefault(input.Parameters, "[]"),
            ConstraintsJson = SkillJsonHelper.BuildConstraintsJson(input.AllowedToolGroups, input.AllowedTools, input.DeniedTools, input.RequiredModel, input.RequiredProvider),
            RequirementsJson = input.Requirements != null ? JsonSerializer.Serialize(input.Requirements, TnziJsonDefaults.Options) : null,
            TagsJson = input.Tags != null ? JsonSerializer.Serialize(input.Tags, TnziJsonDefaults.Options) : null,
            Priority = input.Priority,
            Version = input.Version,
            Author = input.Author,
            Enabled = input.Enabled
        };

        await _repository.InsertAsync(entity);

        _registry.InvalidateCache();

        return Ok(MapEntityToDetailDto(entity));
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
        if (input.Parameters != null) entity.ParametersJson = JsonSerializer.Serialize(input.Parameters, TnziJsonDefaults.Options);
        if (input.AllowedToolGroups != null || input.AllowedTools != null || input.DeniedTools != null
            || input.RequiredModel != null || input.RequiredProvider != null)
            entity.ConstraintsJson = SkillJsonHelper.MergeConstraintsJson(entity.ConstraintsJson, input.AllowedToolGroups, input.AllowedTools, input.DeniedTools, input.RequiredModel, input.RequiredProvider);
        if (input.Requirements != null) entity.RequirementsJson = JsonSerializer.Serialize(input.Requirements, TnziJsonDefaults.Options);
        if (input.Tags != null) entity.TagsJson = JsonSerializer.Serialize(input.Tags, TnziJsonDefaults.Options);
        if (input.Priority.HasValue) entity.Priority = input.Priority.Value;
        if (input.Version != null) entity.Version = input.Version;
        if (input.Author != null) entity.Author = input.Author;
        if (input.Enabled.HasValue) entity.Enabled = input.Enabled.Value;

        await _repository.UpdateAsync(entity);

        _registry.InvalidateCache();

        return Ok(MapEntityToDetailDto(entity));
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

    /// <summary>
    /// Maps a SkillEntity to SkillDetailDto (with entity-specific fields: Id, OwnerUserId, timestamps).
    /// Uses Mapster for SkillDefinition→SkillDetailDto base mapping, then overlays entity fields.
    /// </summary>
    private static SkillDetailDto MapEntityToDetailDto(SkillEntity entity)
    {
        var definition = MapEntityToDefinition(entity);
        var dto = definition.MapTo<SkillDetailDto>();
        dto.Id = entity.Id;
        dto.OwnerUserId = entity.OwnerUserId;
        dto.CreationTime = entity.CreationTime;
        dto.LastModificationTime = entity.LastModificationTime;
        return dto;
    }

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
            Parameters = SkillJsonHelper.DeserializeOrDefault<List<SkillParameter>>(entity.ParametersJson) ?? [],
            Tags = SkillJsonHelper.DeserializeOrDefault<List<string>>(entity.TagsJson) ?? [],
            Requirements = SkillJsonHelper.DeserializeOrDefault<SkillRequirements>(entity.RequirementsJson),
            Priority = entity.Priority,
            Version = entity.Version,
            Author = entity.Author,
            Enabled = entity.Enabled,
            Source = SkillSource.Database,
            AllowedToolGroups = SkillJsonHelper.ParseConstraintField<List<string>>(entity.ConstraintsJson, "allowedToolGroups"),
            RequiredModel = SkillJsonHelper.ParseConstraintField<string>(entity.ConstraintsJson, "requiredModel"),
            RequiredProvider = SkillJsonHelper.ParseConstraintField<string>(entity.ConstraintsJson, "requiredProvider")
        };
    }
}
