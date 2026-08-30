namespace Tnzi.Documents.Tests;

/// <summary>
/// 现造测试用 PDF。
/// </summary>
/// <remarks>
/// ★ 不放二进制夹具：夹具会把「某个版本的 PDF 生成器的输出」冻在仓库里，
/// 而这些测试要守的是「随便一份 PDF 都渲染得出来」。用框架自己的
/// <see cref="IPdfStamper"/> 造，顺带也让这条链路一直是可编译的。
/// </remarks>
internal static class TestPdfs
{
    /// <summary>US Letter 的点尺寸。</summary>
    public const double LetterWidthPoints = 612d;

    /// <summary>US Letter 的点尺寸。</summary>
    public const double LetterHeightPoints = 792d;

    private static readonly PdfSharpPdfStamper Stamper = new(NullLogger<PdfSharpPdfStamper>.Instance);

    /// <summary>造一份指定页数的空白 US Letter 文档，每页左上角写一行可区分的文字。</summary>
    public static byte[] Blank(int pageCount) => Stamper.Create(new PdfStampRequest
    {
        AppendPages = Enumerable.Range(0, pageCount)
            .Select(_ => new PdfPageSpec { WidthPoints = LetterWidthPoints, HeightPoints = LetterHeightPoints })
            .ToList(),
        Stamps = Enumerable.Range(1, pageCount)
            .Select(page => new PdfTextStamp
            {
                PageNumber = page,
                Rect = new NormalizedRect(0.1, 0.1, 0.5, 0.05),
                Text = $"PAGE {page}",
                FontSize = 36
            })
            .Cast<PdfStamp>()
            .ToList()
    });

    /// <summary>造一份「扫描件形态」的单页文档：整页只有一张位图，没有文本层。</summary>
    /// <param name="stampedImage">铺满页面的位图（PNG）。</param>
    /// <remarks>
    /// ★ 这里造不出真正的 CCITT G4 传真件（PdfSharp 不写那个编码），
    /// 但它复现了关键的那一点：<b>页面内容是像素而不是文字</b>。
    /// 而「渲染整页」这条路本来就不看图像用什么编码 —— 那是 PDFium 的事。
    /// </remarks>
    public static byte[] ScannedPage(byte[] stampedImage) => Stamper.Create(new PdfStampRequest
    {
        AppendPages = [new PdfPageSpec { WidthPoints = LetterWidthPoints, HeightPoints = LetterHeightPoints }],
        Stamps =
        [
            new PdfImageStamp
            {
                PageNumber = 1,
                Rect = new NormalizedRect(0, 0, 1, 1),
                Content = stampedImage,
                PreserveAspectRatio = false
            }
        ]
    });

    /// <summary>把一张图按归一化坐标盖到一页空白 US Letter 上。</summary>
    public static byte[] PageWithImageAt(byte[] image, NormalizedRect rect) => Stamper.Create(new PdfStampRequest
    {
        AppendPages = [new PdfPageSpec { WidthPoints = LetterWidthPoints, HeightPoints = LetterHeightPoints }],
        Stamps = [new PdfImageStamp { PageNumber = 1, Rect = rect, Content = image }]
    });
}
