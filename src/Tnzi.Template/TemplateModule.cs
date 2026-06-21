namespace Tnzi.Template;

/// <summary>
/// Tnzi模板引擎模块（业务模块）
/// </summary>
[DependsOn(typeof(EFCoreModule))]
public class TemplateModule : TnziApplicationModule
{
    /// <summary>
    /// 模板模块加载顺序
    /// </summary>
    public override int LoadOrder => 30;
    
    /// <summary>
    /// 表名前缀
    /// </summary>
    public override string? TableNamePrefix => "Template";

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册配置选项并启用启动时验证
        context.Services.AddTnziOptions<TemplateOptions, TemplateOptionsValidator>(context.Configuration);

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册 MemoryCache（如果尚未注册），缓存大小限制从配置读取
        var templateOptions = context.Configuration.GetSection("Template").Get<TemplateOptions>();
        context.Services.AddMemoryCache(cacheOptions =>
        {
            cacheOptions.SizeLimit = templateOptions?.CacheSizeLimit ?? 1000;
        });

        // 注册模板引擎
        context.Services.AddSingleton<ITemplateEngine, RazorTemplateEngine>();

        // 注册通用解析器
        context.Services.AddSingleton<TemplateFileParser>();

        // 注册模板存储服务
        context.Services.AddScoped<ITemplateStoreService, TemplateStoreService>();
        context.Services.AddScoped<ILayoutStoreService, LayoutStoreService>();

        // 注册模板渲染服务（高级 API，统一编排引擎+存储+布局）
        context.Services.AddScoped<ITemplateRenderService, TemplateRenderService>();

        // 注册 HTML→PDF 转换器（默认 HTML 直出，应用层可替换为 PuppeteerSharp/Gotenberg 等实现）
        context.Services.TryAddScoped<IHtmlToPdfConverter, HtmlPassThroughConverter>();

        return Task.CompletedTask;
    }

    /// <summary>
    /// 后配置服务（注册模板选项后配置）
    /// </summary>
    public override Task PostConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册 IPostConfigureOptions 实现，在服务提供者构建后执行程序集扫描和路径配置
        // 这样可以避免 BuildServiceProvider 反模式，同时支持日志记录
        context.Services.AddSingleton<IPostConfigureOptions<TemplateOptions>, Options.TemplateOptionsPostConfigure>();

        return Task.CompletedTask;
    }
}
