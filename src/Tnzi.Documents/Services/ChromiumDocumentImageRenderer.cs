namespace Tnzi.Documents.Services;

/// <summary>
/// 把 HTML 文档的首页渲染成位图：本机 Chromium 系浏览器 headless 截图。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="ChromiumHtmlDocumentConverter"/> 共用 <see cref="ChromiumPageRunner"/> 的骨架
/// （同一套进程管理、同一个并发闸门），<b>只有页面就绪之后那一条命令不同</b>：
/// 那边是 <c>Page.printToPDF</c>，这边是 <c>Page.captureScreenshot</c>。
/// </para>
/// <para>
/// ★ <b>刻意是独立的一趟，而不是让转 PDF 顺手多吐一张图。</b>合并确实能省一次页面加载（约 1 秒），
/// 但代价是把两件事绑死：想重新生成缩略图（换尺寸、补历史数据）就得连 PDF 一起重出，
/// 而那份 PDF 可能已经盖了签名、算过哈希 —— 重出它是绝对不能发生的事。
/// 何况缩略图只在文档创建时生成一次，省下的那一秒买不到这个耦合。
/// </para>
/// <para>
/// ★ <b>几何取自同一份 <c>Documents:Html</c> 配置</b>（纸张、边距、横向），所以缩略图与它代表的那份 PDF
/// 排版宽度一致。出图按**页面内容盒**而不是整张纸：纸张四周那圈空白在卡片上纯属浪费。
/// </para>
/// <para>
/// ★ <b>按 <c>print</c> 媒体渲染</b>：文档的判定标准是「打印出来长什么样」，
/// 屏幕媒体下 <c>@media print</c> 里的规则不会生效，缩略图会跟 PDF 长得不一样。
/// </para>
/// <para>
/// 出的是<b>屏幕渲染</b>的像素，不走打印分页管线，因此与最终 PDF 不保证逐像素一致 ——
/// 它的用途是「在列表里认出这是哪一份」，不是证据。
/// </para>
/// </remarks>
public sealed class ChromiumDocumentImageRenderer : IDocumentImageRenderer
{
    /// <summary>请求宽度的上限：再大就不是缩略图了，而且它直接决定浏览器要栅格化多少像素。</summary>
    private const int MaxWidth = 4096;

    private readonly IOptions<HtmlPdfOptions> _options;
    private readonly ILogger<ChromiumDocumentImageRenderer> _logger;

    /// <summary>初始化一个 <see cref="ChromiumDocumentImageRenderer"/> 实例。</summary>
    /// <param name="options">HTML 渲染配置。</param>
    /// <param name="logger">日志。</param>
    public ChromiumDocumentImageRenderer(IOptions<HtmlPdfOptions> options, ILogger<ChromiumDocumentImageRenderer> logger)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public bool IsAvailable => ChromiumPageRunner.IsAvailable(_options.Value);

    /// <inheritdoc />
    /// <remarks>
    /// 只认 <c>.htm</c> / <c>.html</c>，且 <c>Documents:Html:Enabled = false</c> 时一律 false。
    /// ★ <c>.pdf</c> **不在**其中：实测 headless 浏览器打开 PDF 渲染出来的是**查看器界面**
    /// （工具栏、侧边缩略图面板、深色背景），不是页面本身，靠裁剪去糊弄会随浏览器版本碎掉。
    /// 真要支持 PDF 需要一个光栅化引擎，那是一个新的原生依赖决定。
    /// </remarks>
    public bool CanRender(string fileName) => HtmlSource.IsHtml(_options.Value, fileName);

    /// <inheritdoc />
    public async Task<DocumentImage> RenderFirstPageAsync(
        byte[] source,
        string sourceFileName,
        DocumentImageRequest? request = null,
        CancellationToken ct = default)
    {
        var options = _options.Value;
        HtmlSource.Validate(options, source, sourceFileName);

        var effective = request ?? new DocumentImageRequest();
        ValidateRequest(effective);

        var layout = ImageLayout.Create(options, effective);

        var content = await ChromiumPageRunner.RunAsync(
            options,
            source,
            sourceFileName,
            (session, sessionId, token) => CaptureAsync(session, sessionId, layout, effective, token),
            _logger,
            ct,
            (session, sessionId, token) => PrepareAsync(session, sessionId, layout, token));

        var image = new DocumentImage(content, ContentTypeOf(effective.Format), layout.OutputWidth, layout.OutputHeight);

        _logger.LogDebug(
            "Rendered the first page of '{FileName}' to a {Width}x{Height} image ({Bytes} bytes).",
            sourceFileName, image.Width, image.Height, content.Length);

        return image;
    }

    /// <summary>
    /// 导航之前把视口与媒体类型设好 —— 它们决定排版，加载之后再改要靠一次重排才生效。
    /// </summary>
    private static async Task PrepareAsync(DevToolsSession session, string sessionId, ImageLayout layout, CancellationToken ct)
    {
        await session.SendAsync(
            "Emulation.setDeviceMetricsOverride",
            new
            {
                width = (int)Math.Round(layout.ContentWidthPx),
                height = (int)Math.Round(layout.ContentHeightPx),
                deviceScaleFactor = 1,
                mobile = false
            },
            sessionId,
            ct);

        await session.SendAsync("Emulation.setEmulatedMedia", new { media = "print" }, sessionId, ct);
    }

    private static async Task<byte[]> CaptureAsync(
        DevToolsSession session,
        string sessionId,
        ImageLayout layout,
        DocumentImageRequest request,
        CancellationToken ct)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["format"] = FormatOf(request.Format),
            ["captureBeyondViewport"] = true,
            // clip.scale 直接决定出图像素数（实测：内容盒 739 CSS px、scale = 480/739 → 正好 480 px 宽），
            // 所以这里不需要任何图像库来缩放，浏览器就是按目标分辨率栅格化的。
            ["clip"] = new
            {
                x = 0,
                y = 0,
                width = layout.ContentWidthPx,
                height = layout.ClipHeightPx,
                scale = layout.Scale
            }
        };

        // PNG 没有质量参数，带上会被拒
        if (request.Format != DocumentImageFormat.Png)
            parameters["quality"] = request.Quality;

        var captured = await session.SendAsync("Page.captureScreenshot", parameters, sessionId, ct);

        var data = captured.TryGetProperty("data", out var payload) ? payload.GetString() : null;
        if (string.IsNullOrEmpty(data))
            throw new DocumentConversionException("The browser reported success but returned no image data.");

        return Convert.FromBase64String(data);
    }

    private static void ValidateRequest(DocumentImageRequest request)
    {
        if (request.Width is < 1 or > MaxWidth)
            throw new ArgumentOutOfRangeException(nameof(request), request.Width, $"Image width must be between 1 and {MaxWidth} pixels.");

        if (request.Height is < 1)
            throw new ArgumentOutOfRangeException(nameof(request), request.Height, "Image height must be greater than zero when specified.");

        if (request.Format != DocumentImageFormat.Png && request.Quality is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(request), request.Quality, "Image quality must be between 1 and 100.");
    }

    private static string FormatOf(DocumentImageFormat format) => format switch
    {
        DocumentImageFormat.Jpeg => "jpeg",
        DocumentImageFormat.Webp => "webp",
        _ => "png"
    };

    private static string ContentTypeOf(DocumentImageFormat format) => format switch
    {
        DocumentImageFormat.Jpeg => "image/jpeg",
        DocumentImageFormat.Webp => "image/webp",
        _ => "image/png"
    };

    /// <summary>请求尺寸 + 页面几何算出来的实际出图参数。</summary>
    /// <remarks>纯计算、不碰浏览器，因此可以单独测（<c>ImageLayoutTests</c>）。</remarks>
    internal sealed record ImageLayout(
        double ContentWidthPx,
        double ContentHeightPx,
        double ClipHeightPx,
        double Scale,
        int OutputWidth,
        int OutputHeight)
    {
        public static ImageLayout Create(HtmlPdfOptions options, DocumentImageRequest request)
        {
            var (contentWidthPx, contentHeightPx) = HtmlPageGeometry.ContentBoxCssPixels(options);

            var scale = request.Width / contentWidthPx;

            // 高度给多了就按整页截断：请求的是**首页**，多截出来的是第二页的内容。
            var clipHeightPx = request.Height.HasValue
                ? Math.Min(request.Height.Value / scale, contentHeightPx)
                : contentHeightPx;

            var outputHeight = Math.Max(1, (int)Math.Round(clipHeightPx * scale));

            return new ImageLayout(contentWidthPx, contentHeightPx, clipHeightPx, scale, request.Width, outputHeight);
        }
    }
}
