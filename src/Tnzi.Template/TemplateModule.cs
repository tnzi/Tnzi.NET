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
        // Code-declared permissions for this module's admin surfaces - the
        // Authorization module's PermissionDbSeeder picks every registered
        // provider up on startup (no-op when Authorization is not loaded).
        context.Services.AddTransient<IPermissionDefinitionProvider, TemplatePermissions>();

        // 模板编译缓存自持：Template:CacheSizeLimit 只作用于这一个实例。
        // ★ 此前这里是 AddMemoryCache(o => o.SizeLimit = CacheSizeLimit)，而 AddMemoryCache(setup) 只是
        // services.Configure(setup)：它与核心 CachingModule 的委托按注册顺序作用于同一个 MemoryCacheOptions，
        // 本模块后跑 ⇒ 全进程共享的 IMemoryCache 被封顶 1000，Caching:MemorySizeLimit「为空即不限」被静默顶掉；
        // 更糟的是 MemoryCache 在设了 SizeLimit 后拒绝任何不带 Size 的写入（InvalidOperationException），
        // 别的模块往共享缓存里写时并不都带 Size —— 行级数据授权的过滤器缓存写入当场 500。
        // 业务模块不得改全局 MemoryCacheOptions；要限自己的条目数，就自己开一个实例。
        context.Services.AddKeyedSingleton<IMemoryCache>(RazorTemplateEngine.CacheServiceKey, static (sp, _) =>
        {
            var sizeLimit = sp.GetRequiredService<IOptions<TemplateOptions>>().Value.CacheSizeLimit;
            return new MemoryCache(new MemoryCacheOptions { SizeLimit = sizeLimit > 0 ? sizeLimit : null });
        });

        // 注册模板引擎
        context.Services.AddSingleton<ITemplateEngine, RazorTemplateEngine>();

        // 注册通用解析器
        context.Services.AddSingleton<TemplateFileParser>();

        // 注册文件模板读取服务（消费方读取模板文件 front matter 自述内容的受支持入口）
        context.Services.AddSingleton<ITemplateFileService, TemplateFileService>();

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
        context.Services.AddSingleton<IPostConfigureOptions<TemplateOptions>, TemplateOptionsPostConfigure>();

        return Task.CompletedTask;
    }
}
