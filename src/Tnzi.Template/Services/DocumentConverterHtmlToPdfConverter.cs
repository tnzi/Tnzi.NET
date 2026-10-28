namespace Tnzi.Template.Services;

/// <summary>
/// 把核心的 <see cref="IDocumentConverter"/> 接成 <see cref="IHtmlToPdfConverter"/>：宿主装了能把 HTML 转成 PDF 的实现
/// （<c>Tnzi.Documents</c> 用本机 Chromium）时，模板渲染出的 HTML 经它出真正的 PDF。
/// </summary>
/// <remarks>
/// <para>
/// ★ 此前模板模块只注册 <see cref="HtmlPassThroughConverter"/>，没有任何东西替换它：同时加载了模板与文档两个模块的宿主，
/// 发票、收据、贷项通知单一律以 <c>text/html</c> 出、扩展名 <c>.html</c>，而文档模块明明带着一个能用的 HTML → PDF 转换器。
/// 两个模块互不引用（都只依赖核心），所以接线落在这一侧：契约在核心，模板模块按核心契约取用，不认识任何具体实现。
/// </para>
/// <para>
/// 只在转换器<b>启动时</b>就答得上「HTML 可转」时选用（见 <c>TemplateModule</c> 的注册）；之后转换失败照常抛出，
/// <b>不悄悄退回 HTML</b>：那会让一份标着 PDF 的票据成为 HTML，正是这里要消除的那件事。
/// 纸张与边距由文档模块自己的配置（<c>Documents:Html</c>）决定，<see cref="PdfConvertOptions"/> 不传给它。
/// </para>
/// </remarks>
public sealed class DocumentConverterHtmlToPdfConverter : IHtmlToPdfConverter
{
    /// <summary>交给文档转换器的源文件名：只用来让它按扩展名选 HTML 那条转换路径。</summary>
    public const string SourceFileName = "document.html";

    private readonly IDocumentConverter _documentConverter;

    public DocumentConverterHtmlToPdfConverter(IDocumentConverter documentConverter)
    {
        _documentConverter = Check.NotNull(documentConverter);
    }

    /// <inheritdoc />
    public string ContentType => "application/pdf";

    /// <inheritdoc />
    public string FileExtension => ".pdf";

    /// <inheritdoc />
    public Task<byte[]> ConvertAsync(string htmlContent, PdfConvertOptions? options = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(htmlContent);
        return _documentConverter.ConvertToPdfAsync(Encoding.UTF8.GetBytes(htmlContent), SourceFileName, cancellationToken);
    }

    /// <summary>
    /// 这台宿主的 HTML 转换器：有能把 HTML 转成 PDF 的 <see cref="IDocumentConverter"/> 就用它，否则 HTML 直出。
    /// </summary>
    public static IHtmlToPdfConverter Select(IDocumentConverter? documentConverter)
        => documentConverter != null && documentConverter.IsAvailableFor(SourceFileName)
            ? new DocumentConverterHtmlToPdfConverter(documentConverter)
            : new HtmlPassThroughConverter();
}
