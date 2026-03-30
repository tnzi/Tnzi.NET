namespace Tnzi.Template.Options;

/// <summary>
/// TemplateOptions 后配置（在服务提供者构建后执行）
/// 用于扫描程序集并添加模板搜索路径，避免 BuildServiceProvider 反模式
/// </summary>
internal class TemplateOptionsPostConfigure : IPostConfigureOptions<TemplateOptions>
{
    private readonly ILogger<TemplateOptionsPostConfigure>? _logger;

    // 静态缓存已扫描的程序集列表（线程安全，只扫描一次）
    private static readonly Lazy<HashSet<Assembly>> _cachedApplicationModuleAssemblies =
        new Lazy<HashSet<Assembly>>(() => ScanApplicationModuleAssemblies(),
            LazyThreadSafetyMode.ExecutionAndPublication);

    public TemplateOptionsPostConfigure(ILogger<TemplateOptionsPostConfigure>? logger = null)
    {
        _logger = logger;
    }

    public void PostConfigure(string? name, TemplateOptions options)
    {
        // 使用缓存的程序集列表（避免重复扫描）
        var applicationModuleAssemblies = _cachedApplicationModuleAssemblies.Value;

        _logger?.LogInformation("Found {Count} application module assemblies for template scanning", applicationModuleAssemblies.Count);

        // 添加模板搜索路径
        var addedPaths = 0;
        foreach (var assembly in applicationModuleAssemblies)
        {
            try
            {
                // 动态程序集没有 Location，跳过
                if (assembly.IsDynamic)
                {
                    _logger?.LogDebug("Skipping dynamic assembly for template path: {AssemblyName}", assembly.GetName().Name);
                    continue;
                }

                var assemblyPath = assembly.Location;
                if (string.IsNullOrWhiteSpace(assemblyPath))
                {
                    _logger?.LogDebug("Assembly has no location: {AssemblyName}", assembly.GetName().Name);
                    continue;
                }

                var directoryName = Path.GetDirectoryName(assemblyPath);
                if (string.IsNullOrWhiteSpace(directoryName))
                {
                    _logger?.LogWarning("Cannot determine directory for assembly: {AssemblyPath}", assemblyPath);
                    continue;
                }

                var templatesPath = Path.Combine(directoryName, "Templates");

                // 将模块的 Templates 目录添加为额外搜索路径（优先级低于应用层）
                if (Directory.Exists(templatesPath))
                {
                    if (!options.AdditionalSearchPaths.Contains(templatesPath))
                    {
                        options.AdditionalSearchPaths.Add(templatesPath);
                        addedPaths++;
                        _logger?.LogDebug("Added template search path: {Path} (from assembly: {AssemblyName})", templatesPath, assembly.GetName().Name);
                    }
                    else
                    {
                        _logger?.LogDebug("Template search path already exists: {Path}", templatesPath);
                    }
                }
                else
                {
                    _logger?.LogDebug("Template directory does not exist: {Path} (from assembly: {AssemblyName})", templatesPath, assembly.GetName().Name);
                }
            }
            catch (PathTooLongException ex)
            {
                _logger?.LogWarning(ex, "Path too long for assembly: {AssemblyName}", assembly.GetName().Name);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger?.LogWarning(ex, "Unauthorized access to assembly path: {AssemblyName}", assembly.GetName().Name);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Unexpected error while processing assembly template path: {AssemblyName}. Error: {Error}", assembly.GetName().Name, ex.Message);
            }
        }

        _logger?.LogInformation("Added {Count} template search paths from application modules", addedPaths);
    }

    /// <summary>
    /// 扫描所有已加载的程序集，查找包含 TnziApplicationModule 的程序集
    /// </summary>
    private static HashSet<Assembly> ScanApplicationModuleAssemblies()
    {
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        var applicationModuleAssemblies = new HashSet<Assembly>();

        foreach (var assembly in loadedAssemblies)
        {
            // 跳过动态程序集（没有文件路径）
            if (assembly.IsDynamic)
            {
                continue;
            }

            try
            {
                // 检查程序集中是否包含 TnziApplicationModule 的子类
                var hasApplicationModule = assembly.GetTypes()
                    .Any(t => t.IsClass && 
                             !t.IsAbstract && 
                             typeof(TnziApplicationModule).IsAssignableFrom(t));

                if (hasApplicationModule)
                {
                    applicationModuleAssemblies.Add(assembly);
                }
            }
            catch (ReflectionTypeLoadException)
            {
                // 程序集类型加载失败（常见于部分加载的程序集）
                // 静默忽略，不记录日志（避免在静态方法中依赖日志）
            }
            catch (FileNotFoundException)
            {
                // 程序集文件未找到（可能已被卸载或移动）
                // 静默忽略，不记录日志（避免在静态方法中依赖日志）
            }
            catch (BadImageFormatException)
            {
                // 程序集格式错误
                // 静默忽略，不记录日志（避免在静态方法中依赖日志）
            }
            catch
            {
                // 其他异常
                // 静默忽略，不记录日志（避免在静态方法中依赖日志）
            }
        }

        return applicationModuleAssemblies;
    }
}
