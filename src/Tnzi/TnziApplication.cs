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
    /// 获取服务集合（仅在 ServiceProvider 构建之前可用，构建后为 null）
    /// 用于模块在 OnApplicationInitialization 阶段补充注册服务
    /// </summary>
    [ExperimentalApi(Reason = "Late service registration may be replaced by a more structured approach in future versions")]
    IServiceCollection? Services { get; }

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

    /// <inheritdoc />
    public IServiceCollection? Services => _services;

    private readonly IServiceCollection _services;
    private readonly IConfiguration _configuration;
    private bool _servicesConfigured = false;
    private Dictionary<Type, List<ServiceDescriptor>>? _moduleServiceMap;

    // 缓存空的服务提供者，避免重复创建
    private static readonly IServiceProvider EmptyServiceProvider = new ServiceCollection().BuildServiceProvider();

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
    }

    /// <inheritdoc />
    public async Task ConfigureServicesAsync()
    {
        if (_servicesConfigured)
        {
            throw new InvalidOperationException("Services have already been configured.");
        }

        var context = new ServiceConfigurationContext(_services, _configuration);

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
        // 用于覆盖或补充其他模块的配置
        foreach (var module in Modules)
        {
            try
            {
                await module.Instance.PostConfigureServicesAsync(context);
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

    /// <inheritdoc />
    public async Task InitializeAsync(IServiceProvider serviceProvider, IApplicationBuilder? app = null, IWebHostEnvironment? env = null, WebApplication? webApp = null)
    {
        ServiceProvider = Check.NotNull(serviceProvider);

        // 模块依赖审计（通过 TnziOptions 配置启用，建议仅在开发环境使用）
        if (_moduleServiceMap != null)
        {
            var tnziOptions = serviceProvider.GetService<IOptions<TnziOptions>>()?.Value;
            if (tnziOptions?.EnableModuleDependencyAudit == true)
            {
                var auditLogger = serviceProvider.GetService<ILogger<TnziApplication>>();
                ModuleDependencyAuditor.Audit(Modules, _moduleServiceMap, auditLogger);
            }
            _moduleServiceMap = null; // 审计完成后释放
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
    }

    /// <inheritdoc />
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
            // 注意：这是为了兼容非异步 Dispose 场景的保底措施
            if (!_isShutdown)
            {
                try
                {
                    // 同步释放：使用 AsyncHelper.RunSync 避免死锁
                    AsyncHelper.RunSync(ShutdownAsync);
                }
                catch (Exception)
                {
                    // 忽略关闭过程中的错误，避免中断 Dispose
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

        // 这里的 Dispose(false) 会由公开的 DisposeAsync 方法调用，因此这里不需要处理 _shutdownLock
        // 但为了完整性，如果 DisposeAsyncCore 被重写并未调用 ShutdownAsync，需要注意
    }

    /// <summary>
    /// 统一注册所有业务模块的控制器程序集
    /// 在所有模块的 ConfigureServices 执行完成后调用，确保 IMvcBuilder 已注册
    /// </summary>

}
