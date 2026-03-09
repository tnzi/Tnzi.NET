namespace Tnzi.Extensions;

public static class TnziServiceCollectionExtensions
{
    /// <summary>
    /// 添加 Tnzi.NET 框架（异步配置）
    /// </summary>
    /// <typeparam name="TStartupModule">启动模块类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置对象（必需）</param>
    /// <returns>Tnzi 应用程序实例</returns>
    public static async Task<ITnziApplication> AddTnziAsync<TStartupModule>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TStartupModule : ITnziModule
    {
        Check.NotNull(configuration);

        var application = new TnziApplication(typeof(TStartupModule), services, configuration);

        // 在模块 ConfigureServicesAsync 执行前注册应用实例，
        // 使框架模块能够在服务注册阶段访问模块列表和容器元数据。
        services.TryAddSingleton<ITnziApplication>(application);
        services.TryAddSingleton<IModuleContainer>(new ModuleContainer(application.Modules));

        // 异步配置服务
        await application.ConfigureServicesAsync();

        return application;
    }

    /// <summary>
    /// 初始化 Tnzi.NET 框架（异步方式）
    /// </summary>
    public static async Task UseTnziAsync(this IApplicationBuilder app)
    {
        var application = app.ApplicationServices.GetRequiredService<ITnziApplication>();
        var env = app.ApplicationServices.GetService<IWebHostEnvironment>();
        await application.InitializeAsync(app.ApplicationServices, app, env).ConfigureAwait(false);
    }

    /// <summary>
    /// 替换服务实现（移除所有已注册的实现，然后注册新实现）
    /// </summary>
    /// <typeparam name="TService">服务类型</typeparam>
    /// <typeparam name="TImplementation">实现类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="lifetime">服务生命周期，默认为 Scoped</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection Replace<TService, TImplementation>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TService : class
        where TImplementation : class, TService
    {
        services.RemoveAll<TService>();
        services.Add(ServiceDescriptor.Describe(typeof(TService), typeof(TImplementation), lifetime));
        return services;
    }

    /// <summary>
    /// 替换服务实现（使用工厂方法）
    /// </summary>
    /// <typeparam name="TService">服务类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="implementationFactory">实现工厂方法</param>
    /// <param name="lifetime">服务生命周期，默认为 Scoped</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection Replace<TService>(
        this IServiceCollection services,
        Func<IServiceProvider, TService> implementationFactory,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TService : class
    {
        services.RemoveAll<TService>();
        services.Add(ServiceDescriptor.Describe(typeof(TService), implementationFactory, lifetime));
        return services;
    }

    /// <summary>
    /// 替换服务实现（使用实例，仅支持 Singleton）
    /// </summary>
    /// <typeparam name="TService">服务类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="instance">服务实例</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection Replace<TService>(
        this IServiceCollection services,
        TService instance)
        where TService : class
    {
        services.RemoveAll<TService>();
        services.AddSingleton(instance);
        return services;
    }
}

public interface IModuleContainer
{
    IReadOnlyList<IModuleDescriptor> Modules { get; }
}

public class ModuleContainer : IModuleContainer
{
    public IReadOnlyList<IModuleDescriptor> Modules { get; }
    public ModuleContainer(IReadOnlyList<IModuleDescriptor> modules)
    {
        Modules = modules;
    }
}
