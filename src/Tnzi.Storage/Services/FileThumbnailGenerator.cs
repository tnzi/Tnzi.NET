namespace Tnzi.Storage.Services;

/// <inheritdoc cref="IFileThumbnailGenerator"/>
/// <remarks>
/// <para>
/// 两条出图路径，两种形状，刻意不统一：
/// </para>
/// <list type="bullet">
/// <item><b>位图</b>：<see cref="ImageDecodeGuard"/> 解码 → 中心裁剪成正方形 → 缩到
/// <c>max(ThumbnailSize.Width, Height)</c> → JPEG。与 2026-09 之前 <c>FileStorageService.GenerateThumbnailAsync</c>
/// 逐字相同 —— 头像、相册这类消费方靠的就是「缩略图是方的」。</item>
/// <item><b>PDF</b>：可选注入的核心契约 <see cref="IPdfRasterizer"/> 渲染第 0 页（彩色、<c>Storage:PdfThumbnail:Dpi</c>）
/// → 仍经 <see cref="ImageDecodeGuard"/> 解码（渲染器已按 <c>Documents:Raster:MaxPagePixels</c> 拦过，但本模块的
/// <c>Imaging:MaxDecodePixels</c> 是自己这一侧的闸门，不该因为对面有一道就拆掉）→ 整页等比缩进
/// <c>MaxWidth × MaxHeight</c> 的盒子 → JPEG。整页而不是方块：看一张支票缩略图的人要找的是下边缘的账号行。</item>
/// </list>
/// <para>
/// ★ <b>PDF 路径的三道前置判定缺一不可</b>：包加载了（渲染器非 null）、原生库此刻装得上（<c>IsAvailable</c>）、
/// 开关开着。少问 <c>IsAvailable</c> 的后果与 <c>FilePreviewService</c> 那次一样 —— 发布时没带
/// <c>runtimes/&lt;rid&gt;/native</c> 的宿主上，每份 PDF 上传都会在渲染那一步抛一次，日志里多一条谁也处理不了的记录。
/// 三条都成立才尝试；不成立时不尝试、不记日志，与没有这个能力时逐字相同。
/// </para>
/// <para>
/// ★ <b>失败记 Warning 不记 Error。</b>有口令的 PDF、截断的上传、超过解码闸门的位图，都是那份文件自身的性质，
/// 没有人能对着这条日志做什么；原件已经存好、照常可下载，只是没有图。
/// </para>
/// </remarks>
public sealed class FileThumbnailGenerator : IFileThumbnailGenerator
{
    private const string PdfExtension = ".pdf";

    private readonly IFileStorage _storage;
    private readonly IOptionsMonitor<StorageOptions> _optionsMonitor;
    private readonly ILogger<FileThumbnailGenerator>? _logger;
    private readonly IPdfRasterizer? _pdfRasterizer;

    /// <param name="storage">存储提供者：原件从它读、缩略图写回它。</param>
    /// <param name="optionsMonitor">存储配置（热读：尺寸 / 质量 / PDF 开关随配置中心改）。</param>
    /// <param name="logger">日志；画不出来时记一条 Warning。</param>
    /// <param name="pdfRasterizer">
    /// PDF 光栅化器，来自可选包 <c>Tnzi.Documents</c>；没加载时为 null，此时 PDF 与此前一样没有缩略图。
    /// </param>
    public FileThumbnailGenerator(
        IFileStorage storage,
        IOptionsMonitor<StorageOptions> optionsMonitor,
        ILogger<FileThumbnailGenerator>? logger = null,
        IPdfRasterizer? pdfRasterizer = null)
    {
        _storage = Check.NotNull(storage);
        _optionsMonitor = Check.NotNull(optionsMonitor);
        _logger = logger;
        _pdfRasterizer = pdfRasterizer;
    }

    private StorageOptions Options => _optionsMonitor.CurrentValue;

    /// <summary>
    /// PDF 此刻画不画得出来：包加载了、原生库装得上、开关开着。
    /// </summary>
    private bool PdfEnabled => Options.PdfThumbnail.Enabled && _pdfRasterizer?.IsAvailable == true;

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedExtensions
        => PdfEnabled
            ? [.. FileTypeHelper.ThumbnailableImageExtensions, PdfExtension]
            : FileTypeHelper.ThumbnailableImageExtensions;

    /// <inheritdoc />
    public bool CanGenerate(string? extension)
    {
        if (string.IsNullOrEmpty(extension))
            return false;

        return FileTypeHelper.IsThumbnailable(extension)
               || (FileTypeHelper.IsPdf(extension) && PdfEnabled);
    }

    /// <inheritdoc />
    public async Task<string?> GenerateAsync(string originalPath, string originalKey, string? extension, long size, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(originalPath);
        Check.NotNullOrWhiteSpace(originalKey);

        if (!CanGenerate(extension))
            return null;

        try
        {
            using var thumbnail = FileTypeHelper.IsPdf(extension!)
                ? await RenderPdfFirstPageAsync(originalPath, size, cancellationToken)
                : await RenderBitmapAsync(originalPath, cancellationToken);

            if (thumbnail is null)
                return null;

            using var thumbnailStream = new MemoryStream();
            await thumbnail.SaveAsJpegAsync(thumbnailStream, new JpegEncoder { Quality = Options.ImageCompressionQuality }, cancellationToken);
            thumbnailStream.Position = 0;

            var thumbnailKey = StorageKeyHelper.ThumbnailKey(originalKey);
            return await _storage.UploadAsync(thumbnailKey, thumbnailStream, "image/jpeg");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Could not generate a thumbnail for {OriginalPath} ({Extension}); the file is stored without one.",
                originalPath, extension);
            return null;
        }
    }

    /// <summary>
    /// 位图：解码 → 正方形。与提取前的 <c>FileStorageService.GenerateThumbnailAsync</c> 逐字相同。
    /// </summary>
    private async Task<Image<Rgba32>?> RenderBitmapAsync(string originalPath, CancellationToken cancellationToken)
    {
        using var originalStream = await _storage.DownloadAsync(originalPath);

        // 经解码闸门：先读文件头判尺寸再解码。压缩字节数与解码后的内存没有关系 ——
        // 一个 200KB 的 PNG 可以声明 50000×50000，直接 Load 就是一次 OOM，
        // 而上传大小限制对它毫无作用。上限见 Imaging:MaxDecodePixels。
        using var image = await ImageDecodeGuard.LoadAsync(originalStream, cancellationToken: cancellationToken);
        var thumbnailSize = Options.ThumbnailSize;
        return image.GenerateSquareThumbnail(Math.Max(thumbnailSize.Width, thumbnailSize.Height));
    }

    /// <summary>
    /// PDF：渲染第 0 页 → 整页等比缩进盒子。
    /// </summary>
    /// <remarks>
    /// 渲染器只收字节数组，故整份文件要读进内存；<c>MaxSourceBytes</c> 在读之前按记录的 <paramref name="size"/> 拦一次，
    /// 读的时候再按实际字节数拦一次（记录里的 Size 可能是 0，见 <c>ResolveStoredSizeAsync</c>）。
    /// </remarks>
    private async Task<Image<Rgba32>?> RenderPdfFirstPageAsync(string originalPath, long size, CancellationToken cancellationToken)
    {
        var pdf = Options.PdfThumbnail;
        var limit = pdf.MaxSourceBytes > 0 ? pdf.MaxSourceBytes : long.MaxValue;

        if (size > limit)
        {
            _logger?.LogInformation("Not drawing a thumbnail for {OriginalPath}: {Size} bytes is over Storage:PdfThumbnail:MaxSourceBytes ({Limit}).",
                originalPath, size, limit);
            return null;
        }

        byte[] source;
        using (var originalStream = await _storage.DownloadAsync(originalPath))
        using (var buffer = new MemoryStream())
        {
            var copied = await StreamLimitHelper.CopyBoundedAsync(originalStream, buffer, limit, cancellationToken);
            if (copied < 0)
            {
                _logger?.LogInformation("Not drawing a thumbnail for {OriginalPath}: the object is over Storage:PdfThumbnail:MaxSourceBytes ({Limit}).",
                    originalPath, limit);
                return null;
            }

            source = buffer.ToArray();
        }

        if (source.Length == 0)
        {
            _logger?.LogWarning("Could not generate a thumbnail for {OriginalPath}: the stored object is empty.", originalPath);
            return null;
        }

        // 渲染器对越界页索引抛 ArgumentOutOfRangeException（调用方错误）；零页的文件在这里先拦下来，
        // 让「没有页」与「有口令」「损坏」一样只是一条 Warning。
        var pageCount = await _pdfRasterizer!.GetPageCountAsync(source, cancellationToken);
        if (pageCount <= 0)
        {
            _logger?.LogWarning("Could not generate a thumbnail for {OriginalPath}: the PDF has no pages.", originalPath);
            return null;
        }

        var page = await _pdfRasterizer.RenderPageAsync(
            source,
            pageIndex: 0,
            new PdfRasterRequest
            {
                Dpi = pdf.Dpi,
                // 彩色。渲染器的默认值服务机读（先把颜色扔掉），这张图是给人认的：
                // 彩色的信头与印章是他们分辨「这是哪一份」的一半依据。
                Grayscale = false,
            },
            cancellationToken);

        using var image = await ImageDecodeGuard.LoadAsync(page.Content, cancellationToken: cancellationToken);
        return image.ResizeToFit(pdf.MaxWidth, pdf.MaxHeight);
    }
}
