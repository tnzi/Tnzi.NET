using System.Buffers.Binary;
using System.Text;
using Xunit.Abstractions;

namespace Tnzi.Documents.Tests;

/// <summary>
/// <see cref="ChromiumDocumentImageRenderer"/> 的认领判定、出图几何与真实截图。
/// </summary>
/// <remarks>
/// 真实截图只在本机确实装了 Chrome / Edge / Chromium 时才跑（与另外两组浏览器用例同一取舍）。
/// 几何是纯计算，永远跑：它算错的症状是「图出来了，只是比例不对」，不会有任何报错。
/// </remarks>
public class ChromiumDocumentImageRendererTests
{
    private readonly ITestOutputHelper _output;

    public ChromiumDocumentImageRendererTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData("composed.html")]
    [InlineData("form.HTML")]
    [InlineData("letter.htm")]
    public void CanRender_HtmlDocuments_ReturnsTrue(string fileName)
    {
        CreateRenderer().CanRender(fileName).ShouldBeTrue();
    }

    /// <summary>
    /// ★ <c>.pdf</c> 明确**不**支持：headless 浏览器打开 PDF 渲染的是查看器界面（工具栏 + 侧边缩略图面板 +
    /// 深色背景），不是页面本身。答 true 会让调用方拿到一张浏览器截图当缩略图。
    /// </summary>
    [Theory]
    [InlineData("archive.pdf")]
    [InlineData("contract.docx")]
    [InlineData("photo.png")]
    [InlineData("no-extension")]
    [InlineData("")]
    public void CanRender_EverythingElse_ReturnsFalse(string fileName)
    {
        CreateRenderer().CanRender(fileName).ShouldBeFalse();
    }

    [Fact]
    public void CanRender_WhenBrowserRenderingIsDisabled_ClaimsNothing()
    {
        CreateRenderer(options => options.Enabled = false).CanRender("composed.html").ShouldBeFalse();
    }

    [Fact]
    public void IsAvailableFor_CombinesFormatAndEnvironment()
    {
        // 装了浏览器的机器上 .html 可用、.pdf 不可用；两者都不该只看其中一个条件。
        // 走接口类型：IsAvailableFor 是默认接口方法，实现类上没有这个成员。
        IDocumentImageRenderer renderer = CreateRenderer();
        renderer.IsAvailableFor("archive.pdf").ShouldBeFalse();
        renderer.IsAvailableFor("composed.html").ShouldBe(renderer.IsAvailable);
    }

    [Fact]
    public async Task RenderFirstPageAsync_ConfiguredBrowserDoesNotExist_ThrowsWithConfigurationGuidance()
    {
        ChromiumLocator.ResetCache();
        var missing = Path.Combine(Path.GetTempPath(), "tnzi-no-such-browser", "chrome.exe");
        var renderer = CreateRenderer(options => options.BrowserPath = missing);

        var exception = await Should.ThrowAsync<DocumentConversionException>(
            () => renderer.RenderFirstPageAsync(HtmlDocument(), "composed.html"));

        exception.Message.ShouldContain("Documents:Html:BrowserPath");
        ChromiumLocator.ResetCache();
    }

    [Fact]
    public async Task RenderFirstPageAsync_WhenDisabled_SaysSoInsteadOfReturningAPlaceholder()
    {
        var exception = await Should.ThrowAsync<DocumentConversionException>(
            () => CreateRenderer(options => options.Enabled = false).RenderFirstPageAsync(HtmlDocument(), "composed.html"));

        exception.Message.ShouldContain("Documents:Html:Enabled");
    }

    [Fact]
    public async Task RenderFirstPageAsync_UnsupportedFormat_Throws()
    {
        var exception = await Should.ThrowAsync<DocumentConversionException>(
            () => CreateRenderer().RenderFirstPageAsync(HtmlDocument(), "archive.pdf"));

        exception.Message.ShouldContain(".pdf");
    }

    [Fact]
    public async Task RenderFirstPageAsync_EmptySource_Throws()
    {
        await Should.ThrowAsync<DocumentConversionException>(
            () => CreateRenderer().RenderFirstPageAsync([], "composed.html"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9000)]
    public async Task RenderFirstPageAsync_ImpossibleWidth_IsRejectedBeforeStartingABrowser(int width)
    {
        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => CreateRenderer().RenderFirstPageAsync(HtmlDocument(), "composed.html", new DocumentImageRequest { Width = width }));
    }

    [Fact]
    public async Task RenderFirstPageAsync_ImpossibleQuality_IsRejected()
    {
        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => CreateRenderer().RenderFirstPageAsync(
                HtmlDocument(),
                "composed.html",
                new DocumentImageRequest { Format = DocumentImageFormat.Jpeg, Quality = 0 }));
    }

    /// <summary>
    /// 出图几何：宽度按内容盒（纸张去掉边距）等比缩放，高度按请求裁剪但不越过首页。
    /// </summary>
    [Fact]
    public void ImageLayout_ScalesTheContentBoxAndKeepsTheAspectRatio()
    {
        // Letter 612x792pt、四边 28.8pt 边距 => 内容盒 554.4 x 734.4pt => 739.2 x 979.2 CSS px
        var layout = ChromiumDocumentImageRenderer.ImageLayout.Create(new HtmlPdfOptions(), new DocumentImageRequest { Width = 480 });

        layout.ContentWidthPx.ShouldBe(739.2d, tolerance: 0.01d);
        layout.ContentHeightPx.ShouldBe(979.2d, tolerance: 0.01d);
        layout.OutputWidth.ShouldBe(480);
        layout.OutputHeight.ShouldBe(636);   // 979.2 * (480/739.2)
    }

    [Fact]
    public void ImageLayout_ExplicitHeight_CropsFromTheTopInsteadOfSquashing()
    {
        // 消费方的实际用法：卡片上 240x104 CSS px @ DPR2
        var layout = ChromiumDocumentImageRenderer.ImageLayout.Create(
            new HtmlPdfOptions(), new DocumentImageRequest { Width = 480, Height = 208 });

        layout.OutputHeight.ShouldBe(208);
        // 裁的是页面顶部那一条，而不是把整页压进 208 像素
        layout.ClipHeightPx.ShouldBeLessThan(layout.ContentHeightPx);
    }

    [Fact]
    public void ImageLayout_HeightBeyondTheFirstPage_IsTruncated_NotBorrowedFromPageTwo()
    {
        var layout = ChromiumDocumentImageRenderer.ImageLayout.Create(
            new HtmlPdfOptions(), new DocumentImageRequest { Width = 480, Height = 5000 });

        layout.ClipHeightPx.ShouldBe(layout.ContentHeightPx);
        layout.OutputHeight.ShouldBe(636);
    }

    [Fact]
    public void ImageLayout_Landscape_SwapsThePaperBeforeMeasuring()
    {
        var portrait = ChromiumDocumentImageRenderer.ImageLayout.Create(new HtmlPdfOptions(), new DocumentImageRequest());
        var landscape = ChromiumDocumentImageRenderer.ImageLayout.Create(
            new HtmlPdfOptions { Landscape = true }, new DocumentImageRequest());

        landscape.ContentWidthPx.ShouldBe(portrait.ContentHeightPx, tolerance: 0.01d);
        landscape.ContentHeightPx.ShouldBe(portrait.ContentWidthPx, tolerance: 0.01d);
    }

    /// <summary>
    /// ★ 真实出图：像素尺寸必须**正好**是请求的尺寸，且 <see cref="DocumentImage"/> 报的与图里写的一致。
    /// </summary>
    /// <remarks>
    /// 缩略图错的方式几乎都是「出来了但不对」（尺寸差一点、比例变形、报的尺寸与实际不符），
    /// 没有任何一种会抛异常。所以这里直接去读 PNG 头里的真实宽高。
    /// </remarks>
    [Fact]
    public async Task RenderFirstPageAsync_WithARealBrowser_ProducesExactlyTheRequestedPixels()
    {
        if (!BrowserAvailableOrSkip())
            return;

        var image = await CreateRenderer().RenderFirstPageAsync(
            HtmlDocument(), "composed.html", new DocumentImageRequest { Width = 480, Height = 208 });

        image.ContentType.ShouldBe("image/png");
        image.FileExtension.ShouldBe(".png");
        image.Width.ShouldBe(480);
        image.Height.ShouldBe(208);

        var (width, height) = ReadPngSize(image.Content);
        width.ShouldBe(image.Width);
        height.ShouldBe(image.Height);
    }

    [Fact]
    public async Task RenderFirstPageAsync_WithoutAnExplicitHeight_KeepsTheWholeFirstPage()
    {
        if (!BrowserAvailableOrSkip())
            return;

        var image = await CreateRenderer().RenderFirstPageAsync(HtmlDocument(), "composed.html", new DocumentImageRequest { Width = 240 });

        var (width, height) = ReadPngSize(image.Content);
        width.ShouldBe(240);
        height.ShouldBe(image.Height);
        // 整页比例：Letter 内容盒约 1:1.32，出图必须还是竖的
        height.ShouldBeGreaterThan(width);
    }

    [Fact]
    public async Task RenderFirstPageAsync_Jpeg_IsLabelledAsJpeg()
    {
        if (!BrowserAvailableOrSkip())
            return;

        var image = await CreateRenderer().RenderFirstPageAsync(
            HtmlDocument(), "composed.html", new DocumentImageRequest { Width = 240, Format = DocumentImageFormat.Jpeg, Quality = 70 });

        image.ContentType.ShouldBe("image/jpeg");
        image.FileExtension.ShouldBe(".jpg");
        image.Content[0].ShouldBe((byte)0xFF);
        image.Content[1].ShouldBe((byte)0xD8);
    }

    private bool BrowserAvailableOrSkip()
    {
        ChromiumLocator.ResetCache();
        var executable = ChromiumLocator.Resolve(null);

        if (executable == null)
            _output.WriteLine("Skipped: no Chromium-based browser is installed on this machine.");
        else
            _output.WriteLine($"Rendering with the browser at '{executable}'.");

        return executable != null;
    }

    /// <summary>直接读 PNG 的 IHDR 块（第 16-23 字节，大端）—— 不需要任何图像库。</summary>
    private static (int Width, int Height) ReadPngSize(byte[] png)
    {
        png.Length.ShouldBeGreaterThan(24);
        png[..4].ShouldBe(new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        return (
            BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
            BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
    }

    private static ChromiumDocumentImageRenderer CreateRenderer(Action<HtmlPdfOptions>? configure = null)
    {
        var options = new HtmlPdfOptions();
        configure?.Invoke(options);

        return new ChromiumDocumentImageRenderer(
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<ChromiumDocumentImageRenderer>.Instance);
    }

    private static byte[] HtmlDocument()
        => Encoding.UTF8.GetBytes(
            "<!doctype html><html><head><meta charset=\"utf-8\"><style>" +
            "body{font-family:Arial,Helvetica,sans-serif;font-size:12pt;margin:0}" +
            ".title{text-align:center;font-size:18pt}" +
            "</style></head><body><div class=\"title\">Tnzi form {{ClientName;type=text}}</div>" +
            "<p>Body text so the page is not blank.</p></body></html>");
}
