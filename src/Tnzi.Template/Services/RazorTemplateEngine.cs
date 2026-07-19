namespace Tnzi.Template.Services;

/// <summary>
/// Razor模板引擎实现
/// </summary>
public class RazorTemplateEngine : ITemplateEngine
{
    private readonly TemplateOptions _options;
    private readonly ILogger<RazorTemplateEngine> _logger;
    private readonly IMemoryCache _cache;
    private readonly RazorEngine _razorEngine;
    private readonly string _normalizedRootPath;
    private readonly ConcurrentDictionary<string, byte> _trackedCacheKeys = new();

    // 缓存键前缀，用于区分不同类型的缓存
    private const string CacheKeyPrefix = "Tnzi.Template:";

    public RazorTemplateEngine(
        IOptions<TemplateOptions> options,
        ILogger<RazorTemplateEngine> logger,
        IMemoryCache cache)
    {
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
        _cache = Check.NotNull(cache);

        _razorEngine = new RazorEngine();

        // 预计算并规范化根路径
        _normalizedRootPath = GetNormalizedRootPath();
    }

    /// <summary>
    /// 从字符串渲染模板
    /// </summary>
    public async Task<string> RenderAsync(string templateContent, object? model = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(templateContent))
            return templateContent ?? string.Empty;

        try
        {
            // 生成缓存键（优化：对短模板使用简单哈希）
            var cacheKey = GenerateCacheKey(templateContent);

            // 获取或编译模板
            var compiledTemplate = await GetOrCompileTemplateAsync(cacheKey, templateContent, cancellationToken);

            // 将模型转换为安全动态对象，访问不存在的属性时返回 null 而不是抛出异常
            var safeModel = ConvertToSafeDynamicObject(model);

            // 执行模板渲染
            var result = await compiledTemplate.RunAsync(safeModel);

            return result ?? string.Empty;
        }
        catch (TemplateException)
        {
            // 已经是模板异常，直接抛出
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rendering template from string");
            throw new TemplateRenderException($"Template rendering failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 从文件渲染模板
    /// </summary>
    public async Task<string> RenderFromFileAsync(string filePath, object? model = null, string? layoutPath = null, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(filePath);

        try
        {
            string fullPath;

            // 如果已经是绝对路径（通常来自 RenderFromNameAsync），直接使用
            if (Path.IsPathRooted(filePath))
            {
                // 验证绝对路径是否在模板根目录下（安全检查）
                var normalizedPath = Path.GetFullPath(filePath);
                if (!normalizedPath.StartsWith(_normalizedRootPath, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Path traversal attempt detected: {FilePath}", filePath);
                    throw new TemplateSecurityException($"Access denied: Path must be within template root directory.", filePath);
                }
                fullPath = normalizedPath;
            }
            else
            {
                // 相对路径：先添加扩展名（如果需要），然后解析路径
                if (!filePath.EndsWith(_options.TemplateExtension, StringComparison.OrdinalIgnoreCase))
                {
                    filePath += _options.TemplateExtension;
                }

                // 解析文件路径（带安全验证）
                fullPath = ResolveTemplatePath(filePath);
            }

            if (!File.Exists(fullPath))
            {
                throw new TemplateNotFoundException(fullPath);
            }

            // 读取模板内容（使用缓存）
            var templateContent = await GetOrReadFileContentAsync(fullPath, cancellationToken);

            // 渲染模板
            var renderedContent = await RenderAsync(templateContent, model, cancellationToken);

            // 如果指定了Layout，应用Layout（传递模板变量）
            if (!string.IsNullOrWhiteSpace(layoutPath))
            {
                renderedContent = await ApplyLayoutAsync(renderedContent, layoutPath, model, cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(_options.DefaultLayoutPath))
            {
                renderedContent = await ApplyLayoutAsync(renderedContent, _options.DefaultLayoutPath, model, cancellationToken);
            }

            return renderedContent;
        }
        catch (TemplateException)
        {
            // 已经是模板异常，直接抛出
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rendering template from file: {FilePath}", filePath);
            throw new TemplateRenderException($"Template rendering failed for file '{filePath}': {ex.Message}", ex, filePath);
        }
    }

    /// <summary>
    /// 从模板名称渲染（从模板存储加载）
    /// </summary>
    public Task<string> RenderFromNameAsync(string templateName, object? model = null, string? layoutName = null, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(templateName);

        // templateName 应该是相对于模板根目录的路径（如 "Module/Category/Template"）
        // 先添加扩展名（如果需要），然后使用路径解析逻辑进行安全检查
        if (!templateName.EndsWith(_options.TemplateExtension, StringComparison.OrdinalIgnoreCase))
        {
            templateName += _options.TemplateExtension;
        }

        // ResolveTemplatePath 会进行路径规范化并防止路径遍历攻击，返回绝对路径
        var fullPath = ResolveTemplatePath(templateName);

        // 直接使用绝对路径调用 RenderFromFileAsync（传入绝对路径时，RenderFromFileAsync 会跳过路径解析）
        return RenderFromFileAsync(fullPath, model, layoutName, cancellationToken);
    }

    /// <summary>
    /// 验证模板语法
    /// </summary>
    public async Task<TemplateValidationResult> ValidateAsync(string templateContent)
    {
        if (string.IsNullOrWhiteSpace(templateContent))
        {
            return TemplateValidationResult.Failed("Template content cannot be null or empty.");
        }

        try
        {
            // 尝试编译模板以验证语法
            var cacheKey = GenerateCacheKey(templateContent);
            await GetOrCompileTemplateAsync(cacheKey, templateContent);

            return TemplateValidationResult.Success();
        }
        catch (Exception ex)
        {
            return TemplateValidationResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// 预编译模板（应用启动时调用）
    /// </summary>
    public async Task PrecompileTemplatesAsync(IEnumerable<string> templatePaths, CancellationToken cancellationToken = default)
    {
        Check.NotNull(templatePaths);

        // 转换为列表以避免多次枚举，并缓存计数
        var pathsList = templatePaths.ToList();
        var totalCount = pathsList.Count;

        var tasks = pathsList.Select(async path =>
        {
            try
            {
                var fullPath = ResolveTemplatePath(path);
                if (!File.Exists(fullPath))
                {
                    _logger.LogWarning("Template file not found for precompilation: {Path}", fullPath);
                    return;
                }

                var content = await GetOrReadFileContentAsync(fullPath, cancellationToken);
                var cacheKey = GenerateCacheKey(content);
                await GetOrCompileTemplateAsync(cacheKey, content, cancellationToken);

                _logger.LogDebug("Precompiled template: {Path}", path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to precompile template: {Path}", path);
            }
        });

        await Task.WhenAll(tasks);
        _logger.LogInformation("Template precompilation completed. {Count} templates processed.", totalCount);
    }

    /// <summary>
    /// 清除模板缓存
    /// 仅移除本引擎管理的缓存条目，不影响 IMemoryCache 中其他模块的缓存
    /// </summary>
    public void ClearCache()
    {
        var keys = _trackedCacheKeys.Keys.ToList();
        foreach (var key in keys)
        {
            _cache.Remove(key);
        }

        _trackedCacheKeys.Clear();
        _logger.LogInformation("Template cache cleared. Removed {Count} entries.", keys.Count);
    }

    /// <summary>
    /// 将模型转换为安全的动态对象
    /// 当访问不存在的属性时返回 null（而不是抛出异常）
    /// </summary>
    private static object? ConvertToSafeDynamicObject(object? model)
    {
        if (model == null)
            return null;

        // 如果已经是 SafeDynamicObject，直接返回
        if (model is SafeDynamicObject)
            return model;

        // 如果是字典，转换为 SafeDynamicObject
        if (model is IDictionary<string, object> dict)
        {
            return SafeDynamicObject.FromDictionary(dict);
        }

        // 如果是可空字典，转换为 SafeDynamicObject
        if (model is IDictionary<string, object?> nullableDict)
        {
            return SafeDynamicObject.FromNullableDictionary(nullableDict);
        }

        // 如果是 ExpandoObject，转换为 SafeDynamicObject
        if (model is ExpandoObject expando)
        {
            return SafeDynamicObject.FromNullableDictionary((IDictionary<string, object?>)expando);
        }

        // 对于普通对象（如匿名类型），转换为 SafeDynamicObject
        var properties = model.GetType().GetProperties();
        var values = new Dictionary<string, object?>();
        foreach (var prop in properties)
        {
            values[prop.Name] = prop.GetValue(model);
        }
        return SafeDynamicObject.FromNullableDictionary(values);
    }

    /// <summary>
    /// 生成缓存键（使用 MD5 哈希确保唯一性）
    /// </summary>
    private static string GenerateCacheKey(string content)
    {
        // 统一使用 MD5 哈希，避免 GetHashCode() 可能产生的哈希冲突
        return $"{CacheKeyPrefix}{content.ToMd5()}";
    }

    /// <summary>
    /// 获取或编译模板（使用 IMemoryCache 支持过期策略）
    /// </summary>
    private async Task<IRazorEngineCompiledTemplate> GetOrCompileTemplateAsync(string cacheKey, string templateContent, CancellationToken cancellationToken = default)
    {
        // 如果禁用缓存，直接编译
        if (!_options.EnableCache)
        {
            return await CompileTemplateAsync(templateContent, cancellationToken);
        }

        // 使用 GetOrCreateAsync 模式
        var compiledTemplate = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            // 设置缓存过期策略
            entry.SlidingExpiration = TimeSpan.FromSeconds(_options.CacheExpirationSeconds);
            entry.Size = 1;  // 用于限制缓存大小

            // 注册淘汰回调，自动清理跟踪集合
            entry.RegisterPostEvictionCallback((key, _, _, _) => _trackedCacheKeys.TryRemove(key.ToString()!, out _));

            // 跟踪缓存键
            _trackedCacheKeys.TryAdd(cacheKey, 0);

            return await CompileTemplateAsync(templateContent, cancellationToken);
        });

        // GetOrCreateAsync 理论上不应该返回 null，但如果发生异常情况，提供清晰的错误信息
        if (compiledTemplate == null)
        {
            _logger.LogError("Failed to compile or retrieve template from cache. Cache key: {CacheKey}", cacheKey);
            throw new InvalidOperationException($"Failed to compile or retrieve template from cache. Cache key: {cacheKey}");
        }

        return compiledTemplate;
    }

    /// <summary>
    /// 编译模板（使用自定义基类和引用）
    /// </summary>
    private async Task<IRazorEngineCompiledTemplate> CompileTemplateAsync(string templateContent, CancellationToken cancellationToken = default)
    {
        try
        {
            // 使用编译选项：自定义基类 + 添加常用命名空间引用
            return await _razorEngine.CompileAsync(templateContent, builder =>
            {
                // 使用自定义模板基类，提供 Raw、HtmlEncode、DateTime 等方法
                builder.Inherits(typeof(TemplateBase));

                // 添加常用命名空间引用
                builder.AddUsing("System");
                builder.AddUsing("System.Linq");
                builder.AddUsing("System.Collections.Generic");
                builder.AddUsing("System.Text");

                // 添加程序集引用
                builder.AddAssemblyReferenceByName("System.Runtime");
                builder.AddAssemblyReferenceByName("System.Collections");
                builder.AddAssemblyReferenceByName("netstandard");
                builder.AddAssemblyReference(typeof(TemplateBase));  // 自定义基类所在程序集
            });
        }
        catch (TemplateCompilationException)
        {
            // 已经是模板编译异常，直接抛出
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Template compilation failed");
            throw new TemplateCompilationException($"Template compilation failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 获取或读取文件内容（带缓存和热重载支持）
    /// </summary>
    /// <param name="fullPath">模板文件的完整路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>文件内容</returns>
    /// <remarks>
    /// 此方法实现了文件内容的缓存和热重载功能：
    /// 1. 在热重载模式下，快速检查缓存中的文件时间戳，如果文件未修改则直接返回缓存内容
    /// 2. 使用 GetOrCreateAsync 确保文件读取的原子性，避免并发读取导致的不一致
    /// 3. 在读取文件后再次检查文件时间戳，防止在读取过程中文件被修改
    /// </remarks>
    private async Task<string> GetOrReadFileContentAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{CacheKeyPrefix}file:{fullPath}";

        // 热重载模式：快速检查缓存（避免进入回调，提高性能）
        if (_options.EnableHotReload)
        {
            if (_cache.TryGetValue<FileContentCacheEntry>(cacheKey, out var cached) && cached != null)
            {
                var currentLastWrite = File.GetLastWriteTimeUtc(fullPath);
                if (cached.LastWriteTime == currentLastWrite)
                {
                    return cached.Content;
                }

                // 文件已修改，清除旧缓存（让 GetOrCreateAsync 重新读取）
                _cache.Remove(cacheKey);
            }
        }

        // 从缓存获取或读取文件（在回调内统一处理文件时间检查和内容读取，确保原子性）
        var entry = await _cache.GetOrCreateAsync(cacheKey, async cacheEntry =>
        {
            const int maxRetries = 3;
            var retryCount = 0;
            DateTime lastWrite;
            string content;

            // 读取文件时间（在回调开始时获取，确保原子性）
            lastWrite = File.GetLastWriteTimeUtc(fullPath);
            content = await File.ReadAllTextAsync(fullPath, cancellationToken);

            // 读取后再次检查文件时间（防止在读取过程中文件被修改）
            // 最多重试 maxRetries 次，防止极端情况下的无限循环
            while (retryCount < maxRetries)
            {
                var verifyLastWrite = File.GetLastWriteTimeUtc(fullPath);
                if (verifyLastWrite == lastWrite)
                {
                    // 文件时间未变化，使用当前内容
                    break;
                }

                // 文件在读取过程中被修改，使用最新的时间和内容
                lastWrite = verifyLastWrite;
                content = await File.ReadAllTextAsync(fullPath, cancellationToken);
                retryCount++;
            }

            // 如果达到最大重试次数，记录警告（文件可能在频繁修改）
            if (retryCount >= maxRetries)
            {
                _logger.LogWarning("File '{FilePath}' was modified multiple times during read. Using latest content after {RetryCount} retries.", fullPath, retryCount);
            }

            var fileEntry = new FileContentCacheEntry(content, lastWrite);

            // 设置缓存选项
            cacheEntry.SlidingExpiration = TimeSpan.FromSeconds(_options.CacheExpirationSeconds);
            cacheEntry.Size = 1;

            // 注册淘汰回调并跟踪缓存键
            cacheEntry.RegisterPostEvictionCallback((key, _, _, _) => _trackedCacheKeys.TryRemove(key.ToString()!, out _));
            _trackedCacheKeys.TryAdd(cacheKey, 0);

            return fileEntry;
        });

        return entry?.Content ?? throw new InvalidOperationException($"Failed to load template: {fullPath}");
    }

    /// <summary>
    /// 获取规范化的根路径
    /// 使用 AppContext.BaseDirectory（输出目录）而非 ContentRootPath（源代码目录）
    /// 这确保在调试时能正确找到通过 CopyToOutputDirectory 复制到 bin 目录的模板文件
    /// </summary>
    private string GetNormalizedRootPath()
    {
        var rootPath = _options.TemplateRootPath;

        if (string.IsNullOrWhiteSpace(rootPath))
        {
            rootPath = "Templates";
        }

        // 如果是相对路径，基于应用程序输出目录（AppContext.BaseDirectory）
        // 这是模板文件复制到的实际位置（如 bin/Debug/net10.0）
        if (!Path.IsPathRooted(rootPath))
        {
            rootPath = Path.Combine(AppContext.BaseDirectory, rootPath);
        }

        // 规范化路径（解析 .. 等）
        return Path.GetFullPath(rootPath);
    }

    /// <summary>
    /// 解析模板文件路径（带安全验证，防止路径遍历攻击）
    /// </summary>
    /// <param name="filePath">模板文件路径（可以是相对路径或绝对路径）</param>
    /// <returns>规范化后的完整路径</returns>
    /// <exception cref="TemplateSecurityException">当检测到路径遍历攻击时抛出</exception>
    /// <remarks>
    /// 此方法实现了路径解析和安全验证：
    /// 1. 对于绝对路径，验证其是否在模板根目录下
    /// 2. 对于相对路径，组合根路径并规范化，然后验证是否仍在根目录下
    /// 3. 防止使用 ../ 等路径遍历攻击访问根目录外的文件
    /// </remarks>
    private string ResolveTemplatePath(string filePath)
    {
        // 如果是绝对路径，验证是否在根目录下
        if (Path.IsPathRooted(filePath))
        {
            var normalizedPath = Path.GetFullPath(filePath);

            // 安全检查：确保路径在模板根目录下
            if (!normalizedPath.StartsWith(_normalizedRootPath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Path traversal attempt detected: {FilePath}", filePath);
                throw new TemplateSecurityException($"Access denied: Path must be within template root directory.", filePath);
            }

            return normalizedPath;
        }

        // 相对路径：组合并规范化
        var fullPath = Path.GetFullPath(Path.Combine(_normalizedRootPath, filePath));

        // 安全检查：确保解析后的路径仍在根目录下（防止 ../ 攻击）
        if (!fullPath.StartsWith(_normalizedRootPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Path traversal attempt detected: {FilePath}", filePath);
            throw new TemplateSecurityException($"Path traversal detected: '{filePath}'", filePath);
        }

        return fullPath;
    }

    /// <summary>
    /// 应用Layout（支持传递模板变量）
    /// 将模板变量和Content占位符一起传递给Layout渲染
    /// </summary>
    private async Task<string> RenderLayoutAsync(string layoutContent, string bodyContent, object? templateModel = null, CancellationToken cancellationToken = default)
    {
        // 使用 SafeDynamicObject 统一处理模型转换，并添加 Content 属性
        var safeModel = ConvertToSafeDynamicObject(templateModel);
        var layoutDict = safeModel is SafeDynamicObject sdo
            ? new Dictionary<string, object?>(sdo.GetMembers())
            : new Dictionary<string, object?>();

        layoutDict["Content"] = bodyContent;

        return await RenderAsync(layoutContent, layoutDict, cancellationToken);
    }

    /// <summary>
    /// 应用Layout文件（支持传递模板变量）
    /// </summary>
    private async Task<string> ApplyLayoutAsync(string bodyContent, string layoutPath, object? templateModel = null, CancellationToken cancellationToken = default)
    {
        // 确保Layout路径有扩展名
        if (!layoutPath.EndsWith(_options.TemplateExtension, StringComparison.OrdinalIgnoreCase))
        {
            layoutPath += _options.TemplateExtension;
        }

        var fullLayoutPath = ResolveTemplatePath(layoutPath);

        if (!File.Exists(fullLayoutPath))
        {
            _logger.LogWarning("Layout file not found: {LayoutPath}, skipping layout application.", fullLayoutPath);
            return bodyContent;
        }

        var layoutContent = await GetOrReadFileContentAsync(fullLayoutPath, cancellationToken);
        return await RenderLayoutAsync(layoutContent, bodyContent, templateModel, cancellationToken);
    }
}
