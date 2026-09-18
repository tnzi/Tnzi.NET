
using LocalizationOptions = Tnzi.Localization.Options.LocalizationOptions;

namespace Tnzi.Localization;

/// <summary>
/// 本地化模块
/// 提供多语言支持基础设施，支持 Resx 和 JSON 两种资源格式
/// </summary>
[DependsOn(typeof(AspNetCoreModule))]
public class LocalizationModule : TnziFrameworkModule
{
    /// <summary>
    /// 在 AspNetCoreModule 之后加载
    /// </summary>
    public override int LoadOrder => 10;

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册配置选项并启用验证
        context.Services.AddTnziOptions<LocalizationOptions, LocalizationOptionsValidator>(context.Configuration);

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // Code-declared permissions for this module's admin surfaces - the
        // Authorization module's PermissionDbSeeder picks every registered
        // provider up on startup (no-op when Authorization is not loaded).
        context.Services.AddTransient<IPermissionDefinitionProvider, LocalizationPermissions>();

        var options = context.Configuration.GetSection("Localization")
            .Get<LocalizationOptions>() ?? new LocalizationOptions();

        // 如果未启用，跳过配置
        if (!options.Enabled)
        {
            return Task.CompletedTask;
        }

        // 注册缺失翻译追踪器
        context.Services.AddSingleton<IMissingTranslationTracker, MissingTranslationTracker>();

        // 根据资源格式注册本地化服务
        var resourcesPath = options.ResourcesPath ?? "Resources";

        if (options.ResourceFormat == ResourceFormat.Json)
        {
            // JSON 模式：注册 JsonStringLocalizerFactory
            context.Services.AddLocalization(opts =>
            {
                opts.ResourcesPath = resourcesPath;
            });
            // 替换默认的 IStringLocalizerFactory 为 JSON 实现
            context.Services.AddSingleton<IStringLocalizerFactory, JsonStringLocalizerFactory>();
        }
        else
        {
            // Resx 模式：使用默认的 AddLocalization，再把它注册的工厂包进缺失翻译追踪装饰器。
            // ★ 追踪器在上面是无条件注册的，但写入点此前只在 JsonStringLocalizer 里：默认的 Resx 部署上
            // 管理端的「缺失翻译」纵切（4 个端点 + 2 个权限码 + 页面）永远是空的，与「翻译都齐了」看不出区别。
            context.Services.AddLocalization(opts =>
            {
                opts.ResourcesPath = resourcesPath;
            });
            DecorateStringLocalizerFactoryWithTracking(context.Services);
        }

        // 配置支持的语言和语言检测方式
        context.Services.Configure<RequestLocalizationOptions>(opts =>
        {
            var supportedCultures = options.SupportedCultures ?? new[] { "en" };
            opts.SetDefaultCulture(options.DefaultCulture ?? "en")
                .AddSupportedCultures(supportedCultures)
                .AddSupportedUICultures(supportedCultures);

            // 配置语言检测提供者（按优先级顺序）
            opts.RequestCultureProviders.Clear();
            if (options.QueryStringCultureProvider)
            {
                opts.RequestCultureProviders.Add(new QueryStringRequestCultureProvider());
            }
            if (options.CookieCultureProvider)
            {
                opts.RequestCultureProviders.Add(new CookieRequestCultureProvider());
            }
            // Accept-Language header 是最常用的方式，默认启用
            if (options.AcceptLanguageHeaderCultureProvider)
            {
                opts.RequestCultureProviders.Add(new AcceptLanguageHeaderRequestCultureProvider());
            }
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// 把 <c>AddLocalization</c> 注册的 <see cref="IStringLocalizerFactory"/> 换成 <see cref="TrackingStringLocalizerFactory"/>，
    /// 内层仍按原描述符构造（不假设它是哪个具体类型，消费方先于本模块替换过工厂也照样被包住）。
    /// </summary>
    private static void DecorateStringLocalizerFactoryWithTracking(IServiceCollection services)
    {
        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(IStringLocalizerFactory))
            ?? throw new InvalidOperationException(
                "AddLocalization() did not register IStringLocalizerFactory; missing-translation tracking cannot be attached.");

        services.Remove(descriptor);
        services.Add(ServiceDescriptor.Describe(
            typeof(IStringLocalizerFactory),
            sp => new TrackingStringLocalizerFactory(CreateInner(sp, descriptor), sp.GetRequiredService<IMissingTranslationTracker>()),
            descriptor.Lifetime));

        static IStringLocalizerFactory CreateInner(IServiceProvider sp, ServiceDescriptor descriptor)
        {
            if (descriptor.ImplementationInstance is IStringLocalizerFactory instance)
                return instance;
            if (descriptor.ImplementationFactory != null)
                return (IStringLocalizerFactory)descriptor.ImplementationFactory(sp);
            return (IStringLocalizerFactory)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!);
        }
    }

    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        // 注意：UseRequestLocalization() 中间件需要在 AspNetCoreModule 中注册
        // 以确保在异常处理中间件之前执行（中间件顺序问题）
        // 这里不做任何操作，实际的中间件注册在 AspNetCoreModule 中完成

        return Task.CompletedTask;
    }
}
