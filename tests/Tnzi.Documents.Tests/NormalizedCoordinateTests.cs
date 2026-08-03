namespace Tnzi.Documents.Tests;

/// <summary>
/// 归一化坐标换算。
/// </summary>
/// <remarks>
/// 本包同时接触三套坐标系（PDF 原生左下角原点 / 归一化左上角原点 / PDFsharp XGraphics 左上角原点），
/// **写的那一侧再翻一次 Y** 是最容易犯、也最难在肉眼下发现的错（章会盖到页面另一头）。
/// 这组测试把「读要翻、写不翻」钉死。
/// </remarks>
public class NormalizedCoordinateTests
{
    private const double Tolerance = 1e-9;

    [Fact]
    public void ToPageRect_ScalesOnly_DoesNotFlipY()
    {
        var rect = new NormalizedRect(0.25d, 0.10d, 0.5d, 0.05d);

        var page = NormalizedCoordinates.ToPageRect(rect, TestPdfBuilder.LetterWidth, TestPdfBuilder.LetterHeight);

        page.X.ShouldBe(153d, Tolerance);
        // 顶边 = Y * 页高。若这里写成 (1 - Y) * 页高，章会盖到页面另一头。
        page.Y.ShouldBe(79.2d, Tolerance);
        page.Width.ShouldBe(306d, Tolerance);
        page.Height.ShouldBe(39.6d, Tolerance);
    }

    [Fact]
    public void ToPageRect_ZeroWidth_ExtendsToThePageRightEdge()
    {
        var rect = new NormalizedRect(0.25d, 0.10d, 0d, 0.05d);

        var page = NormalizedCoordinates.ToPageRect(rect, TestPdfBuilder.LetterWidth, TestPdfBuilder.LetterHeight);

        page.Width.ShouldBe(TestPdfBuilder.LetterWidth - 153d, Tolerance);
    }

    [Fact]
    public void ToPageRect_ZeroHeight_FallsBackToTheGivenLineHeight()
    {
        var rect = new NormalizedRect(0.25d, 0.10d, 0.5d, 0d);

        var page = NormalizedCoordinates.ToPageRect(rect, TestPdfBuilder.LetterWidth, TestPdfBuilder.LetterHeight, lineHeight: 14d);

        page.Height.ShouldBe(14d, Tolerance);
    }

    [Fact]
    public void FromPdfBox_FlipsYToATopLeftOrigin()
    {
        // PDF 原生坐标：文字底 700、顶 712、左 72、右 172
        var rect = NormalizedCoordinates.FromPdfBox(72d, 700d, 172d, 712d, TestPdfBuilder.LetterWidth, TestPdfBuilder.LetterHeight);

        rect.X.ShouldBe(72d / TestPdfBuilder.LetterWidth, Tolerance);
        rect.Y.ShouldBe(1d - (712d / TestPdfBuilder.LetterHeight), Tolerance);
        rect.Width.ShouldBe(100d / TestPdfBuilder.LetterWidth, Tolerance);
        rect.Height.ShouldBe(12d / TestPdfBuilder.LetterHeight, Tolerance);
    }

    [Fact]
    public void ReadThenWrite_KeepsTheBoxAtTheSamePhysicalSpot()
    {
        // 读（翻 Y）再写（不翻 Y）应当原地不动：顶边距页顶 792 - 712 = 80pt
        var rect = NormalizedCoordinates.FromPdfBox(72d, 700d, 172d, 712d, TestPdfBuilder.LetterWidth, TestPdfBuilder.LetterHeight);

        var page = NormalizedCoordinates.ToPageRect(rect, TestPdfBuilder.LetterWidth, TestPdfBuilder.LetterHeight);

        page.X.ShouldBe(72d, Tolerance);
        page.Y.ShouldBe(80d, Tolerance);
        page.Width.ShouldBe(100d, Tolerance);
        page.Height.ShouldBe(12d, Tolerance);
    }

    [Fact]
    public void FromPdfBox_DegeneratePageSize_ReturnsEmptyInsteadOfInfinity()
    {
        NormalizedCoordinates.FromPdfBox(0d, 0d, 10d, 10d, 0d, 792d).ShouldBe(NormalizedRect.Empty);
    }
}
