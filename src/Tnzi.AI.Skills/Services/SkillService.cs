namespace Tnzi.AI.Skills.Services;

/// <summary>
/// 技能管理服务实现
/// </summary>
public partial class SkillService : ApplicationService, ISkillService
{
    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?$")]
    private static partial Regex SlugPattern();

    private readonly IRepository<SkillEntity, Guid> _repository;
    private readonly ISkillRegistry _registry;
    private readonly ISkillTemplateEngine _templateEngine;
    private readonly FileSystemSkillStore _fileStore;
    private readonly ISkillRequirementsValidator? _requirementsValidator;
    private readonly ICurrentTenant? _currentTenant;

    public SkillService(
        IServiceProvider serviceProvider,
        IRepository<SkillEntity, Guid> repository,
        ISkillRegistry registry,
        ISkillTemplateEngine templateEngine,
        FileSystemSkillStore fileStore,
        ISkillRequirementsValidator? requirementsValidator = null,
        ICurrentTenant? currentTenant = null)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _registry = Check.NotNull(registry);
        _templateEngine = Check.NotNull(templateEngine);
        _fileStore = Check.NotNull(fileStore);
        _requirementsValidator = requirementsValidator;
        _currentTenant = currentTenant;
    }

    /// <summary>
    /// Applies tenant visibility filtering to a SkillEntity query.
    /// <para>
    /// SkillEntity is an <see cref="IScopedResource"/> with no global multi-tenant
    /// query filter (so System rows stay visible to every tenant), so service-layer
    /// admin paths MUST enforce visibility explicitly. Mirrors
    /// <c>AgentPersonaService.ApplyVisibility</c>:
    /// </para>
    /// <list type="bullet">
    ///   <item>Single-tenant mode (<c>_currentTenant?.Id == null</c>): no filter - all rows visible.</item>
    ///   <item>Multi-tenant mode: keep System rows + Tenant/User rows owned by the current tenant.</item>
    /// </list>
    /// </summary>
    private IQueryable<SkillEntity> ApplyTenantVisibility(IQueryable<SkillEntity> query)
    {
        var tenantId = _currentTenant?.Id;
        if (tenantId == null)
            return query; // single-tenant: no additional filter

        return query.Where(e =>
            e.Scope == SkillScope.System ||
            (e.Scope == SkillScope.Tenant && e.TenantId == tenantId) ||
            (e.Scope == SkillScope.User && e.TenantId == tenantId));
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

        // Success path: renderResult.Errors is always empty here (the !Success branch
        // returned above), so start warnings empty rather than copying an empty list.
        var warnings = new List<string>();
        if (renderResult.UnusedParameters.Count > 0)
            warnings.Add($"Unused parameters: {string.Join(", ", renderResult.UnusedParameters)}");

        // Publish usage tracking event with full row identity so the handler can narrow the update
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new SkillActivatedEvent
            {
                Slug = skill.Slug,
                Scope = skill.Scope,
                Source = skill.Source,
                ActivatedAt = DateTime.UtcNow,
                UserId = CurrentUser?.Id,
                SkillTenantId = skill.TenantId,
                OwnerUserId = skill.OwnerUserId
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
        if (string.IsNullOrWhiteSpace(input.Slug) || input.Slug.Length > 64 || !SlugPattern().IsMatch(input.Slug))
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

        // Resolve TenantId: Tenant- AND User-scoped skills both record the current
        // tenant so admin visibility filtering can isolate User rows per tenant.
        // System skills stay tenant-agnostic (null). User-scope ownership is still
        // pinned by OwnerUserId; TenantId adds the tenant dimension for isolation.
        Guid? tenantId = input.Scope == SkillScope.System ? null : _currentTenant?.Id;

        // Check for duplicate slug in same scope/tenant/user
        var duplicate = await _repository.AnyAsync(e =>
            e.Slug == input.Slug &&
            e.Scope == input.Scope &&
            e.TenantId == tenantId &&
            e.OwnerUserId == ownerUserId);

        if (duplicate)
            return Fail<SkillDetailDto>($"A skill with slug '{input.Slug}' already exists in this scope.", 409, ErrorCodes.SkillSlugConflict);

        var entity = new SkillEntity
        {
            Slug = input.Slug,
            Scope = input.Scope,
            TenantId = tenantId,
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
            CategoryId = input.CategoryId,
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

        var tenantId = _currentTenant?.Id;

        // Tenant isolation: a tenant user must never see/modify another tenant's rows.
        // System rows are administrator-managed and off-limits to tenant users.
        if (tenantId != null)
        {
            if (entity.Scope == SkillScope.System)
                return Fail<SkillDetailDto>("Access denied: system skills are managed by administrators.", 403, ErrorCodes.SkillUnauthorized);
            // Tenant/User rows owned by a different tenant are invisible to this tenant → 404
            if (entity.TenantId != tenantId)
                return Fail<SkillDetailDto>("Skill not found.", 404, ErrorCodes.SkillNotFound);
        }

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
        if (input.CategoryId.HasValue) entity.CategoryId = input.CategoryId.Value;
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

        var tenantId = _currentTenant?.Id;

        // Tenant isolation: same guards as UpdateAsync.
        if (tenantId != null)
        {
            if (entity.Scope == SkillScope.System)
                return Fail("Access denied: system skills are managed by administrators.", 403, ErrorCodes.SkillUnauthorized);
            if (entity.TenantId != tenantId)
                return Fail("Skill not found.", 404, ErrorCodes.SkillNotFound);
        }

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

    public async Task<Result<int>> BatchDeleteAsync(List<Guid> ids)
    {
        Check.NotNullOrEmpty(ids);

        // Tenant isolation: ApplyTenantVisibility ensures a tenant user can only
        // delete rows it is allowed to see (System rows are excluded by the filter
        // in multi-tenant mode → never deletable here; admin-only via single-tenant host).
        var deleted = await ApplyTenantVisibility(_repository.AsQueryable())
            .Where(e => ids.Contains(e.Id) && e.Scope != SkillScope.System)
            .ExecuteDeleteAsync();

        if (deleted > 0)
            _registry.InvalidateCache();

        return Ok(deleted);
    }

    public async Task<Result<int>> BatchSetEnabledAsync(List<Guid> ids, bool enabled)
    {
        Check.NotNullOrEmpty(ids);

        // Tenant isolation: only the current tenant's visible rows may be toggled
        // (System rows excluded so tenant users cannot enable/disable shared skills).
        var updated = await ApplyTenantVisibility(_repository.AsQueryable())
            .Where(e => ids.Contains(e.Id) && e.Scope != SkillScope.System)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Enabled, enabled));

        if (updated > 0)
            _registry.InvalidateCache();

        return Ok(updated);
    }

    public async Task<Result<IPagedList<SkillSummaryDto>>> GetPagedAsync(SkillQueryDto query)
    {
        Check.NotNull(query);

        // ── DB branch ─────────────────────────────────────────────────────
        // Tenant isolation: enforce visibility before any caller-supplied filter
        // so a tenant admin can never page over another tenant's rows.
        var queryable = ApplyTenantVisibility(_repository.AsQueryable())
            .WhereIf(e => e.Name.ToLower().Contains(query.Keyword!.ToLower())
                || (e.Description != null && e.Description.ToLower().Contains(query.Keyword!.ToLower()))
                || e.Slug.ToLower().Contains(query.Keyword!.ToLower()),
                !string.IsNullOrWhiteSpace(query.Keyword))
            .WhereIf(e => e.Scope == query.Scope!.Value, query.Scope.HasValue)
            .WhereIf(e => e.Enabled == query.Enabled!.Value, query.Enabled.HasValue)
            .WhereIf(e => e.CategoryId == query.CategoryId!.Value, query.CategoryId.HasValue);

        // Tag filter (JSON contains)
        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            var tagLower = query.Tag.ToLower();
            queryable = queryable.Where(e => e.TagsJson != null && e.TagsJson.ToLower().Contains(tagLower));
        }

        // Sorting
        queryable = query.SortBy?.ToLowerInvariant() switch
        {
            "name" => query.SortDesc ? queryable.OrderByDescending(e => e.Name) : queryable.OrderBy(e => e.Name),
            "priority" => query.SortDesc ? queryable.OrderByDescending(e => e.Priority) : queryable.OrderBy(e => e.Priority),
            "activationcount" => query.SortDesc ? queryable.OrderByDescending(e => e.ActivationCount) : queryable.OrderBy(e => e.ActivationCount),
            _ => query.SortDesc ? queryable.OrderByDescending(e => e.CreationTime) : queryable.OrderBy(e => e.CreationTime)
        };

        // ── Fast path: DB-only (default) ──────────────────────────────────
        // When IncludeFileSource=false the admin caller sees only the
        // editable rows in AI_Skill, which preserves the historical paging
        // semantics. The file-source rows (BuiltIn embedded resources +
        // workspace SKILL.md + plugin/managed paths) are surfaced when the
        // caller explicitly opts in, since pulling them through ISkillRegistry
        // forces an in-memory page slice.
        if (!query.IncludeFileSource)
        {
            var pagedList = await queryable
                .Select(e => new SkillSummaryDto
                {
                    Id = e.Id,
                    Slug = e.Slug,
                    Scope = e.Scope,
                    Name = e.Name,
                    Description = e.Description,
                    WhenToUse = e.WhenToUse,
                    Priority = e.Priority,
                    Version = e.Version,
                    Author = e.Author,
                    Enabled = e.Enabled,
                    Source = SkillSource.Database,
                    IsReadOnly = false,
                    CategoryId = e.CategoryId,
                    CreationTime = e.CreationTime,
                    LastModificationTime = e.LastModificationTime
                })
                .CreateAsync(query);
            return Ok(pagedList);
        }

        // ── Merged branch ────────────────────────────────────────────────
        // Pull ALL skills through ISkillRegistry (handles dual-source merge
        // by slug+scope priority) then materialise to DTOs and filter/sort/
        // page in memory. The total cost is bounded by the file-source size
        // (typically tens of skills); we still re-resolve the DB filters
        // post-merge so paging stays consistent with the DB-only fast path.
        var allSkills = await _registry.GetAvailableSkillsAsync();
        // SkillDefinition is identified by Slug + Scope (file-source rows have
        // no Guid id). To keep the DTO contract `Id: Guid`, we look up the
        // DB row by slug when available and use its Id; otherwise we deterministically
        // derive an empty Guid so the front-end can fall back to Slug-keyed rendering.
        var dbIdBySlug = await ApplyTenantVisibility(_repository.AsQueryable())
            .Select(e => new { e.Id, e.Slug })
            .ToListAsync();
        var dbIdMap = dbIdBySlug
            .GroupBy(x => x.Slug, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        var merged = allSkills.Select(s => new SkillSummaryDto
        {
            Id = dbIdMap.TryGetValue(s.Slug, out var dbId) ? dbId : Guid.Empty,
            Slug = s.Slug,
            Scope = s.Scope,
            Name = s.Name,
            Description = s.Description,
            WhenToUse = s.WhenToUse,
            Priority = s.Priority,
            Version = s.Version,
            Author = s.Author,
            Enabled = s.Enabled,
            Source = s.Source,
            IsReadOnly = s.Source != SkillSource.Database,
            FilePath = s.FilePath,
            Tags = s.Tags?.ToList() ?? [],
            CreationTime = default,
            LastModificationTime = null,
        }).AsQueryable();

        // Apply DB-equivalent filters in memory (sortable by IQueryable since
        // it's a Linq-to-Objects materialised list).
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.ToLowerInvariant();
            merged = merged.Where(d =>
                d.Name.ToLower().Contains(kw)
                || (d.Description ?? string.Empty).ToLower().Contains(kw)
                || d.Slug.ToLower().Contains(kw));
        }
        if (query.Scope.HasValue)
            merged = merged.Where(d => d.Scope == query.Scope.Value);
        if (query.Enabled.HasValue)
            merged = merged.Where(d => d.Enabled == query.Enabled.Value);
        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            var tagLower = query.Tag.ToLowerInvariant();
            merged = merged.Where(d => d.Tags != null && d.Tags.Any(t => t.ToLowerInvariant().Contains(tagLower)));
        }

        merged = query.SortBy?.ToLowerInvariant() switch
        {
            "name" => query.SortDesc ? merged.OrderByDescending(d => d.Name) : merged.OrderBy(d => d.Name),
            "priority" => query.SortDesc ? merged.OrderByDescending(d => d.Priority) : merged.OrderBy(d => d.Priority),
            _ => query.SortDesc ? merged.OrderByDescending(d => d.Name) : merged.OrderBy(d => d.Name),
        };

        var totalCount = merged.Count();
        var skip = (query.PageIndex - 1) * query.PageSize;
        var items = merged.Skip(skip).Take(query.PageSize).ToList();
        var mergedPaged = new PagedList<SkillSummaryDto>(items, query.PageIndex, query.PageSize, totalCount);
        return Ok((IPagedList<SkillSummaryDto>)mergedPaged);
    }

    public async Task<Result<SkillUsageStatsDto>> GetUsageStatsAsync()
    {
        // Count over the same dual-source merged view the list endpoint exposes
        // (DB rows + file-source skills via ISkillRegistry), so file-based skills
        // (BuiltIn embedded resources + workspace SKILL.md + plugin/managed paths)
        // are included. A DB-only count would report 0 on a fresh install even
        // though the skill list shows the file-source skills.
        var allSkills = await _registry.GetAvailableSkillsAsync();

        // TotalActivations keeps its DB semantics: activation tracking
        // (ActivationCount) only exists on AI_Skill rows. Tenant-scoped so the
        // sum never leaks another tenant's usage.
        var totalActivations = await ApplyTenantVisibility(_repository.AsQueryable())
            .SumAsync(e => (long?)e.ActivationCount) ?? 0L;

        var stats = new SkillUsageStatsDto
        {
            TotalSkills = allSkills.Count,
            EnabledSkills = allSkills.Count(s => s.Enabled),
            DisabledSkills = allSkills.Count(s => !s.Enabled),
            TenantScopeSkills = allSkills.Count(s => s.Scope == SkillScope.Tenant),
            UserScopeSkills = allSkills.Count(s => s.Scope == SkillScope.User),
            TotalActivations = totalActivations
        };

        return Ok(stats);
    }

    public async Task<Result<List<PopularSkillDto>>> GetPopularSkillsAsync(int topN = 10)
    {
        var popular = await ApplyTenantVisibility(_repository.AsQueryable())
            .Where(e => e.ActivationCount > 0)
            .OrderByDescending(e => e.ActivationCount)
            .Take(topN)
            .Select(e => new PopularSkillDto
            {
                Slug = e.Slug,
                Name = e.Name,
                Scope = e.Scope,
                Source = SkillSource.Database,
                ActivationCount = e.ActivationCount,
                LastActivatedAt = e.LastActivatedAt
            })
            .ToListAsync();

        return Ok(popular);
    }

    public async Task<Result<List<SkillExportDto>>> ExportSkillsAsync(SkillScope? scope = null)
    {
        var entities = await ApplyTenantVisibility(_repository.AsQueryable())
            .WhereIf(e => e.Scope == scope!.Value, scope.HasValue)
            .OrderBy(e => e.Slug)
            .ToListAsync();

        var exported = entities.Select(e => new SkillExportDto
        {
            Slug = e.Slug,
            Name = e.Name,
            Description = e.Description,
            Content = e.Content,
            WhenToUse = e.WhenToUse,
            Parameters = SkillJsonHelper.DeserializeOrDefault<List<SkillParameter>>(e.ParametersJson),
            AllowedToolGroups = SkillJsonHelper.ParseConstraintField<List<string>>(e.ConstraintsJson, "allowedToolGroups"),
            AllowedTools = SkillJsonHelper.ParseConstraintField<List<string>>(e.ConstraintsJson, "allowedTools"),
            DeniedTools = SkillJsonHelper.ParseConstraintField<List<string>>(e.ConstraintsJson, "deniedTools"),
            RequiredModel = SkillJsonHelper.ParseConstraintField<string>(e.ConstraintsJson, "requiredModel"),
            RequiredProvider = SkillJsonHelper.ParseConstraintField<string>(e.ConstraintsJson, "requiredProvider"),
            Requirements = SkillJsonHelper.DeserializeOrDefault<SkillRequirements>(e.RequirementsJson),
            Tags = SkillJsonHelper.DeserializeOrDefault<List<string>>(e.TagsJson),
            Priority = e.Priority,
            Version = e.Version,
            Author = e.Author,
            Enabled = e.Enabled
        }).ToList();

        return Ok(exported);
    }

    public async Task<Result<SkillImportResultDto>> ImportSkillsAsync(List<SkillExportDto> skills, SkillScope targetScope)
    {
        Check.NotNullOrEmpty(skills);

        var result = new SkillImportResultDto();
        var toInsert = new List<SkillEntity>();
        var toUpdate = new List<SkillEntity>();

        // Tenant isolation: System-scope rows are tenant-agnostic and administrator-managed
        // (UpdateAsync/DeleteAsync return 403 for them, and the admin create endpoint pins
        // Scope=Tenant). Import must not be a side door that lets a tenant user create or
        // overwrite globally visible System skills, so reject it in multi-tenant mode.
        // Single-tenant hosts (_currentTenant?.Id == null) keep the existing behaviour.
        if (targetScope == SkillScope.System && _currentTenant?.Id != null)
            return Fail<SkillImportResultDto>("Access denied: system skills are managed by administrators.", 403, ErrorCodes.SkillUnauthorized);

        // Resolve TenantId for the import batch (same rule as CreateAsync):
        // Tenant- and User-scoped rows both record the current tenant; System stays null.
        Guid? importTenantId = targetScope == SkillScope.System ? null : _currentTenant?.Id;

        // Pre-load existing slugs in target scope+tenant for conflict detection.
        // ApplyTenantVisibility is layered on as defence in depth so the update branch
        // below can never resolve a row outside the caller's tenant.
        var existingSlugs = await ApplyTenantVisibility(_repository.AsQueryable())
            .Where(e => e.Scope == targetScope && e.TenantId == importTenantId)
            .Select(e => new { e.Id, e.Slug })
            .ToDictionaryAsync(e => e.Slug, e => e.Id, StringComparer.OrdinalIgnoreCase);

        // Pre-load all system skill slugs to avoid N+1 queries in the loop
        var systemSlugs = (await _fileStore.GetAllAsync()).Select(s => s.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Guid? ownerUserId = null;
        if (targetScope == SkillScope.User)
        {
            var userId = CurrentUser?.Id;
            if (!userId.HasValue)
                return Fail<SkillImportResultDto>("Authentication required to import user-scoped skills.", 401, ErrorCodes.SkillUnauthorized);
            ownerUserId = userId.Value;
        }

        foreach (var skill in skills)
        {
            if (string.IsNullOrWhiteSpace(skill.Slug))
            {
                result.Errors.Add($"Skipped skill with empty slug.");
                result.Skipped++;
                continue;
            }

            // Check system skill conflict (using pre-loaded set)
            if (systemSlugs.Contains(skill.Slug))
            {
                result.Errors.Add($"Slug '{skill.Slug}' is reserved by a system skill.");
                result.Skipped++;
                continue;
            }

            var constraintsJson = SkillJsonHelper.BuildConstraintsJson(
                skill.AllowedToolGroups, skill.AllowedTools, skill.DeniedTools,
                skill.RequiredModel, skill.RequiredProvider);

            if (existingSlugs.TryGetValue(skill.Slug, out var existingId))
            {
                // Update existing
                var entity = await _repository.GetAsync(existingId);
                if (entity != null)
                {
                    entity.Name = skill.Name;
                    entity.Description = skill.Description;
                    entity.Content = skill.Content;
                    entity.WhenToUse = skill.WhenToUse;
                    entity.ParametersJson = SkillJsonHelper.SerializeOrDefault(skill.Parameters, "[]");
                    entity.ConstraintsJson = constraintsJson;
                    entity.RequirementsJson = skill.Requirements != null ? JsonSerializer.Serialize(skill.Requirements, TnziJsonDefaults.Options) : null;
                    entity.TagsJson = skill.Tags != null ? JsonSerializer.Serialize(skill.Tags, TnziJsonDefaults.Options) : null;
                    entity.Priority = skill.Priority;
                    entity.Version = skill.Version;
                    entity.Author = skill.Author;
                    entity.Enabled = skill.Enabled;
                    toUpdate.Add(entity);
                    result.Updated++;
                }
            }
            else
            {
                // Create new
                toInsert.Add(new SkillEntity
                {
                    Slug = skill.Slug,
                    Scope = targetScope,
                    TenantId = importTenantId,
                    OwnerUserId = ownerUserId,
                    Name = skill.Name,
                    Description = skill.Description,
                    Content = skill.Content,
                    WhenToUse = skill.WhenToUse,
                    ParametersJson = SkillJsonHelper.SerializeOrDefault(skill.Parameters, "[]"),
                    ConstraintsJson = constraintsJson,
                    RequirementsJson = skill.Requirements != null ? JsonSerializer.Serialize(skill.Requirements, TnziJsonDefaults.Options) : null,
                    TagsJson = skill.Tags != null ? JsonSerializer.Serialize(skill.Tags, TnziJsonDefaults.Options) : null,
                    Priority = skill.Priority,
                    Version = skill.Version,
                    Author = skill.Author,
                    Enabled = skill.Enabled
                });
                result.Created++;
            }
        }

        if (toInsert.Count > 0)
            await _repository.InsertManyAsync(toInsert);

        if (toUpdate.Count > 0)
            await _repository.UpdateManyAsync(toUpdate);

        if (toInsert.Count > 0 || toUpdate.Count > 0)
            _registry.InvalidateCache();

        return Ok(result);
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
            AllowedTools = SkillJsonHelper.ParseConstraintField<List<string>>(entity.ConstraintsJson, "allowedTools"),
            DeniedTools = SkillJsonHelper.ParseConstraintField<List<string>>(entity.ConstraintsJson, "deniedTools"),
            RequiredModel = SkillJsonHelper.ParseConstraintField<string>(entity.ConstraintsJson, "requiredModel"),
            RequiredProvider = SkillJsonHelper.ParseConstraintField<string>(entity.ConstraintsJson, "requiredProvider")
        };
    }
}
