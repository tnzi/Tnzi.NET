using System.Text;
using Xunit.Abstractions;

namespace Tnzi.Documents.Tests;

/// <summary>
/// <see cref="ChromiumHtmlDocumentConverter"/> 的认领判定、失败路径与真实渲染。
/// </summary>
/// <remarks>
/// 真实渲染只在本机确实装了 Chrome / Edge / Chromium 时才跑：渲染依赖外部浏览器，
/// 把它设成硬性前置会让 CI 无谓变红（与 LibreOffice 那组测试同一取舍）。
/// </remarks>
public class ChromiumHtmlDocumentConverterTests
{
    private const string TagPattern = @"\{\{[^}]+\}\}";

    private readonly ITestOutputHelper _output;

    public ChromiumHtmlDocumentConverterTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData("composed.html")]
    [InlineData("form.HTML")]
    [InlineData("letter.htm")]
    public void CanConvert_HtmlDocuments_ReturnsTrue(string fileName)
    {
        CreateConverter().CanConvert(fileName).ShouldBeTrue();
    }

    [Theory]
    [InlineData("contract.docx")]   // Office 由 LibreOffice 那条路径处理
    [InlineData("contract.pdf")]
    [InlineData("no-extension")]
    [InlineData("")]
    public void CanConvert_NonHtmlInputs_ReturnsFalse(string fileName)
    {
        CreateConverter().CanConvert(fileName).ShouldBeFalse();
    }

    [Fact]
    public void CanConvert_WhenDisabled_ClaimsNothing_SoRoutingFallsBackToLibreOffice()
    {
        // 这是「退回旧行为」的唯一开关：不认领 => RoutingDocumentConverter 把 HTML 交给 LibreOffice
        CreateConverter(options => options.Enabled = false).CanConvert("composed.html").ShouldBeFalse();
    }

    [Fact]
    public async Task ConvertToPdfAsync_ConfiguredBrowserDoesNotExist_ThrowsWithConfigurationGuidance()
    {
        // 配置了路径就**不回退**自动探测：即便本机装了浏览器，配错也必须报错
        ChromiumLocator.ResetCache();
        var missing = Path.Combine(Path.GetTempPath(), "tnzi-no-such-browser", "chrome.exe");
        var converter = CreateConverter(options => options.BrowserPath = missing);

        var exception = await Should.ThrowAsync<DocumentConversionException>(
            () => converter.ConvertToPdfAsync(HtmlDocument(), "composed.html"));

        exception.Message.ShouldContain("Documents:Html:BrowserPath");
        exception.Message.ShouldContain(missing);
        ChromiumLocator.ResetCache();
    }

    [Fact]
    public async Task ConvertToPdfAsync_WhenDisabled_SaysSoInsteadOfSilentlyUsingAnotherEngine()
    {
        var converter = CreateConverter(options => options.Enabled = false);

        var exception = await Should.ThrowAsync<DocumentConversionException>(
            () => converter.ConvertToPdfAsync(HtmlDocument(), "composed.html"));

        exception.Message.ShouldContain("Documents:Html:Enabled");
    }

    [Fact]
    public async Task ConvertToPdfAsync_NonHtmlExtension_Throws()
    {
        var converter = CreateConverter();

        var exception = await Should.ThrowAsync<DocumentConversionException>(
            () => converter.ConvertToPdfAsync(HtmlDocument(), "contract.docx"));

        exception.Message.ShouldContain(".docx");
        exception.Message.ShouldContain(".html");
    }

    [Fact]
    public async Task ConvertToPdfAsync_EmptySource_Throws()
    {
        await Should.ThrowAsync<DocumentConversionException>(
            () => CreateConverter().ConvertToPdfAsync([], "composed.html"));
    }

    [Theory]
    [InlineData(null, 0, 0, 612d, 792d)]              // 默认 = US Letter
    [InlineData("A4", 0, 0, 595d, 842d)]
    [InlineData("A4", 300, 400, 300d, 400d)]          // 显式宽高覆盖纸张名
    public void ResolvePaperSize_PrefersExplicitPointsOverTheNamedSize(
        string? paperSize, double widthPt, double heightPt, double expectedWidth, double expectedHeight)
    {
        var options = new HtmlPdfOptions { PaperWidthPt = widthPt, PaperHeightPt = heightPt };
        if (paperSize != null)
            options.PaperSize = paperSize;

        var resolved = ChromiumHtmlDocumentConverter.ResolvePaperSize(options);

        resolved.WidthPt.ShouldBe(expectedWidth);
        resolved.HeightPt.ShouldBe(expectedHeight);
    }

    /// <summary>
    /// ★ 本套里最重要的一条：产出必须是**可搜索的真文本**，不是图片。
    /// </summary>
    /// <remarks>
    /// 消费方靠 <see cref="IPdfInspector.FindTags"/> 扫文本层里的 <c>{{…}}</c> 来定位签名字段框。
    /// 一旦哪天渲染换成栅格化、或把字形转成轮廓，文档看上去毫无异样，**每一个字段的坐标却会静默丢失**。
    /// 所以这里不满足于「PDF 生成成功」，而是一路断言到能把标签重新找回来。
    /// </remarks>
    [Fact]
    public async Task ConvertToPdfAsync_WithARealBrowser_RendersASearchableLetterSizedPdf()
    {
        var executable = ResolveBrowserOrSkip();
        if (executable == null)
            return;

        var converter = CreateConverter();

        var pdf = await converter.ConvertToPdfAsync(HtmlDocument(), "composed.html");

        Encoding.ASCII.GetString(pdf, 0, 5).ShouldBe("%PDF-");

        var inspector = new PdfPigPdfInspector();
        var info = inspector.GetInfo(pdf);
        info.PageCount.ShouldBe(1);

        // 纸张默认 US Letter（612 x 792 点），不是 LibreOffice 给的 A4
        info.Pages[0].Width.ShouldBe(612d, tolerance: 1d);
        info.Pages[0].Height.ShouldBe(792d, tolerance: 1d);

        var matches = inspector.FindTags(pdf, TagPattern);
        matches.Count.ShouldBe(1);
        matches[0].Text.ShouldContain("ClientName");
        matches[0].Box.Width.ShouldBeGreaterThan(0d);
    }

    /// <summary>
    /// 文档自带 <c>@page { size: … }</c> 时以它为准 —— 这是「调用方控制页面尺寸」最精确的一档。
    /// </summary>
    [Fact]
    public async Task ConvertToPdfAsync_DocumentDeclaresItsOwnPageSize_TheDocumentWins()
    {
        var executable = ResolveBrowserOrSkip();
        if (executable == null)
            return;

        // 配置说 Letter，文档说 A5：PreferCssPageSize 默认为真，文档赢
        var converter = CreateConverter(options => options.PaperSize = "Letter");
        var html = HtmlDocument("@page { size: 420pt 595pt; margin: 0 }");

        var info = new PdfPigPdfInspector().GetInfo(await converter.ConvertToPdfAsync(html, "composed.html"));

        info.Pages[0].Width.ShouldBe(420d, tolerance: 2d);
        info.Pages[0].Height.ShouldBe(595d, tolerance: 2d);
    }

    private string? ResolveBrowserOrSkip()
    {
        ChromiumLocator.ResetCache();
        var executable = ChromiumLocator.Resolve(null);

        if (executable == null)
            _output.WriteLine("Skipped: no Chromium-based browser is installed on this machine.");
        else
            _output.WriteLine($"Rendering with the browser at '{executable}'.");

        return executable;
    }

    private static ChromiumHtmlDocumentConverter CreateConverter(Action<HtmlPdfOptions>? configure = null)
    {
        var options = new HtmlPdfOptions();
        configure?.Invoke(options);

        return new ChromiumHtmlDocumentConverter(
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<ChromiumHtmlDocumentConverter>.Instance);
    }

    /// <summary>带一个合并标签的最小 HTML —— 标签是文本层断言的抓手。</summary>
    private static byte[] HtmlDocument(string extraCss = "")
        => Encoding.UTF8.GetBytes(
            "<!doctype html><html><head><meta charset=\"utf-8\"><style>" +
            "body{font-family:Arial,Helvetica,sans-serif;font-size:12pt}" +
            ".title{text-align:center}" +
            extraCss +
            "</style></head><body><div class=\"title\">Tnzi form {{ClientName;type=text}}</div></body></html>");
}
