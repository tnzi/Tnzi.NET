namespace Tnzi.Documents.Tests;

/// <summary>
/// <see cref="PdfiumPdfRasterizer"/> 的行为。
/// </summary>
/// <remarks>
/// 这些测试<b>真的加载 PDFium 原生库</b>。跑不起来本身就是有价值的信号：
/// 它说明这台机器（或这个 CI 镜像）拿不到原生资产，而那正是本能力唯一的部署失败方式。
/// </remarks>
public class PdfiumPdfRasterizerTests
{
    private static PdfiumPdfRasterizer Create(long maxPagePixels = PdfRasterOptions.DefaultMaxPagePixels)
        => new(
            Microsoft.Extensions.Options.Options.Create(new PdfRasterOptions { MaxPagePixels = maxPagePixels }),
            NullLogger<PdfiumPdfRasterizer>.Instance);

    /// <summary>PNG 的 IHDR 恒在字节 16-23（大端）。</summary>
    private static (int Width, int Height) PngSize(byte[] png) =>
        (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
         System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));

    [Fact]
    public void IsAvailable_IsTrueWhereTheNativeLibraryLoads()
        => Create().IsAvailable.ShouldBeTrue("原生库加载不了的话下面每一条都会失败，先把原因说清楚");

    [Fact]
    public async Task GetPageCountAsync_ReportsThePageCount()
    {
        var pdf = TestPdfs.Blank(3);

        (await Create().GetPageCountAsync(pdf)).ShouldBe(3);
    }

    // ★ 容差恰好一个像素，是实测出来的而不是拍脑袋：PDFium 报的页尺寸是精确的
    // 612×792 点，但它换算像素时会在某些 dpi 上少一个像素（150 dpi 高 1649 而非 1650、
    // 200 dpi 宽 1699 而非 1700、600 dpi 高 6599 而非 6600）。
    // 写死精确值会让这条测试变成一条「盯着第三方浮点行为」的测试；
    // 放宽到「随便多少都行」又等于不测。一个像素是这两者之间唯一诚实的位置。
    [Theory]
    [InlineData(72, 612, 792)]
    [InlineData(150, 1275, 1650)]
    [InlineData(200, 1700, 2200)]
    public async Task RenderPageAsync_RendersAtTheRequestedResolution(int dpi, int exactWidth, int exactHeight)
    {
        // 分辨率是这个原语唯一真正的旋钮，而它错了的症状是「图出来了但下游算法看到的采样率不对」。
        var pdf = TestPdfs.Blank(1);

        var image = await Create().RenderPageAsync(pdf, 0, new PdfRasterRequest { Dpi = dpi });

        image.Width.ShouldBeInRange(exactWidth - 1, exactWidth);
        image.Height.ShouldBeInRange(exactHeight - 1, exactHeight);
        image.ContentType.ShouldBe("image/png");

        // 这一条没有容差：报出来的尺寸必须就是产物真实的像素数，
        // 否则调用方拿它做的每一次布局与坐标换算都是错的。
        PngSize(image.Content).ShouldBe((image.Width, image.Height));
    }

    [Fact]
    public async Task RenderPageAsync_RendersTheRequestedPageAndNotAlwaysTheFirst()
    {
        // 页索引接错的经典症状是「每一页都渲染成第一页」，而那不会抛任何异常。
        var pdf = TestPdfs.Blank(3);
        var rasterizer = Create();

        var first = await rasterizer.RenderPageAsync(pdf, 0, new PdfRasterRequest { Dpi = 72 });
        var third = await rasterizer.RenderPageAsync(pdf, 2, new PdfRasterRequest { Dpi = 72 });

        first.Content.ShouldNotBe(third.Content, "每页写了不同的字，渲染结果必须不同");
    }

    [Fact]
    public async Task RenderPageAsync_ForAPageBeyondTheEnd_ThrowsArgumentOutOfRange()
    {
        // 调用方错误与环境错误分开：越界是前者。
        var pdf = TestPdfs.Blank(1);

        var ex = await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => Create().RenderPageAsync(pdf, 5));

        ex.Message.ShouldContain("1 page");
    }

    [Fact]
    public async Task RenderPageAsync_ForBytesThatAreNotAPdf_ThrowsPdfRasterizationException()
        => await Should.ThrowAsync<PdfRasterizationException>(
            () => Create().RenderPageAsync("this is not a pdf"u8.ToArray(), 0));

    [Fact]
    public async Task RenderPageAsync_WhenTheOutputWouldBlowThePixelBudget_RefusesBeforeRendering()
    {
        // ★ 页尺寸来自文件、dpi 来自调用方，两者相乘没有上界。没有这条闸门的话，
        // 一个上传口就是一次「一份文件放倒一个进程」。
        var pdf = TestPdfs.Blank(1);

        var ex = await Should.ThrowAsync<PdfRasterizationException>(
            () => Create(maxPagePixels: 100_000).RenderPageAsync(pdf, 0, new PdfRasterRequest { Dpi = 300 }));

        ex.Message.ShouldContain("MaxPagePixels");
    }

    [Fact]
    public async Task RenderPageAsync_WithNoBudget_RendersWhatWouldOtherwiseBeRefused()
    {
        // 证明上一条拦下来的是**配置**，不是这份文档本身渲染不了。
        var pdf = TestPdfs.Blank(1);

        var image = await Create(maxPagePixels: 0).RenderPageAsync(pdf, 0, new PdfRasterRequest { Dpi = 300 });

        image.Width.ShouldBe(2550);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1201)]
    public async Task RenderPageAsync_WithAnOutOfRangeDpi_Throws(int dpi)
        => await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => Create().RenderPageAsync(TestPdfs.Blank(1), 0, new PdfRasterRequest { Dpi = dpi }));

    [Fact]
    public async Task RenderPageAsync_WithANegativePageIndex_Throws()
        => await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => Create().RenderPageAsync(TestPdfs.Blank(1), -1));

    [Fact]
    public async Task RenderPageAsync_WithoutBytes_Throws()
        => await Should.ThrowAsync<ArgumentException>(() => Create().RenderPageAsync([], 0));
}
