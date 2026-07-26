namespace Tnzi;

/// <summary>
/// Tnzi 应用程序接口
/// </summary>
public interface ITnziApplication : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// 服务提供者
    /// </summary>
    IServiceProvider ServiceProvider { get; }

    /// <summary>
    /// 已加载的模块列表（按加载顺序排序）
    /// </summary>
    IReadOnlyList<IModuleDescriptor> Modules { get; }

    /// <summary>
    /// 配置服务（异步执行模块的 PreConfigureServices、ConfigureServices、PostConfigureServices）
    /// </summary>
    Task ConfigureServicesAsync();

    /// <summary>
    /// 初始化应用程序（异步执行模块的 OnApplicationInitialization）
    /// </summary>
    Task InitializeAsync(IServiceProvider serviceProvider, IApplicationBuilder? app = null, IWebHostEnvironment? env = null, WebApplication? webApp = null);

    /// <summary>
    /// 关闭应用程序（异步执行模块的 OnApplicationShutdown）
    /// </summary>
    Task ShutdownAsync();
}

/// <summary>
/// Tnzi 应用程序
/// 负责模块的生命周期管理
/// </summary>
public class TnziApplication : ITnziApplication
{
    /// <inheritdoc />
    public IServiceProvider ServiceProvider { get; private set; } = null!;

    /// <inheritdoc />
    public IReadOnlyList<IModuleDescriptor> Modules { get; }

    private readonly IServiceCollection _services;
    private readonly IConfiguration _configuration;
    private bool _servicesConfigured = false;
    private Dictionary<Type, List<ServiceDescriptor>>? _moduleServiceMap;
    private IReadOnlyList<(Type Consumer, Type Wanted)> _skippedOptionalDependencies = [];

    /// <summary>
    /// 创建 Tnzi 应用程序实例
    /// </summary>
    /// <param name="startupModuleType">启动模块类型</param>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置对象（必需）</param>
    public TnziApplication(
        Type startupModuleType,
        IServiceCollection services,
        IConfiguration configuration)
    {
        _services = Check.NotNull(services);
        _configuration = Check.NotNull(configuration);

        var loader = new ModuleLoader();
        Modules = loader.LoadModules(services, startupModuleType);
        _skippedOptionalDependencies = loader.SkippedOptionalDependencies;
    }

    /// <inheritdoc />
    public async Task ConfigureServicesAsync()
    {
        if (_servicesConfigured)
        {
            throw new InvalidOperationException("Services have already been configured.");
        }

        var context = new ServiceConfigurationContext(_services, _configuration);

        // 注册框架内建的模块基础设施服务（健康检查、文件监听、重初始化重载器）。
        // 这些服务作用于模块图本身，注册无条件；文件监听仅在热重载启用时才启动。
        RegisterModuleInfrastructure(context);

        // 第一阶段：PreConfigureServices（所有模块）
        // 用于注册配置选项、验证器等
        foreach (var module in Modules)
        {
            try
            {
                await module.Instance.PreConfigureServicesAsync(context);
            }
            catch (Exception ex)
            {
                throw new ModuleException(module.Type, "PreConfigureServices", ex.Message, ex);
            }
        }

        // 第二阶段：ConfigureServices（所有模块）
        // 主要的服务注册（同时记录每个模块注册的服务，用于依赖审计）
        var moduleServiceMap = new Dictionary<Type, List<ServiceDescriptor>>();
        foreach (var module in Modules)
        {
            try
            {
                var beforeCount = _services.Count;
                await module.Instance.ConfigureServicesAsync(context);
                var newServices = _services.Skip(beforeCount).ToList();
                moduleServiceMap[module.Type] = newServices;
            }
            catch (Exception ex)
            {
                throw new ModuleException(module.Type, "ConfigureServices", ex.Message, ex);
            }
        }

        // 第三阶段：PostConfigureServices（所有模块）
        // 用于覆盖或补充其他模块的配置（注册增量同样计入模块服务映射 - Manifest/依赖审计不留盲区）
        foreach (var module in Modules)
        {
            try
            {
                var beforeCount = _services.Count;
                await module.Instance.PostConfigureServicesAsync(context);
                if (_services.Count > beforeCount)
                {
                    var newServices = _services.Skip(beforeCount).ToList();
                    if (moduleServiceMap.TryGetValue(module.Type, out var existing))
                    {
                        existing.AddRange(newServices);
                    }
                    else
                    {
                        moduleServiceMap[module.Type] = newServices;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new ModuleException(module.Type, "PostConfigureServices", ex.Message, ex);
            }
        }

        // 保存模块服务映射，用于 InitializeAsync 阶段的依赖审计
        _moduleServiceMap = moduleServiceMap;

        _servicesConfigured = true;
    }

    /// <summary>
    /// 注册框架内建的模块基础设施服务：模块健康检查器、文件监听器、重初始化重载器。
    /// 均为操作模块图的框架内部单例；<see cref="ModuleReloader"/> 是否真正启动文件监听
    /// 由 <see cref="ModuleHotReloadOptions.Enabled"/> 控制（默认关闭），此处仅完成注册。
    /// </summary>
    private static void RegisterModuleInfrastructure(ServiceConfigurationContext context)
    {
        context.Services.AddTnziOptions<ModuleHotReloadOptions, ModuleHotReloadOptionsValidator>(context.Configuration);
        context.Services.TryAddSingleton<ModuleHealthChecker>();
        context.Services.TryAddSingleton<ModuleFileWatcher>();
        context.Services.TryAddSingleton<IModuleReloader, ModuleReloader>();
    }

    /// <inheritdoc />
    public async Task InitializeAsync(IServiceProvider serviceProvider, IApplicationBuilder? app = null, IWebHostEnvironment? env = null, WebApplication? webApp = null)
    {
        ServiceProvider = Check.NotNull(serviceProvider);

        // 读取一次全局选项，用于门控模块依赖诊断输出（默认关闭）。
        var tnziOptions = serviceProvider.GetService<IOptions<TnziOptions>>()?.Value;

        // 延迟诊断：模块加载发生在 DI 容器构建之前（无 logger 可用），此处聚合输出
        // 「[OptionalDependsOn] 目标类型可解析（程序集已引用）但模块未加载」的情况。
        // OptionalDependsOn 只对已加载模块排序、从不加载模块；需要某模块时必须将其纳入
        // 启动模块的 [DependsOn] 闭包。但该信息对 HostingModule「刻意用 [OptionalDependsOn]
        // 声明全部业务模块」的智能适配设计是每次启动的持续噪音，因此收进模块依赖审计门控
        // （EnableModuleDependencyAudit，默认关闭），仅在开发者显式开启审计时才输出。
        if (_skippedOptionalDependencies.Count > 0)
        {
            if (tnziOptions?.EnableModuleDependencyAudit == true)
            {
                var diagLogger = serviceProvider.GetService<ILogger<TnziApplication>>();
                if (diagLogger != null)
                {
                    var details = string.Join("; ", _skippedOptionalDependencies
                        .GroupBy(s => s.Wanted)
                        .Select(g => $"{g.Key.Name} (wanted by {string.Join(", ", g.Select(x => x.Consumer.Name).Distinct())})"));
                    diagLogger.LogInformation(
                        "Optional module dependencies referenced but NOT loaded: {Details}. " +
                        "[OptionalDependsOn] only orders already-loaded modules, it never loads them. " +
                        "If a capability is expected, add [DependsOn(typeof(TheModule))] to your startup module.",
                        details);
                }
            }

            _skippedOptionalDependencies = [];
        }

        // 模块依赖审计（通过 TnziOptions 配置启用，建议仅在开发环境使用）
        if (_moduleServiceMap != null)
        {
            if (tnziOptions?.EnableModuleDependencyAudit == true)
            {
                var auditLogger = serviceProvider.GetService<ILogger<TnziApplication>>();
                ModuleDependencyAuditor.Audit(Modules, _moduleServiceMap, auditLogger);
            }

            // 运行时设置消费审计独立门控且默认开启：命中即「admin 改了不生效」的真问题，
            // 噪音低，不应被高噪音的模块依赖审计（默认关闭）连累而失去保护。
            if (tnziOptions?.EnableRuntimeSettingConsumerAudit != false)
            {
                var auditLogger = serviceProvider.GetService<ILogger<TnziApplication>>();
                RuntimeSettingConsumerAuditor.Audit(
                    _moduleServiceMap.SelectMany(kv => kv.Value),
                    _moduleServiceMap.Keys.Select(t => t.Assembly).Distinct(),
                    auditLogger);
            }

            // 惰性化 Manifest：不在启动时对每个模块做全程序集反射构建，而是把服务描述符
            // 交给描述符，首次访问 Manifest（通常仅诊断端点）时才构建。
            foreach (var module in Modules)
            {
                if (module is ModuleDescriptor descriptor &&
                    _moduleServiceMap.TryGetValue(module.Type, out var moduleServices))
                {
                    descriptor.SetManifestSource(moduleServices);
                }
            }

            _moduleServiceMap = null; // 服务映射引用已转交各描述符，这里释放字典本身
        }

        var context = new ApplicationInitializationContext(serviceProvider, app, env, webApp);
        var initializedModules = new List<IModuleDescriptor>();

        foreach (var module in Modules)
        {
            try
            {
                if (module is ModuleDescriptor descriptor)
                {
                    descriptor.InitializationStartTime = DateTime.UtcNow;
                    descriptor.InitializationState = ModuleInitializationState.NotStarted;
                }

                await module.Instance.OnApplicationInitializationAsync(context);

                if (module is ModuleDescriptor descriptor2)
                {
                    descriptor2.InitializationEndTime = DateTime.UtcNow;
                    descriptor2.IsEnabled = true; // 标记为已启用
                    descriptor2.InitializationState = ModuleInitializationState.Succeeded;
                }

                // 记录成功初始化的模块，用于失败时的清理
                initializedModules.Add(module);
            }
            catch (Exception ex)
            {
                if (module is ModuleDescriptor descriptor)
                {
                    descriptor.IsEnabled = false;
                    descriptor.InitializationEndTime = DateTime.UtcNow; // 记录失败时间
                    descriptor.InitializationState = ModuleInitializationState.Failed;
                    descriptor.InitializationError = ex;
                }

                // 对已成功初始化的模块进行清理（按相反顺序）
                if (initializedModules.Count > 0)
                {
                    var shutdownContext = new ApplicationShutdownContext(serviceProvider);
                    var modulesToShutdown = initializedModules.ToList();
                    modulesToShutdown.Reverse();
                    foreach (var initializedModule in modulesToShutdown)
                    {
                        try
                        {
                            await initializedModule.Instance.OnApplicationShutdownAsync(shutdownContext).ConfigureAwait(false);
                        }
                        catch (Exception shutdownEx)
                        {
                            // 清理失败不影响其他模块的清理，只记录日志
                            try
                            {
                                var logger = serviceProvider?.GetService<ILogger<TnziApplication>>();
                                logger?.LogWarning(shutdownEx, "Module {ModuleName} OnApplicationShutdownAsync failed during cleanup", initializedModule.Type.Name);
                            }
                            catch
                            {
                                // 如果日志服务不可用，忽略错误
                            }
                        }
                    }
                }

                throw new ModuleException(module.Type, "OnApplicationInitialization", ex.Message, ex);
            }
        }

        // 仅当显式启用模块热重载（重初始化）时才启动文件监听。
        // 默认关闭，正常启动不产生任何文件监听开销。
        var hotReloadOptions = serviceProvider.GetService<IOptions<ModuleHotReloadOptions>>()?.Value;
        if (hotReloadOptions?.Enabled == true)
        {
            var reloader = serviceProvider.GetService<IModuleReloader>();
            if (reloader != null)
            {
                await reloader.StartWatchingAsync().ConfigureAwait(false);
            }
        }
    }

    private bool _isShutdown = false;
    private readonly SemaphoreSlim _shutdownLock = new(1, 1);

    /// <inheritdoc />
    public async Task ShutdownAsync()
    {
        // 避免重复关闭
        if (_isShutdown || ServiceProvider == null)
        {
            return;
        }

        // 确保线程安全
        await _shutdownLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isShutdown) return;

            var context = new ApplicationShutdownContext(ServiceProvider);

            // 按相反顺序执行关闭逻辑
            foreach (var module in Modules.Reverse())
            {
                try
                {
                    await module.Instance.OnApplicationShutdownAsync(context).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // 关闭时的错误记录但不中断其他模块的关闭
                    // 尝试使用日志服务，如果不可用则忽略
                    try
                    {
                        var logger = ServiceProvider?.GetService<ILogger<TnziApplication>>();
                        logger?.LogWarning(ex, "Module {ModuleName} OnApplicationShutdownAsync failed", module.Type.Name);
                    }
                    catch
                    {
                        // 如果日志服务不可用，忽略错误
                    }
                }
            }

            _isShutdown = true;
        }
        finally
        {
            _shutdownLock.Release();
        }
    }

    private bool _disposed = false;

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        Dispose(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    /// <param name="disposing">是否释放托管资源</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            // 如果尚未关闭应用程序，尝试同步关闭
            // 注意：这是兼容非异步 Dispose 场景的兜底措施。
            // 正常优雅关闭走 TnziShutdownHostedService.StopAsync(异步) 或 DisposeAsync；
            // 此同步分支仅在宿主/测试以同步 Dispose 释放容器时执行。
            if (!_isShutdown)
            {
                try
                {
                    // 同步释放：使用 Task.Run 避免同步上下文死锁
                    Task.Run(ShutdownAsync).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    // 关闭失败不应中断 Dispose，但必须记录（不再静默吞掉）
                    try
                    {
                        ServiceProvider?.GetService<ILogger<TnziApplication>>()?
                            .LogError(ex, "Error during synchronous TnziApplication shutdown in Dispose");
                    }
                    catch
                    {
                        // 日志服务不可用（容器正在释放）时退回控制台，避免完全丢失信息
                        Console.Error.WriteLine($"[Tnzi] Error during synchronous shutdown in Dispose: {ex}");
                    }
                }
            }

            _shutdownLock.Dispose();
        }

        _disposed = true;
    }

    /// <summary>
    /// 异步释放资源核心逻辑
    /// </summary>
    protected virtual async ValueTask DisposeAsyncCore()
    {
        // 异步释放
        await ShutdownAsync().ConfigureAwait(false);

        // DisposeAsync 随后调用的是 Dispose(false)，不会走 disposing 分支，
        // 因此 _shutdownLock 必须在这里释放，否则异步释放路径永远不会释放它
        _shutdownLock.Dispose();
    }
}
