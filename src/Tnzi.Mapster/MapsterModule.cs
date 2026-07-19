
namespace Tnzi.Mapster;

public class MapsterModule : TnziInfrastructureModule
{
    private int _mappingConfigCount;

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var config = new TypeAdapterConfig();

        // 优先使用模块程序集进行扫描（更精确，避免扫描无关的系统/第三方程序集）
        var assemblies = GetAssembliesForScanning(context.Services);
        var mappingConfigTypes = assemblies
            .SelectMany(a =>
            {
                try
                {
                    return a.GetTypes();
                }
                catch (ReflectionTypeLoadException)
                {
                    // 忽略无法加载的程序集
                    return Array.Empty<Type>();
                }
            })
            .Where(t => typeof(IMappingConfig).IsAssignableFrom(t)
                && !t.IsInterface
                && !t.IsAbstract)
            .ToList();

        // 创建配置上下文并执行配置
        var configContext = new MapsterMappingConfigContext(config);

        // 在配置阶段无法使用日志服务，错误会在应用启动时通过异常暴露
        foreach (var configType in mappingConfigTypes)
        {
            try
            {
                var mappingConfig = Activator.CreateInstance(configType) as IMappingConfig;
                if (mappingConfig != null)
                {
                    mappingConfig.Configure(configContext);
                }
            }
            catch (Exception ex)
            {
                // 在配置阶段无法使用日志服务，错误信息会通过异常暴露
                // 重新抛出异常，确保配置问题能被及时发现
                throw new InvalidOperationException(
                    $"Failed to configure mapping: {configType.FullName}. See inner exception for details.", ex);
            }
        }

        // 缓存映射配置数量，供 OnApplicationInitializationAsync 使用，避免重复扫描程序集
        _mappingConfigCount = mappingConfigTypes.Count;

        context.Services.AddSingleton(config);

        // 将 IMapper 注册为 Singleton，因为 ServiceMapper 与 TypeAdapterConfig 均为线程安全实例。
        // 这里使用模块级 TypeAdapterConfig，避免依赖 Mapster 全局静态配置导致不同宿主之间互相污染。
        context.Services.AddSingleton<IMapper>(sp => new ServiceMapper(sp, config));

        // 核心运行时映射抽象（IObjectMapper）的 Mapster 实现，委托到静态 MapperExtensions。
        // 让 Tnzi 核心类型（如 CrudAppService）无需引用本程序集即可完成映射。
        context.Services.AddSingleton<IObjectMapper, MapsterObjectMapper>();

        return Task.CompletedTask;
    }

    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var config = context.ServiceProvider.GetRequiredService<TypeAdapterConfig>();
        var mapper = context.ServiceProvider.GetRequiredService<IMapper>();
        MapperExtensions.SetMapper(mapper, config);

        var logger = context.ServiceProvider.GetService<ILogger<MapsterModule>>();
        logger?.LogInformation("MapperExtensions initialized with singleton IMapper instance.");

        // 使用 ConfigureServicesAsync 阶段缓存的计数，避免重复扫描程序集
        logger?.LogDebug("Loaded {Count} mapping configuration(s)", _mappingConfigCount);

        // 验证所有映射配置，提前发现配置错误
        try
        {
            config.Compile();
            logger?.LogDebug("Mapster mapping configuration validation passed.");
        }
        catch (Exception ex)
        {
            // 记录警告但不阻止应用启动，避免因映射配置问题导致整个应用无法启动
            logger?.LogWarning(ex, "Mapster mapping configuration validation failed: {Message}", ex.Message);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取用于扫描 IMappingConfig 的程序集列表
    /// 优先从 ITnziApplication.Modules 获取模块程序集（更精确），
    /// 如果不可用则回退到 AppDomain.CurrentDomain.GetAssemblies()
    /// </summary>
    private static Assembly[] GetAssembliesForScanning(IServiceCollection services)
    {
        // 尝试从已注册的 ITnziApplication 获取模块程序集
        // ITnziApplication 在 ConfigureServicesAsync 完成后才注册到 DI，
        // 但我们可以从 IServiceCollection 中查找已添加的 singleton 实例
        var appDescriptor = services.FirstOrDefault(s =>
            s.ServiceType == typeof(ITnziApplication) && s.ImplementationInstance != null);

        if (appDescriptor?.ImplementationInstance is ITnziApplication app && app.Modules.Count > 0)
        {
            // 使用模块程序集，更精确地定位 IMappingConfig 实现
            return app.Modules
                .Select(m => m.Assembly)
                .Distinct()
                .ToArray();
        }

        // 回退到扫描所有已加载的程序集
        return AppDomain.CurrentDomain.GetAssemblies();
    }
}
