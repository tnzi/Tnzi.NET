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
/// </remarks>
public sealed class ChromiumHtmlDocumentConverter : IDocumentConverter
{
    /// <summary>渲染用的临时工作目录根（逐次创建、用完即删，含浏览器 profile）。</summary>
    private const string WorkRootName = "tnzi-html-pdf";

    private const string WorkFileBaseName = "source";
    private const string ProfileDirectoryName = "profile";
    private const double PointsPerInch = 72d;

    private readonly IOptions<HtmlPdfOptions> _options;
    private readonly ILogger<ChromiumHtmlDocumentConverter> _logger;
    private readonly SemaphoreSlim _gate;

    /// <summary>初始化一个 <see cref="ChromiumHtmlDocumentConverter"/> 实例。</summary>
    /// <param name="options">HTML 渲染配置。</param>
    /// <param name="logger">日志。</param>
    public ChromiumHtmlDocumentConverter(IOptions<HtmlPdfOptions> options, ILogger<ChromiumHtmlDocumentConverter> logger)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);

        // 并发上限在构造时定死：信号量的容量本来就不能中途改，改这个配置要重启进程（已写进配置注释）。
        var permits = Math.Clamp(options.Value.MaxConcurrency, 1, 16);
        _gate = new SemaphoreSlim(permits, permits);
    }

    /// <inheritdoc />
    /// <remarks>即本机找不找得到浏览器。探测结果按配置值缓存，列表页逐行询问只有首次真正碰文件系统。</remarks>
    public bool IsAvailable => ChromiumLocator.Resolve(_options.Value.BrowserPath) != null;

    /// <inheritdoc />
    /// <remarks>
    /// 只认 <c>.htm</c> / <c>.html</c>。<c>Documents:Html:Enabled = false</c> 时一律返回 false ——
    /// 这样 <see cref="RoutingDocumentConverter"/> 会把 HTML 交回给 LibreOffice，即旧行为。
    /// </remarks>
    public bool CanConvert(string fileName)
    {
        if (!_options.Value.Enabled || string.IsNullOrWhiteSpace(fileName))
            return false;

        var extension = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(extension) && DocumentFormats.HtmlExtensions.Contains(extension);
    }

    /// <inheritdoc />
    public async Task<byte[]> ConvertToPdfAsync(byte[] source, string sourceFileName, CancellationToken ct = default)
    {
        Check.NotNull(source);
        Check.NotNullOrWhiteSpace(sourceFileName);

        if (source.Length == 0)
            throw new DocumentConversionException($"Source document '{sourceFileName}' is empty.");

        var options = _options.Value;

        if (!options.Enabled)
        {
            throw new DocumentConversionException(
                "Browser-based HTML rendering is disabled ('Documents:Html:Enabled' is false).");
        }

        if (!CanConvert(sourceFileName))
        {
            throw new DocumentConversionException(
                $"'{Path.GetExtension(sourceFileName)}' is not an HTML document. " +
                $"Supported: {string.Join(", ", DocumentFormats.HtmlExtensions.Order(StringComparer.Ordinal))}.");
        }

        var executable = ChromiumLocator.Resolve(options.BrowserPath)
            ?? throw new DocumentConversionException(ChromiumLocator.NotFoundMessage(options.BrowserPath));

        var workDirectory = Path.Combine(Path.GetTempPath(), WorkRootName, Guid.NewGuid().ToString("N"));
        var profileDirectory = Path.Combine(workDirectory, ProfileDirectoryName);
        var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

        try
        {
            Directory.CreateDirectory(profileDirectory);

            // 扩展名已过白名单，且文件名不参与命令行：浏览器只拿到我们自己拼的工作路径。
            var inputPath = Path.Combine(workDirectory, WorkFileBaseName + Path.GetExtension(sourceFileName));
            await File.WriteAllBytesAsync(inputPath, source, ct);

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutSource.CancelAfter(timeout);

            await _gate.WaitAsync(ct);
            try
            {
                var pdf = await RenderAsync(executable, profileDirectory, inputPath, options, timeout, ct, timeoutSource.Token);
                _logger.LogDebug("Rendered '{FileName}' to PDF ({Bytes} bytes) with '{Browser}'.", sourceFileName, pdf.Length, executable);
                return pdf;
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            await TryDeleteDirectoryAsync(workDirectory);
        }
    }

    private static async Task<byte[]> RenderAsync(
        string executable,
        string profileDirectory,
        string inputPath,
        HtmlPdfOptions options,
        TimeSpan timeout,
        CancellationToken ct,
        CancellationToken deadline)
    {
        using var browser = await Run(() => ChromiumProcess.StartAsync(executable, profileDirectory, options, timeout, deadline), timeout, ct);

        await using var session = await Run(() => DevToolsSession.ConnectAsync(browser.Endpoint, deadline), timeout, ct);

        return await Run(() => PrintAsync(session, inputPath, options, deadline), timeout, ct, browser);
    }

    private static async Task<byte[]> PrintAsync(DevToolsSession session, string inputPath, HtmlPdfOptions options, CancellationToken ct)
    {
        var target = await session.SendAsync("Target.createTarget", new { url = "about:blank" }, ct: ct);
        var targetId = target.GetProperty("targetId").GetString();

        var attached = await session.SendAsync("Target.attachToTarget", new { targetId, flatten = true }, ct: ct);
        var sessionId = attached.GetProperty("sessionId").GetString();

        await session.SendAsync("Page.enable", sessionId: sessionId, ct: ct);

        // ★ 先登记事件再导航：页面可能快到 navigate 的响应还没回来 load 就已经发出了。
        var loaded = session.WhenEventAsync("Page.loadEventFired");

        var navigation = await session.SendAsync(
            "Page.navigate", new { url = new Uri(inputPath).AbsoluteUri }, sessionId, ct);

        if (navigation.TryGetProperty("errorText", out var errorText) && errorText.GetString() is { Length: > 0 } reason)
            throw new DocumentConversionException($"The browser failed to load the document: {reason}");

        await loaded.WaitAsync(ct);

        // 字体没就位就打印会让文本按回退字形排版（行宽随之改变）。拿不到结果不是致命错误：
        // 老浏览器可能没有 document.fonts，此时按「已就绪」继续。
        try
        {
            await session.SendAsync(
                "Runtime.evaluate",
                new { expression = "document.fonts ? document.fonts.ready.then(() => true) : true", awaitPromise = true },
                sessionId,
                ct);
        }
        catch (DocumentConversionException)
        {
            // 忽略：字体就绪只是排版质量的优化，不值得让整次转换失败
        }

        var printed = await session.SendAsync("Page.printToPDF", BuildPrintParameters(options), sessionId, ct);

        var data = printed.TryGetProperty("data", out var payload) ? payload.GetString() : null;
        if (string.IsNullOrEmpty(data))
            throw new DocumentConversionException("The browser reported success but returned no PDF data.");

        return Convert.FromBase64String(data);
    }

    private static Dictionary<string, object?> BuildPrintParameters(HtmlPdfOptions options)
    {
        var (widthPt, heightPt) = ResolvePaperSize(options);

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

    /// <summary>解析纸张尺寸（点）：显式宽高 &gt; 纸张名 &gt; US Letter。</summary>
    internal static (double WidthPt, double HeightPt) ResolvePaperSize(HtmlPdfOptions options)
    {
        if (options.PaperWidthPt > 0 && options.PaperHeightPt > 0)
            return (options.PaperWidthPt, options.PaperHeightPt);

        // 名字非法在启动期就被验证器拦下了；这里的回退只为「验证器被绕过」留一条确定的路。
        return PaperSizes.TryGet(options.PaperSize, out var named)
            ? named
            : (PaperSizes.LetterWidthPt, PaperSizes.LetterHeightPt);
    }

    /// <summary>
    /// 把「超时」与「调用方取消」区分开：前者要给出可操作的提示，后者原样抛出。
    /// </summary>
    /// <remarks>
    /// 超时后浏览器进程树由 <paramref name="browser"/> 的 <c>Dispose</c> 收拾（<c>using</c> 已经安排好），
    /// 但抛出前先把它的诊断输出捞进消息里 —— 崩溃原因只在它的 stderr 上。
    /// </remarks>
    private static async Task<T> Run<T>(Func<Task<T>> action, TimeSpan timeout, CancellationToken ct, ChromiumProcess? browser = null)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            var diagnostics = browser?.Diagnostics;
            var detail = string.IsNullOrEmpty(diagnostics) ? string.Empty : $" Browser output: {diagnostics}";

            throw new DocumentConversionException(
                $"HTML rendering timed out after {timeout.TotalSeconds:0} seconds. " +
                $"Raise 'Documents:Html:TimeoutSeconds' if large documents or slow remote resources are expected.{detail}",
                isRetryable: true);
        }
    }

    private async Task TryDeleteDirectoryAsync(string directory)
    {
        // 浏览器刚被杀掉时 profile 里的文件可能还锁着几十毫秒，重试几次再放弃。
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!Directory.Exists(directory))
                    return;

                Directory.Delete(directory, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == 2)
                {
                    // 清理失败不影响渲染结果，但要留痕（临时目录堆积是可观测的运维问题）
                    _logger.LogWarning(ex, "Failed to clean up the HTML rendering work directory '{Directory}'.", directory);
                    return;
                }

                // 取消令牌刻意不传：清理跑在 finally 里，取消之后更要把临时目录收拾干净。
                await Task.Delay(200);
            }
        }
    }
}
