namespace Tnzi.Documents;

/// <summary>
/// 文档原语模块：Office 转 PDF、PDF 读取与定位、PDF 盖章压平三件事。
/// </summary>
/// <remarks>
/// <para>可选加载。三个原语与业务无关（电子签署、合同归档、报表出图都能用），重量级 PDF 依赖
/// （PDFsharp / PdfPig）收在本包内，核心与其它消费者不被传递拉入 —— 与
/// <c>Tnzi.Finance.Documents</c> 把渲染依赖收进可选子模块是同一条路子，区别在于
/// 那个包是财务专用的票据渲染，本包是通用 PDF 原语。</para>
/// <para>三个实现都经 <c>TryAddSingleton</c> 注册，消费应用先注册自己的实现即可整体覆盖
/// （实现无状态、无 DbContext，Singleton 是合适的生命周期）。</para>
/// <para>无实体、无表、无迁移，故用 <see cref="TnziCustomModule"/> 且不设 <c>TableNamePrefix</c>。</para>
/// </remarks>
public class DocumentsModule : TnziCustomModule
{
    /// <inheritdoc />
    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddTnziOptions<DocumentsOptions, DocumentsOptionsValidator>(context.Configuration);
        context.Services.AddTnziOptions<HtmlPdfOptions, HtmlPdfOptionsValidator>(context.Configuration);
        return base.PreConfigureServicesAsync(context);
    }

    /// <inheritdoc />
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 两个引擎各自注册成具体类型，再由 RoutingDocumentConverter 按扩展名分流：
        // HTML 的判定标准是浏览器长什么样，Office 的判定标准是 LibreOffice 打开长什么样，
        // 而消费方注入的是一个 IDocumentConverter，所以分流只能发生在这一侧。
        // 顺序即优先级：HTML 先被浏览器引擎认领（它关掉时不认领，于是自然落回 LibreOffice）。
        context.Services.TryAddSingleton<LibreOfficeDocumentConverter>();
        context.Services.TryAddSingleton<ChromiumHtmlDocumentConverter>();
        context.Services.TryAddSingleton<IDocumentConverter>(provider => new RoutingDocumentConverter(
            provider.GetRequiredService<ChromiumHtmlDocumentConverter>(),
            provider.GetRequiredService<LibreOfficeDocumentConverter>()));

        context.Services.TryAddSingleton<IPdfInspector, PdfPigPdfInspector>();
        context.Services.TryAddSingleton<IPdfStamper, PdfSharpPdfStamper>();

        return base.ConfigureServicesAsync(context);
    }

    /// <inheritdoc />
    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        // 转换要跑外部进程，装没装是运维事实而不是配置错误：启动期把结论说清楚，
        // 但**不**因此让应用起不来 —— 读 PDF 与盖章不依赖 LibreOffice，照常可用。
        var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<DocumentsModule>();
        var options = context.ServiceProvider.GetRequiredService<IOptions<DocumentsOptions>>().Value;

        var executable = LibreOfficeLocator.Resolve(options.LibreOfficePath);
        if (executable == null)
        {
            logger.LogWarning(
                "Office to PDF conversion is unavailable. {Reason}",
                LibreOfficeLocator.NotFoundMessage(options.LibreOfficePath));
        }
        else
        {
            logger.LogInformation("Office to PDF conversion will use LibreOffice at '{Path}'.", executable);
        }

        // HTML 走本机浏览器（见 ChromiumHtmlDocumentConverter），同样是运维事实而非配置错误。
        var html = context.ServiceProvider.GetRequiredService<IOptions<HtmlPdfOptions>>().Value;
        if (!html.Enabled)
        {
            logger.LogInformation(
                "Browser-based HTML rendering is disabled; HTML will go through LibreOffice, which drops most CSS.");
        }
        else
        {
            var browser = ChromiumLocator.Resolve(html.BrowserPath);
            if (browser == null)
            {
                logger.LogWarning("HTML to PDF conversion is unavailable. {Reason}", ChromiumLocator.NotFoundMessage(html.BrowserPath));
            }
            else
            {
                logger.LogInformation("HTML to PDF conversion will use the browser at '{Path}'.", browser);
            }
        }

        return base.OnApplicationInitializationAsync(context);
    }
}
