
namespace Tnzi.Template.Services;

/// <summary>
/// 布局存储服务实现
/// 支持从数据库和文件系统加载布局（优先数据库，文件系统作为后备）
/// </summary>
public class LayoutStoreService : ApplicationService, ILayoutStoreService
{
    private readonly IRepository<Layout, Guid> _repository;
    private readonly TemplateFileParser? _templateFileParser;
    private readonly TemplateOptions? _templateOptions;

    public LayoutStoreService(
        IRepository<Layout, Guid> repository,
        IServiceProvider serviceProvider,
        TemplateFileParser? templateFileParser = null,
        IOptions<TemplateOptions>? templateOptions = null)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _templateFileParser = templateFileParser;
        _templateOptions = templateOptions?.Value;
    }

    public async Task<Result<Layout>> GetLayoutAsync(string layoutName, string module, string? category = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(layoutName))
            return Fail<Layout>("Layout name cannot be null or empty", 400, ErrorCodes.VALIDATION_ERROR);

        if (string.IsNullOrWhiteSpace(module))
            return Fail<Layout>("Module cannot be null or empty", 400, ErrorCodes.VALIDATION_ERROR);

        // 首先从数据库加载
        var query = _repository
            .Where(l => l.LayoutName == layoutName && l.Module == module && !l.IsDeleted && l.IsActive);

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(l => l.Category == category);
        }

        var layout = await query.FirstOrDefaultAsync(cancellationToken);

        // 如果数据库中没有找到，尝试从文件系统加载
        if (layout == null && _templateFileParser != null && _templateOptions?.EnableFileSystemTemplates == true)
        {
            layout = await LoadLayoutFromFileSystemAsync(layoutName, module, category, cancellationToken);
        }

        if (layout == null)
        {
            return Fail<Layout>("Layout not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        return Ok(layout);
    }

    public async Task<Result<LayoutDto>> GetLayoutByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var layout = await _repository.GetAsync(id, cancellationToken);
        if (layout == null)
        {
            return Fail<LayoutDto>("Layout not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }
        return Ok(layout.MapTo<LayoutDto>());
    }

    public async Task<Result<Layout>> GetDefaultLayoutAsync(string module, string category, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(module))
            return Fail<Layout>("Module cannot be null or empty", 400, ErrorCodes.VALIDATION_ERROR);

        if (string.IsNullOrWhiteSpace(category))
            return Fail<Layout>("Category cannot be null or empty", 400, ErrorCodes.VALIDATION_ERROR);

        // 首先从数据库查找默认布局
        var layout = await _repository
            .Where(l => l.Module == module
                && l.Category == category
                && l.IsDefault
                && l.IsActive
                && !l.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        // 如果数据库中没有找到，尝试从文件系统加载默认布局
        if (layout == null && _templateFileParser != null && _templateOptions?.EnableFileSystemTemplates == true)
        {
            layout = await LoadDefaultLayoutFromFileSystemAsync(module, category, cancellationToken);
        }

        if (layout == null)
        {
            return Fail<Layout>("Default layout not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        return Ok(layout);
    }

    public async Task<Result<LayoutDto>> CreateLayoutAsync(CreateLayoutRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        // 检查Layout名称是否已存在
        var exists = await _repository
            .ExistsAsync(l => l.LayoutName == request.LayoutName
                && l.Module == request.Module
                && l.Category == request.Category
                && !l.IsDeleted, cancellationToken);

        if (exists)
        {
            return Fail<LayoutDto>(
                $"Layout with name '{request.LayoutName}' already exists in module '{request.Module}' and category '{request.Category}'",
                409,
                ErrorCodes.DATA_CONFLICT);
        }

        // 如果设置为默认Layout，取消同模块同分类的其他默认Layout
        if (request.IsDefault)
        {
            await ClearDefaultLayoutsAsync(request.Module, request.Category, null, cancellationToken);
        }

        var layout = request.MapTo<Layout>();
        await _repository.InsertAsync(layout, cancellationToken);
        LogInformation("Layout created: {LayoutName}, Module: {Module}, Category: {Category}", layout.LayoutName, layout.Module, layout.Category);
        return Ok(layout.MapTo<LayoutDto>(), "Layout created successfully");
    }

    public async Task<Result<LayoutDto>> UpdateLayoutAsync(Guid id, UpdateLayoutRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var existing = await _repository.GetAsync(id, cancellationToken);
        if (existing == null)
        {
            return Fail<LayoutDto>("Layout not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // 如果名称/模块/分类改变，检查新组合是否已存在
        if (existing.LayoutName != request.LayoutName
            || existing.Module != request.Module
            || existing.Category != request.Category)
        {
            var nameExists = await _repository
                .ExistsAsync(l => l.LayoutName == request.LayoutName
                    && l.Module == request.Module
                    && l.Category == request.Category
                    && l.Id != id
                    && !l.IsDeleted, cancellationToken);

            if (nameExists)
            {
                return Fail<LayoutDto>(
                    $"Layout with name '{request.LayoutName}' already exists in module '{request.Module}' and category '{request.Category}'",
                    409,
                    ErrorCodes.DATA_CONFLICT);
            }
        }

        // 如果设置为默认Layout，取消同模块同分类的其他默认Layout
        if (request.IsDefault && !existing.IsDefault)
        {
            await ClearDefaultLayoutsAsync(request.Module, request.Category, id, cancellationToken);
        }

        request.MapTo(existing);
        await _repository.UpdateAsync(existing, cancellationToken);
        LogInformation("Layout updated: {LayoutName}, Module: {Module}, Category: {Category}", existing.LayoutName, existing.Module, existing.Category);
        return Ok(existing.MapTo<LayoutDto>(), "Layout updated successfully");
    }

    public async Task<Result> DeleteLayoutAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var layout = await _repository.GetAsync(id, cancellationToken);
        if (layout == null)
        {
            return Fail("Layout not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        await _repository.DeleteAsync(id, cancellationToken);
        LogInformation("Layout deleted: {LayoutName}, Module: {Module}, Category: {Category}", layout.LayoutName, layout.Module, layout.Category);
        return Ok("Layout deleted successfully");
    }

    public async Task<Result> DeleteLayoutsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(ids);

        var idList = ids.ToList();
        var layouts = await _repository.Where(l => idList.Contains(l.Id)).ToListAsync(cancellationToken);

        if (layouts.Count == 0)
        {
            return Fail("No layouts found for the specified IDs", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        await _repository.DeleteManyAsync(layouts, cancellationToken);
        LogInformation("Batch deleted {Count} layouts", layouts.Count);
        return Ok($"Successfully deleted {layouts.Count} layouts");
    }

    public async Task<Result<IEnumerable<LayoutDto>>> GetAllLayoutsAsync(string? module = null, string? category = null, CancellationToken cancellationToken = default)
    {
        var query = _repository.Where(l => !l.IsDeleted);

        if (!string.IsNullOrWhiteSpace(module))
        {
            query = query.Where(l => l.Module == module);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(l => l.Category == category);
        }

        var layouts = await query.OrderBy(l => l.Module).ThenBy(l => l.Category).ThenBy(l => l.LayoutName).ToListAsync(cancellationToken);
        return Ok(layouts.MapToList<LayoutDto>().AsEnumerable());
    }

    public async Task<Result<IPagedList<LayoutInfoDto>>> QueryLayoutsAsync(QueryLayoutRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var query = _repository.Where(l => !l.IsDeleted);

        if (!string.IsNullOrWhiteSpace(request.Module))
        {
            query = query.Where(l => l.Module == request.Module);
        }

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            query = query.Where(l => l.Category == request.Category);
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(l => l.IsActive == request.IsActive.Value);
        }

        if (request.IsDefault.HasValue)
        {
            query = query.Where(l => l.IsDefault == request.IsDefault.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.ToLower();
            query = query.Where(l =>
                l.LayoutName.ToLower().Contains(keyword) ||
                (l.Description != null && l.Description.ToLower().Contains(keyword)));
        }

        var pagedList = await query
            .OrderBy(l => l.Module)
            .ThenBy(l => l.Category)
            .ThenBy(l => l.LayoutName)
            .ProjectTo<Layout, LayoutInfoDto>()
            .CreateAsync(request, cancellationToken);

        return Ok(pagedList);
    }

    /// <summary>
    /// 清除同模块同分类的默认布局标记
    /// </summary>
    private async Task ClearDefaultLayoutsAsync(string module, string category, Guid? excludeId, CancellationToken cancellationToken)
    {
        var query = _repository
            .Where(l => l.Module == module
                && l.Category == category
                && l.IsDefault
                && !l.IsDeleted);

        if (excludeId.HasValue)
        {
            query = query.Where(l => l.Id != excludeId.Value);
        }

        var existingDefaults = await query.ToListAsync(cancellationToken);
        if (existingDefaults.Count > 0)
        {
            foreach (var layout in existingDefaults)
            {
                layout.IsDefault = false;
            }
            await _repository.UpdateManyAsync(existingDefaults, cancellationToken);
        }
    }

    /// <summary>
    /// 从文件系统加载布局
    /// </summary>
    private async Task<Layout?> LoadLayoutFromFileSystemAsync(string layoutName, string module, string? category, CancellationToken cancellationToken)
    {
        if (_templateFileParser == null || _templateOptions == null)
            return null;

        try
        {
            var templateRoot = _templateOptions.TemplateRootPath ?? "Templates";
            var extension = _templateOptions.TemplateExtension ?? ".cshtml";

            // 布局文件通常以下划线开头：_Default.cshtml, _EmailDefault.cshtml
            var layoutFileName = layoutName.StartsWith("_") ? layoutName : $"_{layoutName}";

            var relativePath = string.IsNullOrWhiteSpace(category)
                ? Path.Combine(templateRoot, "Layouts", $"{layoutFileName}{extension}")
                : Path.Combine(templateRoot, "Layouts", category, $"{layoutFileName}{extension}");

            var searchRoots = BuildSearchRoots(_templateOptions, ServiceProvider);
            var foundPath = TemplatePathHelper.FindFileInSearchRoots(relativePath, searchRoots);

            if (foundPath == null)
                return null;

            var layoutInfo = await _templateFileParser.ParseLayoutFileAsync(foundPath, cancellationToken);
            if (layoutInfo == null)
            {
                Logger.LogWarning("Failed to parse layout file: {Path}", foundPath);
                return null;
            }

            return new Layout
            {
                LayoutName = layoutInfo.Name ?? layoutName,
                Module = module,
                Category = category ?? layoutInfo.Category ?? string.Empty,
                LayoutContent = layoutInfo.LayoutContent ?? string.Empty,
                IsActive = true,
                IsDefault = layoutInfo.IsDefault,
                Description = layoutInfo.Metadata?.TryGetValue("description", out var desc) == true
                    ? desc?.ToString()
                    : null
            };
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            Logger.LogDebug(ex, "Layout file not found: {LayoutName} (module: {Module}, category: {Category})", layoutName, module, category);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.LogWarning(ex, "Unauthorized access when loading layout: {LayoutName} (module: {Module}, category: {Category})", layoutName, module, category);
            return null;
        }
        catch (IOException ex)
        {
            Logger.LogWarning(ex, "IO error when loading layout: {LayoutName} (module: {Module}, category: {Category})", layoutName, module, category);
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unexpected error loading layout '{LayoutName}' from file system (module: {Module}, category: {Category})", layoutName, module, category);
            return null;
        }
    }

    /// <summary>
    /// 从文件系统加载默认布局
    /// </summary>
    private async Task<Layout?> LoadDefaultLayoutFromFileSystemAsync(string module, string category, CancellationToken cancellationToken)
    {
        if (_templateFileParser == null || _templateOptions == null)
            return null;

        try
        {
            var templateRoot = _templateOptions.TemplateRootPath ?? "Templates";
            var extension = _templateOptions.TemplateExtension ?? ".cshtml";

            var relativePath = Path.Combine(templateRoot, "Layouts", category, $"_Default{extension}");

            var searchRoots = BuildSearchRoots(_templateOptions, ServiceProvider);
            var foundPath = TemplatePathHelper.FindFileInSearchRoots(relativePath, searchRoots);

            if (foundPath == null)
                return null;

            var layoutInfo = await _templateFileParser.ParseLayoutFileAsync(foundPath, cancellationToken);
            if (layoutInfo == null)
                return null;

            return new Layout
            {
                LayoutName = layoutInfo.Name ?? "Default",
                Module = module,
                Category = category,
                LayoutContent = layoutInfo.LayoutContent ?? string.Empty,
                IsActive = true,
                IsDefault = true,
                Description = layoutInfo.Metadata?.TryGetValue("description", out var desc) == true
                    ? desc?.ToString()
                    : null
            };
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            Logger.LogDebug(ex, "Default layout file not found for module '{Module}' category '{Category}'", module, category);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.LogWarning(ex, "Unauthorized access when loading default layout (module: {Module}, category: {Category})", module, category);
            return null;
        }
        catch (IOException ex)
        {
            Logger.LogWarning(ex, "IO error when loading default layout (module: {Module}, category: {Category})", module, category);
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unexpected error loading default layout for module '{Module}' category '{Category}'", module, category);
            return null;
        }
    }
}
