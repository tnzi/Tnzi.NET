namespace Tnzi.Documents.Tests;

/// <summary>
/// 纸质回路的验收级测试：造码 → 盖进 PDF → 光栅化 → 读回。
/// </summary>
/// <remarks>
/// <para>
/// ★ 这三个原语（<see cref="IQrCodeGenerator"/> / <see cref="IPdfRasterizer"/> /
/// <see cref="IQrCodeReader"/>）各自的单元测试都绿，也证明不了这条回路闭合 ——
/// 每一环的输出都要成为下一环能吃的输入，而那是三个包之间的事。
/// 「机制建好了没接上」是这个仓库反复出现过的失效形态，所以这条回路必须有一条测试真的走一遍。
/// </para>
/// <para>
/// 印码参数取自实测：22 毫米见方、纠错等级 Q、版本 1 —— 那组参数在五条回传路径
/// （含两代标准传真）上 500/500 全部读回。
/// </para>
/// </remarks>
public class PaperRoundTripTests
{
    private const string Payload = "TNZI-CORR-000123";

    /// <summary>22 毫米折算成点（1 点 = 1/72 英寸）。</summary>
    private const double CodeSidePoints = 22d / 25.4d * 72d;

    private static readonly QrCodeGenerator Generator = new();
    private static readonly QrCodeReader Reader = new();

    private static PdfiumPdfRasterizer Rasterizer() => new(
        Microsoft.Extensions.Options.Options.Create(new PdfRasterOptions()),
        NullLogger<PdfiumPdfRasterizer>.Instance);

    private static QrCodeImage PrintableCode() => Generator.Generate(new QrCodeRequest
    {
        Payload = Payload,
        ErrorCorrection = QrErrorCorrection.Quartile,
        MinVersion = 1,
        MaxVersion = 1,
        // 600 dpi 打印下 22 毫米约 520 像素；取整数每模块像素数，实际略小。
        TargetSize = 520
    });

    /// <summary>把码放在页面右上角，按 22 毫米见方。</summary>
    private static NormalizedRect CornerPlacement() => new(
        1 - ((CodeSidePoints + 36) / TestPdfs.LetterWidthPoints),
        36 / TestPdfs.LetterHeightPoints,
        CodeSidePoints / TestPdfs.LetterWidthPoints,
        CodeSidePoints / TestPdfs.LetterHeightPoints);

    [Theory]
    [InlineData(200)]
    [InlineData(300)]
    public async Task ACodePrintedOnAPage_SurvivesRasterisationAndIsReadBack(int scanDpi)
    {
        var code = PrintableCode();
        code.Version.ShouldBe(1, "版本 1 是那组实测参数的前提");

        var pdf = TestPdfs.PageWithImageAt(code.Content, CornerPlacement());

        var page = await Rasterizer().RenderPageAsync(pdf, 0, new PdfRasterRequest { Dpi = scanDpi });

        var result = await Reader.ReadAsync(page.Content);

        result.Payload.ShouldBe(Payload, $"{scanDpi} dpi 下 22 毫米的码应当读得回来");
    }

    [Fact]
    public async Task ACodeOnAScannedPageWithNoTextLayer_IsStillReadBack()
    {
        // ★ 入站的从来不是框架自己生成的那份 PDF，而是对方扫描或传真回来的：
        // 整页只有一张位图、没有文本层。抠内嵌图像那条路会在这里开始碰壁，
        // 而「渲染整页」不在乎页面内容是什么。
        var code = PrintableCode();

        using var sheet = new Image<L8>(1700, 2200, new L8(255));
        using (var stamped = Image.Load<L8>(code.Content))
        {
            sheet.Mutate(x => x.DrawImage(stamped, new Point(1120, 100), 1f));
        }

        using var scan = new MemoryStream();
        sheet.Save(scan, new SixLabors.ImageSharp.Formats.Png.PngEncoder
        {
            ColorType = SixLabors.ImageSharp.Formats.Png.PngColorType.Grayscale,
            BitDepth = SixLabors.ImageSharp.Formats.Png.PngBitDepth.Bit8
        });

        var pdf = TestPdfs.ScannedPage(scan.ToArray());

        var page = await Rasterizer().RenderPageAsync(pdf, 0, new PdfRasterRequest { Dpi = 200 });

        (await Reader.ReadAsync(page.Content)).Payload.ShouldBe(Payload);
    }

    [Fact]
    public async Task APageWithoutACode_ComesBackAsNotFound()
    {
        // 回错了页是正常情况，不是故障 —— 这条回路必须能平静地说「这张纸上没有码」。
        var pdf = TestPdfs.Blank(1);

        var page = await Rasterizer().RenderPageAsync(pdf, 0, new PdfRasterRequest { Dpi = 200 });

        (await Reader.ReadAsync(page.Content)).Found.ShouldBeFalse();
    }
}
