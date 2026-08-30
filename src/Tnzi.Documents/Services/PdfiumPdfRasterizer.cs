// PDFtoImage 的类型只在文件级导入：它的 Conversion / RenderOptions 都是很通用的名字，
// 而本包这一侧已经有 PdfRasterRequest / DocumentImage / RenderOptions 意义相近的类型
// 在同一个概念空间里。第三方 PDF 命名空间一律文件级 using，与 PdfSharp / PdfPig 同规矩。
using PDFtoImage;
using PdfEngineException = PDFtoImage.Exceptions.PdfException;

namespace Tnzi.Documents.Services;

/// <inheritdoc cref="IPdfRasterizer"/>
/// <remarks>
/// <para>
/// 基于 PDFium（Chrome 的 PDF 引擎，BSD-3-Clause），经 <c>PDFtoImage</c>（MIT）调用。
/// <b>渲染整页</b>，而不是把内嵌图像抠出来 —— 后者在入站传真上会逐个碰壁：
/// CCITT G4 编码、按横条切成多张图、上面还叠了一层 OCR 文本。
/// </para>
/// <para>
/// ★ <b>刻意不再加一把自己的锁。</b>PDFium 不是可重入的，而 <c>PDFtoImage</c> 内部已经
/// 用一个静态锁把调用串起来了。再包一层只会让下一个读代码的人分不清哪一把才是有效的。
/// 但要知道后果：<b>光栅化在整个进程里是串行的</b>，加线程不会提高吞吐。
/// </para>
/// <para>
/// ★ <b>产物尺寸可能比「页宽 ÷ 72 × dpi」少一个像素。</b>实测 US Letter：
/// 150 dpi 出 1275×1649（精确值 1650）、200 dpi 出 1699×2200（精确值 1700）、
/// 600 dpi 出 5100×6599 —— PDFium 报的页尺寸是精确的 612×792 点，差在它自己的换算上。
/// 这不是本实现的舍入错误，别去「修」它；<see cref="DocumentImage.Width"/> 报的
/// 一律是产物真实的像素数。
/// </para>
/// <para>
/// ★ <b>页面尺寸先算再渲染。</b>页宽高来自文件、dpi 来自调用方，两者相乘没有上界：
/// 一份声明自己 200 英寸见方的 PDF 配 600 dpi 会让 PDFium 去申请上百 GB 位图。
/// 先按 <see cref="PdfRasterOptions.MaxPagePixels"/> 拦下来，才不会变成「一份文件放倒一个进程」。
/// </para>
/// </remarks>
public class PdfiumPdfRasterizer : IPdfRasterizer
{
    private readonly PdfRasterOptions _options;
    private readonly ILogger<PdfiumPdfRasterizer> _logger;

    /// <summary>初始化一个 <see cref="PdfiumPdfRasterizer"/> 实例。</summary>
    /// <param name="options">光栅化配置。</param>
    /// <param name="logger">日志。</param>
    public PdfiumPdfRasterizer(IOptions<PdfRasterOptions> options, ILogger<PdfiumPdfRasterizer> logger)
    {
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 真去加载一次原生库来回答，而不是看包引没引 —— 包在、原生库不在（RID 不匹配、
    /// 精简镜像少了 C 运行时）才是这个能力实际的失效方式，而那时程序集引用看起来完全正常。
    /// 结果缓存在静态字段里：探测本身要起 PDFium。
    /// </remarks>
    public bool IsAvailable => NativeProbe.Succeeded(_logger);

    /// <inheritdoc />
    public Task<int> GetPageCountAsync(byte[] source, CancellationToken ct = default)
    {
        Check.NotNullOrEmpty(source);
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(Guard(() => Conversion.GetPageCount(source), "read the page count of"));
    }

    /// <inheritdoc />
    public async Task<DocumentImage> RenderPageAsync(
        byte[] source,
        int pageIndex,
        PdfRasterRequest? request = null,
        CancellationToken ct = default)
    {
        Check.NotNullOrEmpty(source);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);

        request ??= new PdfRasterRequest();
        if (request.Dpi is < 1 or > 1200)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Dpi, "Dpi must be between 1 and 1200.");
        }

        ct.ThrowIfCancellationRequested();

        var pageCount = await GetPageCountAsync(source, ct);
        if (pageIndex >= pageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex), pageIndex, $"The document has {pageCount} page(s).");
        }

        GuardPageBudget(source, pageIndex, request.Dpi);

        ct.ThrowIfCancellationRequested();

        var options = new RenderOptions(
            Dpi: request.Dpi,
            WithAnnotations: request.WithAnnotations,
            Grayscale: request.Grayscale);

        using var output = new MemoryStream();
        Guard(
            () =>
            {
                Conversion.SavePng(output, source, page: pageIndex, password: null, options: options);
                return true;
            },
            $"render page {pageIndex} of");

        var content = output.ToArray();
        var (width, height) = ReadPngSize(content);

        return new DocumentImage(content, "image/png", width, height);
    }

    /// <summary>
    /// 按页面物理尺寸 × dpi 估算产物像素数，超预算就拒绝。
    /// </summary>
    /// <remarks>
    /// PDFium 报的页尺寸单位是点（1/72 英寸），故 <c>像素 = 点 / 72 × dpi</c>。
    /// 这是在渲染<b>之前</b>算的 —— 等它开始申请内存就已经晚了。
    /// </remarks>
    private void GuardPageBudget(byte[] source, int pageIndex, int dpi)
    {
        if (_options.MaxPagePixels <= 0)
        {
            return;
        }

        var size = Guard(() => Conversion.GetPageSize(source, pageIndex), $"read the size of page {pageIndex} of");

        var width = (long)Math.Ceiling(size.Width / 72.0 * dpi);
        var height = (long)Math.Ceiling(size.Height / 72.0 * dpi);
        var pixels = width * height;

        if (pixels > _options.MaxPagePixels)
        {
            throw new PdfRasterizationException(
                $"Rendering page {pageIndex} at {dpi} dpi would produce {width}x{height} = {pixels} pixels, "
                + $"over the configured limit of {_options.MaxPagePixels} (Documents:Raster:MaxPagePixels). "
                + "Lower the dpi or raise the limit.");
        }
    }

    /// <summary>
    /// 把 PDFium 一侧的失败翻译成本框架的异常。
    /// </summary>
    /// <remarks>
    /// ★ <see cref="DllNotFoundException"/> 单独接住并说清楚：原生库缺失的默认症状是一条
    /// 「找不到 pdfium」的裸异常，读到它的人多半不知道那是发布产物里少了
    /// <c>runtimes/&lt;rid&gt;/native</c>，而不是代码写错了。
    /// </remarks>
    private static T Guard<T>(Func<T> action, string what)
    {
        try
        {
            return action();
        }
        catch (PdfEngineException ex)
        {
            throw new PdfRasterizationException($"Failed to {what} the PDF: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or EntryPointNotFoundException)
        {
            throw new PdfRasterizationException(
                $"Failed to {what} the PDF because the PDFium native library could not be loaded. "
                + "Publish with a runtime identifier, or make sure runtimes/<rid>/native travels with the application.",
                ex);
        }
    }

    /// <summary>
    /// 从 PNG 的 IHDR 里读出宽高。
    /// </summary>
    /// <remarks>
    /// 刻意不为了拿两个整数把产物再解码一遍：IHDR 恒在字节 16-23，大端。
    /// 与 <c>Tnzi.Documents</c> 的 <c>ImagePayload</c> 是同一条思路。
    /// </remarks>
    private static (int Width, int Height) ReadPngSize(byte[] png)
    {
        const int ihdrWidthOffset = 16;

        if (png.Length < ihdrWidthOffset + 8)
        {
            throw new PdfRasterizationException("PDFium returned a PNG too short to carry an IHDR chunk.");
        }

        var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(ihdrWidthOffset, 4));
        var height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(ihdrWidthOffset + 4, 4));
        return (width, height);
    }
}
