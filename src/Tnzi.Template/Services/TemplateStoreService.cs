using TemplateEntity = Tnzi.Template.Entities.Template;

namespace Tnzi.Template.Services;

/// <summary>
/// 模板存储服务实现
/// 支持从数据库和文件系统加载模板（优先数据库，文件系统作为后备）
/// </summary>
public class TemplateStoreService : ApplicationService, ITemplateStoreService
{
    private readonly IRepository<TemplateEntity, Guid> _repository;
    private readonly TemplateFileParser? _templateFileParser;
    private readonly TemplateOptions? _templateOptions;

    public TemplateStoreService(
        IRepository<TemplateEntity, Guid> repository,
        IServiceProvider serviceProvider,
        TemplateFileParser? templateFileParser = null,
        IOptions<TemplateOptions>? templateOptions = null)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _templateFileParser = templateFileParser;
        _templateOptions = templateOptions?.Value;
    }

    public async Task<Result<TemplateEntity>> GetTemplateAsync(string templateName, string module, string? category = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(templateName))
            return Fail<TemplateEntity>("Template name cannot be null or empty", 400, ErrorCodes.VALIDATION_ERROR);

        if (string.IsNullOrWhiteSpace(module))
            return Fail<TemplateEntity>("Module cannot be null or empty", 400, ErrorCodes.VALIDATION_ERROR);

        // 首先尝试从数据库加载
        var query = _repository
            .Where(t => t.TemplateName == templateName && t.Module == module && !t.IsDeleted && t.IsActive);

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(t => t.Category == category);
        }

        var template = await query.FirstOrDefaultAsync(cancellationToken);

        // 如果数据库中没有找到，且启用了文件系统模板，尝试从文件系统加载
        if (template == null && _templateFileParser != null && _templateOptions?.EnableFileSystemTemplates == true)
        {
            template = await LoadTemplateFromFileSystemAsync(templateName, module, category, cancellationToken);
        }

        if (template == null)
        {
            return Fail<TemplateEntity>("Template not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        return Ok(template);
    }

    /// <summary>
    /// 从文件系统加载模板
    /// </summary>
    private async Task<TemplateEntity?> LoadTemplateFromFileSystemAsync(string templateName, string module, string? category, CancellationToken cancellationToken)
    {
        if (_templateFileParser == null || _templateOptions == null)
            return null;

        try
        {
            var templateRoot = _templateOptions.TemplateRootPath ?? "Templates";
            var extension = _templateOptions.TemplateExtension ?? ".cshtml";

            var relativePath = string.IsNullOrWhiteSpace(category)
                ? Path.Combine(templateRoot, module, $"{templateName}{extension}")
                : Path.Combine(templateRoot, module, category, $"{templateName}{extension}");

            var searchRoots = BuildSearchRoots(_templateOptions, ServiceProvider);
            var foundPath = FindFileInSearchRoots(relativePath, searchRoots);

            if (foundPath == null)
                return null;

            var templateInfo = await _templateFileParser.ParseTemplateFileAsync(foundPath, cancellationToken);
            if (templateInfo == null)
            {
                Logger.LogWarning("Failed to parse template file: {Path}", foundPath);
                return null;
            }

            return new TemplateEntity
            {
                TemplateName = templateInfo.Name,
                Module = module,
                Category = category ?? templateInfo.Category ?? string.Empty,
                SubjectTemplate = templateInfo.SubjectTemplate ?? string.Empty,
                ContentTemplate = templateInfo.ContentTemplate ?? string.Empty,
                DefaultLayoutName = templateInfo.DefaultLayoutName,
                IsActive = true,
                Description = templateInfo.Metadata?.TryGetValue("description", out var desc) == true
                    ? desc?.ToString()
                    : null
            };
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            Logger.LogDebug(ex, "Template file not found: {TemplateName} (module: {Module}, category: {Category})", templateName, module, category);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.LogWarning(ex, "Unauthorized access when loading template: {TemplateName} (module: {Module}, category: {Category})", templateName, module, category);
            return null;
        }
        catch (IOException ex)
        {
            Logger.LogWarning(ex, "IO error when loading template: {TemplateName} (module: {Module}, category: {Category})", templateName, module, category);
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unexpected error loading template '{TemplateName}' from file system (module: {Module}, category: {Category})", templateName, module, category);
            return null;
        }
    }

    public async Task<Result<TemplateDto>> GetTemplateByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await _repository.GetAsync(id, cancellationToken);
        if (template == null)
        {
            return Fail<TemplateDto>("Template not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }
        return Ok(template.MapTo<TemplateDto>());
    }

    public async Task<Result<TemplateDto>> CreateTemplateAsync(CreateTemplateRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        // 检查模板名称是否已存在（在同一个模块和分类下）
        var exists = await _repository
            .AnyAsync(t => t.TemplateName == request.TemplateName
                && t.Module == request.Module
                && t.Category == request.Category
                && !t.IsDeleted, cancellationToken);

        if (exists)
        {
            return Fail<TemplateDto>(
                $"Template with name '{request.TemplateName}' already exists in module '{request.Module}' and category '{request.Category}'",
                409,
                ErrorCodes.DATA_CONFLICT);
        }

        var template = request.MapTo<TemplateEntity>();
        await _repository.InsertAsync(template, cancellationToken);
        LogInformation("Template created: {TemplateName}, Module: {Module}, Category: {Category}", template.TemplateName, template.Module, template.Category);
        return Ok(template.MapTo<TemplateDto>(), "Template created successfully");
    }

    public async Task<Result<TemplateDto>> UpdateTemplateAsync(Guid id, UpdateTemplateRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var existing = await _repository.GetAsync(id, cancellationToken);
        if (existing == null)
        {
            return Fail<TemplateDto>("Template not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // 如果模板名称、模块或分类改变，检查新组合是否已存在
        if (existing.TemplateName != request.TemplateName
            || existing.Module != request.Module
            || existing.Category != request.Category)
        {
            var nameExists = await _repository
                .AnyAsync(t => t.TemplateName == request.TemplateName
                    && t.Module == request.Module
                    && t.Category == request.Category
                    && t.Id != id
                    && !t.IsDeleted, cancellationToken);

            if (nameExists)
            {
                return Fail<TemplateDto>(
                    $"Template with name '{request.TemplateName}' already exists in module '{request.Module}' and category '{request.Category}'",
                    409,
                    ErrorCodes.DATA_CONFLICT);
            }
        }

        request.MapTo(existing);
        await _repository.UpdateAsync(existing, cancellationToken);
        LogInformation("Template updated: {TemplateName}, Module: {Module}, Category: {Category}", existing.TemplateName, existing.Module, existing.Category);
        return Ok(existing.MapTo<TemplateDto>(), "Template updated successfully");
    }

    public async Task<Result> DeleteTemplateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await _repository.GetAsync(id, cancellationToken);
        if (template == null)
        {
            return Fail("Template not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        await _repository.DeleteAsync(id, cancellationToken);
        LogInformation("Template deleted: {TemplateName}, Module: {Module}, Category: {Category}", template.TemplateName, template.Module, template.Category);
        return Ok("Template deleted successfully");
    }

    public async Task<Result> DeleteTemplatesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(ids);

        var idList = ids.ToList();
        var templates = await _repository.Where(t => idList.Contains(t.Id)).ToListAsync(cancellationToken);

        if (templates.Count == 0)
        {
            return Fail("No templates found for the specified IDs", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        await _repository.DeleteManyAsync(templates, cancellationToken);
        LogInformation("Batch deleted {Count} templates", templates.Count);
        return Ok($"Successfully deleted {templates.Count} templates");
    }

    public async Task<Result<IEnumerable<TemplateDto>>> GetAllTemplatesAsync(string? module = null, string? category = null, CancellationToken cancellationToken = default)
    {
        var query = _repository.Where(t => !t.IsDeleted);

        if (!string.IsNullOrWhiteSpace(module))
        {
            query = query.Where(t => t.Module == module);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(t => t.Category == category);
        }

        var templates = await query.OrderBy(t => t.Module).ThenBy(t => t.Category).ThenBy(t => t.TemplateName).ToListAsync(cancellationToken);
        return Ok(templates.MapToList<TemplateDto>().AsEnumerable());
    }

    public async Task<Result<IPagedList<TemplateInfoDto>>> QueryTemplatesAsync(QueryTemplateRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var query = _repository.Where(t => !t.IsDeleted);

        if (!string.IsNullOrWhiteSpace(request.Module))
        {
            query = query.Where(t => t.Module == request.Module);
        }

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            query = query.Where(t => t.Category == request.Category);
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(t => t.IsActive == request.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.ToLower();
            query = query.Where(t =>
                t.TemplateName.ToLower().Contains(keyword) ||
                (t.Description != null && t.Description.ToLower().Contains(keyword)));
        }

        var pagedList = await query
            .OrderBy(t => t.Module)
            .ThenBy(t => t.Category)
            .ThenBy(t => t.TemplateName)
            .ProjectTo<TemplateEntity, TemplateInfoDto>()
            .CreateAsync(request, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<string>> ExportTemplatesAsync(string? module = null, string? category = null, CancellationToken cancellationToken = default)
    {
        var query = _repository.AsQueryable().AsNoTracking().Where(t => !t.IsDeleted);

        if (!string.IsNullOrWhiteSpace(module))
            query = query.Where(t => t.Module == module);

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(t => t.Category == category);

        var templates = await query
            .OrderBy(t => t.Module)
            .ThenBy(t => t.Category)
            .ThenBy(t => t.TemplateName)
            .ToListAsync(cancellationToken);

        var entries = templates.Select(t => new TemplateExportEntry
        {
            TemplateName = t.TemplateName,
            Module = t.Module,
            Category = t.Category,
            SubjectTemplate = t.SubjectTemplate,
            ContentTemplate = t.ContentTemplate,
            DefaultLayoutName = t.DefaultLayoutName,
            IsActive = t.IsActive,
            Description = t.Description,
            Metadata = t.Metadata
        }).ToList();

        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        LogInformation("Exported {Count} templates (module: {Module}, category: {Category})", entries.Count, module ?? "all", category ?? "all");
        return Ok(json, $"Exported {entries.Count} templates");
    }

    public async Task<Result<TemplateImportResultDto>> ImportTemplatesAsync(string json, bool overwriteExisting = false, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(json);

        List<TemplateExportEntry>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<TemplateExportEntry>>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            return Fail<TemplateImportResultDto>($"Invalid JSON format: {ex.Message}", 400, ErrorCodes.VALIDATION_ERROR);
        }

        if (entries == null || entries.Count == 0)
            return Fail<TemplateImportResultDto>("No templates found in import data", 400, ErrorCodes.VALIDATION_ERROR);

        var result = new TemplateImportResultDto();
        var toInsert = new List<TemplateEntity>();

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.TemplateName) || string.IsNullOrWhiteSpace(entry.Module))
            {
                result.Errors.Add($"Skipped entry with empty TemplateName or Module");
                result.SkippedCount++;
                continue;
            }

            var existing = await _repository.FirstOrDefaultAsync(
                t => t.TemplateName == entry.TemplateName
                    && t.Module == entry.Module
                    && t.Category == entry.Category
                    && !t.IsDeleted,
                cancellationToken);

            if (existing != null)
            {
                if (overwriteExisting)
                {
                    existing.SubjectTemplate = entry.SubjectTemplate;
                    existing.ContentTemplate = entry.ContentTemplate;
                    existing.DefaultLayoutName = entry.DefaultLayoutName;
                    existing.IsActive = entry.IsActive;
                    existing.Description = entry.Description;
                    existing.Metadata = entry.Metadata;
                    await _repository.UpdateAsync(existing, cancellationToken);
                    result.UpdatedCount++;
                }
                else
                {
                    result.SkippedCount++;
                }
                continue;
            }

            toInsert.Add(new TemplateEntity
            {
                TemplateName = entry.TemplateName,
                Module = entry.Module,
                Category = entry.Category,
                SubjectTemplate = entry.SubjectTemplate,
                ContentTemplate = entry.ContentTemplate,
                DefaultLayoutName = entry.DefaultLayoutName,
                IsActive = entry.IsActive,
                Description = entry.Description,
                Metadata = entry.Metadata
            });
        }

        if (toInsert.Count > 0)
        {
            await _repository.InsertManyAsync(toInsert, cancellationToken);
            result.CreatedCount = toInsert.Count;
        }

        LogInformation("Imported templates: {Created} created, {Updated} updated, {Skipped} skipped, {Errors} errors",
            result.CreatedCount, result.UpdatedCount, result.SkippedCount, result.Errors.Count);

        return Ok(result, $"Imported {result.CreatedCount} templates, updated {result.UpdatedCount}, skipped {result.SkippedCount}");
    }

    public async Task<Result<TemplateDto>> CloneTemplateAsync(Guid sourceId, string newTemplateName, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(newTemplateName);

        var source = await _repository.GetAsync(sourceId, cancellationToken);
        if (source == null)
        {
            return Fail<TemplateDto>("Source template not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // 检查新名称是否已存在
        var exists = await _repository
            .AnyAsync(t => t.TemplateName == newTemplateName
                && t.Module == source.Module
                && t.Category == source.Category
                && !t.IsDeleted, cancellationToken);

        if (exists)
        {
            return Fail<TemplateDto>(
                $"Template with name '{newTemplateName}' already exists in module '{source.Module}' and category '{source.Category}'",
                409,
                ErrorCodes.DATA_CONFLICT);
        }

        var clone = new TemplateEntity
        {
            TemplateName = newTemplateName,
            Module = source.Module,
            Category = source.Category,
            SubjectTemplate = source.SubjectTemplate,
            ContentTemplate = source.ContentTemplate,
            DefaultLayoutName = source.DefaultLayoutName,
            IsActive = source.IsActive,
            Description = $"Cloned from '{source.TemplateName}'",
            Metadata = source.Metadata
        };

        await _repository.InsertAsync(clone, cancellationToken);
        LogInformation("Template cloned: {SourceName} -> {CloneName}, Module: {Module}", source.TemplateName, newTemplateName, source.Module);
        return Ok(clone.MapTo<TemplateDto>(), "Template cloned successfully");
    }

    public async Task<Result<TemplateVariablesDto>> GetTemplateVariablesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await _repository.GetAsync(id, cancellationToken);
        if (template == null)
            return Fail<TemplateVariablesDto>("Template not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        var subjectVars = ExtractTemplateVariables(template.SubjectTemplate);
        var contentVars = ExtractTemplateVariables(template.ContentTemplate);
        var allVars = subjectVars.Union(contentVars).OrderBy(v => v).ToList();

        return Ok(new TemplateVariablesDto
        {
            TemplateId = template.Id,
            TemplateName = template.TemplateName,
            SubjectVariables = subjectVars,
            ContentVariables = contentVars,
            AllVariables = allVars
        });
    }

    public async Task<Result<int>> BatchActivateAsync(List<Guid> ids, bool isActive, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(ids);

        var templates = await _repository.Where(t => ids.Contains(t.Id) && !t.IsDeleted).ToListAsync(cancellationToken);
        if (templates.Count == 0)
            return Fail<int>("No templates found for the specified IDs", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        var changed = 0;
        foreach (var template in templates)
        {
            if (template.IsActive != isActive)
            {
                template.IsActive = isActive;
                changed++;
            }
        }

        if (changed > 0)
            await _repository.UpdateManyAsync(templates, cancellationToken);

        LogInformation("Batch {Action} {Count} templates", isActive ? "activated" : "deactivated", changed);
        return Ok(changed, $"Successfully {(isActive ? "activated" : "deactivated")} {changed} templates");
    }

    /// <summary>
    /// Extract @Model.XXX variable references from a Razor template string
    /// </summary>
    private static List<string> ExtractTemplateVariables(string? templateContent)
    {
        if (string.IsNullOrWhiteSpace(templateContent))
            return new List<string>();

        var variables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Match @Model.PropertyName patterns (including nested like @Model.User.Name)
        var regex = new Regex(
            @"@Model\.([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        foreach (Match match in regex.Matches(templateContent))
        {
            variables.Add(match.Groups[1].Value);
        }

        return variables.OrderBy(v => v).ToList();
    }
}
