using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Tnzi.Storage.Tests.TestSupport;

/// <summary>
/// 站在 <c>Tnzi.Documents</c> 的位置回答 <see cref="IPdfRasterizer"/>：本测试项目**刻意不引用那个包**
/// （那正是「宿主没加载它」的真实现场），PDF 一侧的行为只能靠这个替身来驱动。
/// </summary>
/// <remarks>
/// 渲染出来的是一张真 PNG（ImageSharp 编码，尺寸由用例定）：生成器拿到它之后还要过解码闸门、
/// 等比缩进盒子、再编 JPEG —— 那一段是真实代码，喂它假字节测不到。
/// </remarks>
public sealed class FakePdfRasterizer : IPdfRasterizer
{
    /// <summary>原生库装不装得上。</summary>
    public bool Available { get; set; } = true;

    /// <summary>页数；0 模拟一份没有页的文件。</summary>
    public int PageCount { get; set; } = 1;

    /// <summary>渲染出来的页面宽度（像素）。默认 1700×700：一张横向支票在 200 dpi 下的形状。</summary>
    public int PageWidth { get; set; } = 1700;

    /// <summary>渲染出来的页面高度（像素）。</summary>
    public int PageHeight { get; set; } = 700;

    /// <summary>设了就在渲染时抛：模拟有口令 / 损坏 / 原生库炸掉。</summary>
    public Exception? ThrowOnRender { get; set; }

    /// <summary>按文件内容决定抛不抛：模拟一批里只有某几份有口令。</summary>
    public Func<byte[], Exception?>? ThrowFor { get; set; }

    /// <summary>设了就在读页数时抛。</summary>
    public Exception? ThrowOnPageCount { get; set; }

    /// <summary><see cref="RenderPageAsync"/> 被调用的次数。</summary>
    public int RenderCalls { get; private set; }

    /// <summary><see cref="GetPageCountAsync"/> 被调用的次数。</summary>
    public int PageCountCalls { get; private set; }

    /// <summary>最后一次渲染要的页索引。</summary>
    public int? LastPageIndex { get; private set; }

    /// <summary>最后一次渲染收到的请求。</summary>
    public PdfRasterRequest? LastRequest { get; private set; }

    /// <summary>最后一次渲染收到的字节数。</summary>
    public int? LastSourceLength { get; private set; }

    public bool IsAvailable => Available;

    public Task<int> GetPageCountAsync(byte[] source, CancellationToken ct = default)
    {
        PageCountCalls++;
        if (ThrowOnPageCount is not null)
            throw ThrowOnPageCount;

        return Task.FromResult(PageCount);
    }

    /// <summary>
    /// 设了就在渲染开始时同步阻塞到它被 Set：模拟 PDFium 在进程级锁里跑一份首页极复杂的文件（取消不掉）。
    /// </summary>
    public ManualResetEventSlim? BlockRenderUntil { get; set; }

    /// <summary>渲染开始时调用：模拟渲染进行的那几十秒里别的请求对同一条记录的改动。</summary>
    public Action? OnRender { get; set; }

    public async Task<DocumentImage> RenderPageAsync(byte[] source, int pageIndex, PdfRasterRequest? request = null, CancellationToken ct = default)
    {
        RenderCalls++;
        LastPageIndex = pageIndex;
        LastRequest = request ?? new PdfRasterRequest();
        LastSourceLength = source.Length;

        BlockRenderUntil?.Wait(CancellationToken.None);
        OnRender?.Invoke();

        // 与真实实现同形：渲染自己先读页数，越界页索引抛 ArgumentOutOfRangeException。
        var pageCount = await GetPageCountAsync(source, ct);
        if (pageIndex >= pageCount)
            throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, $"The document has {pageCount} page(s).");

        if (ThrowOnRender is not null)
            throw ThrowOnRender;

        if (ThrowFor?.Invoke(source) is { } perFile)
            throw perFile;

        // 左上角一块深色：缩略图里还认得出来它在左上角 → 证明没有被裁掉、也没有被翻转。
        using var page = new Image<Rgba32>(PageWidth, PageHeight, new Rgba32(255, 255, 255));
        for (var y = 0; y < Math.Min(40, PageHeight); y++)
        {
            for (var x = 0; x < Math.Min(40, PageWidth); x++)
            {
                page[x, y] = new Rgba32(20, 20, 20);
            }
        }

        using var buffer = new MemoryStream();
        await page.SaveAsPngAsync(buffer, ct);
        return new DocumentImage(buffer.ToArray(), "image/png", PageWidth, PageHeight);
    }
}
