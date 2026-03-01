
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
        context.Services.AddOptions<LocalizationOptions>()
            .Bind(context.Configuration.GetSection("Localization"))
            .ValidateWith<LocalizationOptions, LocalizationOptionsValidator>();

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
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
            // Resx 模式：使用默认的 AddLocalization
            context.Services.AddLocalization(opts =>
            {
                opts.ResourcesPath = resourcesPath;
            });
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

    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        // 注意：UseRequestLocalization() 中间件需要在 AspNetCoreModule 中注册
        // 以确保在异常处理中间件之前执行（中间件顺序问题）
        // 这里不做任何操作，实际的中间件注册在 AspNetCoreModule 中完成

        return Task.CompletedTask;
    }
}
