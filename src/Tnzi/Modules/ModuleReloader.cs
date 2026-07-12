
namespace Tnzi.Modules;

/// <summary>
/// 模块重载器实现 —— 语义为「模块重初始化（re-initialization）」，而非程序集热重载。
/// <para>
/// 能力边界（重要）：.NET 没有可回收的 <see cref="AssemblyLoadContext"/> 用于框架宿主场景，
/// 无法真正卸载/重新加载已加载的程序集，也不会重新注册 DI 服务。本重载器做的是对目标模块
/// 按序执行生命周期钩子：<c>OnApplicationShutdownAsync</c> → <c>OnApplicationInitializationAsync</c>。
/// 这对「重新加载配置驱动的状态、重建连接、刷新缓存」等有真实价值；模块可实现
/// <see cref="IModuleHotReload"/> 自定义否决、状态保存/恢复与重载后回调。
/// </para>
/// <para>
/// 默认关闭（<see cref="ModuleHotReloadOptions.Enabled"/> = false）。
/// </para>
/// </summary>
[ExperimentalApi(Reason = "Module re-initialization only; assembly unload/reload and service re-registration are not supported")]
public class ModuleReloader : IModuleReloader, IDisposable
{
    private readonly ILogger<ModuleReloader> _logger;
    private readonly ModuleHotReloadOptions _options;
    private readonly ModuleFileWatcher _fileWatcher;
    private readonly ITnziApplication _application;
    private bool _disposed = false;

    /// <summary>
    /// 初始化一个<see cref="ModuleReloader"/>类型的新实例
    /// </summary>
    public ModuleReloader(
        ILogger<ModuleReloader> logger,
        IOptions<ModuleHotReloadOptions> options,
        ModuleFileWatcher fileWatcher,
        ITnziApplication application)
    {
        _logger = Check.NotNull(logger);
        _options = Check.NotNull(options).Value;
        _fileWatcher = Check.NotNull(fileWatcher);
        _application = Check.NotNull(application);

        _fileWatcher.FileChanged += OnFileChanged;
    }

    /// <inheritdoc />
    public bool IsWatching => _fileWatcher.IsWatching;

    /// <inheritdoc />
    public Task StartWatchingAsync()
    {
        if (!_options.Enabled)
        {
            _logger.LogWarning("Module hot reload is disabled. Enable it in ModuleHotReloadOptions to use this feature.");
            return Task.CompletedTask;
        }

        _fileWatcher.StartWatching();
        _logger.LogInformation("Module reloader started watching for file changes");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopWatchingAsync()
    {
        _fileWatcher.StopWatching();
        _logger.LogInformation("Module reloader stopped watching for file changes");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> ReloadModuleAsync(Type moduleType, CancellationToken cancellationToken = default)
    {
        Check.NotNull(moduleType);

        if (!_options.Enabled)
        {
            _logger.LogWarning("Module hot reload is disabled");
            return false;
        }

        // 查找模块描述符
        var moduleDescriptor = _application.Modules.FirstOrDefault(m => m.Type == moduleType);
        if (moduleDescriptor == null)
        {
            _logger.LogWarning("Module not found: {ModuleType}", moduleType.Name);
            return false;
        }

        // 重初始化需要一个已构建的服务容器
        var serviceProvider = _application.ServiceProvider;
        if (serviceProvider == null)
        {
            _logger.LogWarning("Cannot re-initialize module before the application is initialized: {ModuleType}", moduleType.Name);
            return false;
        }

        // 模块可选实现 IModuleHotReload，以获得否决权与状态保存/恢复钩子
        var hotReloadModule = moduleDescriptor.Instance as IModuleHotReload;

        try
        {
            _logger.LogInformation("Re-initializing module: {ModuleType}", moduleType.Name);

            // 重载前处理：允许模块否决本次重初始化
            if (hotReloadModule != null)
            {
                var canReload = await hotReloadModule.OnBeforeReloadAsync().ConfigureAwait(false);
                if (!canReload)
                {
                    _logger.LogWarning("Module refused to reload: {ModuleType}", moduleType.Name);
                    return false;
                }
            }

            // 保存需要跨重初始化保留的状态
            object? state = hotReloadModule != null
                ? await hotReloadModule.GetStateAsync().ConfigureAwait(false)
                : null;

            // 核心语义：对目标模块按序重跑生命周期钩子 —— 先关闭后重新初始化。
            // 不卸载程序集、不重注册 DI 服务，仅重执行模块自身的关闭/初始化逻辑。
            var shutdownContext = new ApplicationShutdownContext(serviceProvider);
            await moduleDescriptor.Instance.OnApplicationShutdownAsync(shutdownContext).ConfigureAwait(false);

            // 重新初始化上下文只携带 ServiceProvider：中间件管道在启动时已构建，
            // 运行期无法重建，因此不提供 IApplicationBuilder（App 为 null）。
            var initContext = new ApplicationInitializationContext(serviceProvider);
            await moduleDescriptor.Instance.OnApplicationInitializationAsync(initContext).ConfigureAwait(false);

            // 恢复状态并触发重载后回调
            if (hotReloadModule != null)
            {
                await hotReloadModule.RestoreStateAsync(state).ConfigureAwait(false);
                await hotReloadModule.OnAfterReloadAsync().ConfigureAwait(false);
            }

            _logger.LogInformation("Module re-initialization completed: {ModuleType}", moduleType.Name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error re-initializing module: {ModuleType}", moduleType.Name);
            return false;
        }
    }

    private void OnFileChanged(object? sender, FileSystemEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("Module file changed: {FilePath}", e.FullPath);

                // 尝试从文件路径推断模块类型（按程序集名匹配）
                var fileName = Path.GetFileNameWithoutExtension(e.FullPath);
                var moduleDescriptor = _application.Modules.FirstOrDefault(m =>
                    m.Type.Assembly.GetName().Name?.Equals(fileName, StringComparison.OrdinalIgnoreCase) == true);

                if (moduleDescriptor != null)
                {
                    await ReloadModuleAsync(moduleDescriptor.Type).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogDebug("Could not find module for changed file: {FilePath}", e.FullPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling file change event: {FilePath}", e.FullPath);
            }
        });
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _fileWatcher.FileChanged -= OnFileChanged;
        _fileWatcher.Dispose();
        _disposed = true;
    }
}
