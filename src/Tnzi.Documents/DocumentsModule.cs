namespace Tnzi.Documents;

/// <summary>
/// 文档原语模块：文档转 PDF、出缩略图、PDF 读取与定位、PDF 盖章压平、多份 PDF 合并、PDF 页面光栅化。
/// </summary>
/// <remarks>
/// <para>可选加载。这些原语与业务无关（电子签署、合同归档、报表出图、传真都能用），重量级 PDF 依赖
/// （PDFsharp / PdfPig / PDFium）收在本包内，核心与其它消费者不被传递拉入 —— 与
/// <c>Tnzi.Finance.Documents</c> 把渲染依赖收进可选子模块是同一条路子，区别在于
/// 那个包是财务专用的票据渲染，本包是通用 PDF 原语。</para>
/// <para>
/// ★ <b>本包带原生二进制</b>（PDFium + SkiaSharp，经 <c>PDFtoImage</c>），这是六个原语里
/// <see cref="IPdfRasterizer"/> 一家的代价，但它落在<b>每一个</b>引用本包的消费者身上 ——
/// <c>Tnzi.Signing</c> 一行光栅化都不调，发布产物一样会带上。指定 RID 发布约 19 MB，
/// 不指定 RID 会把二十来个 RID 的原生库全带上。随包发的
/// <c>buildTransitive/Tnzi.Documents.targets</c> 默认剔掉 89 MB 的 <c>libSkiaSharp.pdb</c>；
/// 要留着就设 <c>TnziKeepNativeSymbols=true</c>。<b>生产发布请指定 RID。</b>
/// </para>
/// <para>每个实现都经 <c>TryAddSingleton</c> 注册，消费应用先注册自己的实现即可整体覆盖
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
        context.Services.AddTnziOptions<PdfRasterOptions, PdfRasterOptionsValidator>(context.Configuration);
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

        // 源文档 → 位图（列表页认脸）。只有浏览器一条实现，故不需要分流器。
        context.Services.TryAddSingleton<IDocumentImageRenderer, ChromiumDocumentImageRenderer>();

        context.Services.TryAddSingleton<IPdfInspector, PdfPigPdfInspector>();
        context.Services.TryAddSingleton<IPdfStamper, PdfSharpPdfStamper>();
        context.Services.TryAddSingleton<IPdfCombiner, PdfSharpPdfCombiner>();

        // 「PDF → 像素」。与上面那个 IDocumentImageRenderer 是两件事：那个把**源文档**
        // （HTML / Office）的首页出成缩略图给人看，明确不支持 .pdf（headless 浏览器打开 PDF
        // 渲染的是查看器界面）；这个按页索引、按 dpi 渲染**已有 PDF**，服务的是机读
        // （找码、OCR、逐页比对），扫描件与传真件也必须成立。
        context.Services.TryAddSingleton<IPdfRasterizer, PdfiumPdfRasterizer>();

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

        // 光栅化的失效方式与上面两个不同：不是「外部程序装没装」，而是「原生库随不随发布产物到达」。
        // 三种情况（没带 runtimes/<rid>/native、RID 不匹配、精简镜像缺 C 运行时）下程序集引用
        // 看起来完全正常，直到第一次渲染才炸，所以这里真去加载一次 PDFium 来回答。
        // 同样**不**阻断启动 —— 只在某条支线上用光栅化的应用不该因此开不了机。
        // ★ 代价：这一次探测会真的加载 PDFium，实测约 150 ms，一个进程只付一次（结果静态缓存）。
        // 合并 Tnzi.Documents.Raster 之后，这笔钱由**每一个**加载本模块的应用付，
        // 包括只用盖章与定位、永不光栅化的签署应用。仍然保留它，是因为替代方案
        // （不探测）等于把「发布产物少带了原生库」这件事推迟到第一次渲染才暴露。
        var rasterizer = context.ServiceProvider.GetRequiredService<IPdfRasterizer>();
        if (rasterizer.IsAvailable)
        {
            logger.LogInformation("PDF rasterisation is available.");
        }
        else
        {
            logger.LogWarning(
                "PDF rasterisation is unavailable on this host; IPdfRasterizer will throw when called. "
                + "See the preceding warning for the reason.");
        }

        return base.OnApplicationInitializationAsync(context);
    }
}
