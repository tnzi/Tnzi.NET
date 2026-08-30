namespace Tnzi.Documents.Services;

/// <summary>
/// HTML 转 PDF：用本机 Chromium 系浏览器（Chrome / Edge / Chromium）的 headless 模式渲染。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么 HTML 不走 LibreOffice。</b>LibreOffice 的 HTML 导入只认很小一部分 CSS。同一份
/// 浏览器里显示正常的表单，实测会丢掉：<c>img</c> 上的 <c>width</c>（图按原始像素画，可以大出三倍）、
/// 块级元素的 <c>text-align: center</c>、<c>text-align: justify</c> 与 <c>text-indent</c>、
/// 行内 <c>span</c> 的 <c>border-bottom</c>（填空题下面那条线整条消失）。而 HTML 的判定标准
/// 恰恰是「浏览器长什么样」—— 只有浏览器自己能给出正确答案。
/// </para>
/// <para>
/// <b>产出的是真文本层，不是图片。</b>浏览器的打印管线嵌入字体子集并写出 <c>ToUnicode</c> 映射，
/// 所以 <see cref="IPdfInspector.FindTags"/> 能照常按字母扫描定位。这一条是硬要求：
/// 一旦哪天换成栅格化或把字形转成轮廓，签署流程里所有字段的坐标会**静默**失效 ——
/// 文档看上去毫无异样，只是再也定位不到任何标签。<c>ChromiumRendersASearchablePdf</c> 就是钉这条的。
/// </para>
/// <para>
/// <b>浏览器不随框架分发</b>，用宿主上已经装好的那个（Windows Server 自带 Edge）。
/// 找不到浏览器时**直接报错，不会自动退回 LibreOffice**：同一份 HTML 在两条路径下出来的 PDF
/// 差别极大，「悄悄换一条能跑通的路」比直接失败危险得多。要旧行为就显式设
/// <c>Documents:Html:Enabled = false</c>。
/// </para>
/// <para>
/// <b>喂进来的 HTML 按「应用自己生成的、可信的内容」对待</b>：它会以 <c>file://</c> 形式加载，
/// 与既有的 LibreOffice 路径同一个信任级别（那边同样是把任意字节交给外部进程解析）。
/// 不要拿它渲染终端用户直接提交的 HTML。
/// </para>
/// <para>
/// 进程启动、会话连接、导航等待、超时归类、临时目录清理都在 <see cref="ChromiumPageRunner"/>，
/// 与出缩略图那条路（<see cref="ChromiumDocumentImageRenderer"/>）共用同一套骨架与同一个并发闸门。
/// </para>
/// </remarks>
public sealed class ChromiumHtmlDocumentConverter : IDocumentConverter
{
    private const double PointsPerInch = 72d;

    private readonly IOptions<HtmlPdfOptions> _options;
    private readonly ILogger<ChromiumHtmlDocumentConverter> _logger;

    /// <summary>初始化一个 <see cref="ChromiumHtmlDocumentConverter"/> 实例。</summary>
    /// <param name="options">HTML 渲染配置。</param>
    /// <param name="logger">日志。</param>
    public ChromiumHtmlDocumentConverter(IOptions<HtmlPdfOptions> options, ILogger<ChromiumHtmlDocumentConverter> logger)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    /// <remarks>即本机找不找得到浏览器。探测结果按配置值缓存，列表页逐行询问只有首次真正碰文件系统。</remarks>
    public bool IsAvailable => ChromiumPageRunner.IsAvailable(_options.Value);

    /// <inheritdoc />
    /// <remarks>
    /// 只认 <c>.htm</c> / <c>.html</c>。<c>Documents:Html:Enabled = false</c> 时一律返回 false ——
    /// 这样 <see cref="RoutingDocumentConverter"/> 会把 HTML 交回给 LibreOffice，即旧行为。
    /// </remarks>
    public bool CanConvert(string fileName) => HtmlSource.IsHtml(_options.Value, fileName);

    /// <inheritdoc />
    public async Task<byte[]> ConvertToPdfAsync(byte[] source, string sourceFileName, CancellationToken ct = default)
    {
        var options = _options.Value;
        HtmlSource.Validate(options, source, sourceFileName);

        var pdf = await ChromiumPageRunner.RunAsync(
            options,
            source,
            sourceFileName,
            (session, sessionId, token) => PrintAsync(session, sessionId, options, token),
            _logger,
            ct);

        _logger.LogDebug("Rendered '{FileName}' to PDF ({Bytes} bytes).", sourceFileName, pdf.Length);
        return pdf;
    }

    private static async Task<byte[]> PrintAsync(DevToolsSession session, string sessionId, HtmlPdfOptions options, CancellationToken ct)
    {
        var printed = await session.SendAsync("Page.printToPDF", BuildPrintParameters(options), sessionId, ct);

        var data = printed.TryGetProperty("data", out var payload) ? payload.GetString() : null;
        if (string.IsNullOrEmpty(data))
            throw new DocumentConversionException("The browser reported success but returned no PDF data.");

        return Convert.FromBase64String(data);
    }

    private static Dictionary<string, object?> BuildPrintParameters(HtmlPdfOptions options)
    {
        var (widthPt, heightPt) = HtmlPageGeometry.ResolvePaperSizePt(options);

        // CDP 的纸张与边距单位是**英寸**，本框架对外一律用点（1pt = 1/72in），换算收口在这里。
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["landscape"] = options.Landscape,
            ["printBackground"] = options.PrintBackground,
            ["scale"] = options.Scale,
            ["paperWidth"] = widthPt / PointsPerInch,
            ["paperHeight"] = heightPt / PointsPerInch,
            ["marginTop"] = options.MarginTopPt / PointsPerInch,
            ["marginRight"] = options.MarginRightPt / PointsPerInch,
            ["marginBottom"] = options.MarginBottomPt / PointsPerInch,
            ["marginLeft"] = options.MarginLeftPt / PointsPerInch,
            ["preferCSSPageSize"] = options.PreferCssPageSize,
            ["transferMode"] = "ReturnAsBase64"
        };
    }
}
