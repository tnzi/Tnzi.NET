using System.Text;
using UglyToad.PdfPig;

namespace Tnzi.Documents.Tests;

/// <summary>
/// <see cref="PdfPigPdfInspector"/> 的页信息与标签定位。
/// </summary>
public class PdfPigPdfInspectorTests
{
    /// <summary>被分词器打散的典型标签形态：分号、等号、花括号混在一起。</summary>
    private const string Tag = "{{Key;type=date;role=Client}}";

    private const string TagPattern = @"\{\{([^}]+)\}\}";

    private readonly IPdfInspector _inspector = new PdfPigPdfInspector();

    [Fact]
    public void GetInfo_ReturnsPageCountAndSizeInPoints()
    {
        var pdf = TestPdfBuilder.Letter(new TestPdfBuilder.TextRun("hello", 72d, 700d));

        var info = _inspector.GetInfo(pdf);

        info.PageCount.ShouldBe(1);
        info.Pages[0].Number.ShouldBe(1);
        info.Pages[0].Width.ShouldBe(TestPdfBuilder.LetterWidth, 0.01d);
        info.Pages[0].Height.ShouldBe(TestPdfBuilder.LetterHeight, 0.01d);
    }

    [Fact]
    public void FindTags_MatchesATagThatWordSplittingWouldBreakApart()
    {
        var pdf = TestPdfBuilder.Letter(new TestPdfBuilder.TextRun(Tag, 72d, 700d));

        var matches = _inspector.FindTags(pdf, TagPattern);

        var match = matches.ShouldHaveSingleItem();
        match.Text.ShouldBe(Tag);
        match.PageNumber.ShouldBe(1);
        match.Groups[1].ShouldBe("Key;type=date;role=Client");
    }

    [Fact]
    public void FindTags_ReturnsNormalizedTopLeftCoordinates()
    {
        var pdf = TestPdfBuilder.Letter(new TestPdfBuilder.TextRun(Tag, 72d, 700d));

        var match = _inspector.FindTags(pdf, TagPattern).ShouldHaveSingleItem();

        // 左边界就是绘制起点 72pt
        match.Box.X.ShouldBe(72d / TestPdfBuilder.LetterWidth, 0.005d);
        // 基线在 700pt、页高 792pt，故顶边距页顶约 (792 - 700 - 字形上伸) / 792，落在 0.09-0.12 之间
        match.Box.Y.ShouldBeInRange(0.09d, 0.12d);
        match.Box.Width.ShouldBeGreaterThan(0d);
        match.Box.Height.ShouldBeGreaterThan(0d);
        match.Box.Bottom.ShouldBeLessThan(1d);
    }

    [Fact]
    public void FindTags_GroupsLettersByBaseline_NotByGlyphBottom()
    {
        var pdf = TestPdfBuilder.Letter(new TestPdfBuilder.TextRun(Tag, 72d, 700d));

        // 前提校验：这条标签里确实有下降部字形（{ y p），同一行的字形**底边并不相同**，
        // 而基线相同。否则本测试就退化成恒真，锁不住「按底边分组会把一行炸成好几段」这个坑。
        using (var document = PdfDocument.Open(pdf))
        {
            var letters = document.GetPage(1).Letters;
            letters.Select(letter => Math.Round(letter.BoundingBox.Bottom, 2)).Distinct().Count().ShouldBeGreaterThan(1);
            letters.Select(letter => Math.Round(letter.StartBaseLine.Y, 2)).Distinct().Count().ShouldBe(1);
        }

        var match = _inspector.FindTags(pdf, TagPattern).ShouldHaveSingleItem();

        match.LineBoxes.Count.ShouldBe(1);
        match.Box.ShouldBe(match.LineBoxes[0]);
    }

    [Fact]
    public void FindTags_TagWrappedOntoTwoLines_ReportsBothLinesAndPicksTheLargestAsPrimary()
    {
        var pdf = TestPdfBuilder.Letter(
            new TestPdfBuilder.TextRun("{{Key;type=date;role=Client;", 72d, 700d),
            new TestPdfBuilder.TextRun("x}}", 72d, 680d));

        var match = _inspector.FindTags(pdf, TagPattern).ShouldHaveSingleItem();

        match.LineBoxes.Count.ShouldBe(2);
        // 主定位框 = 面积最大的行框 = 第一行（更长），且它在上方（左上角原点下 Y 更小）
        match.Box.Width.ShouldBeGreaterThan(match.LineBoxes[1].Width);
        match.Box.Y.ShouldBeLessThan(match.LineBoxes[1].Y);
    }

    [Fact]
    public void FindTags_ReturnsEveryOccurrenceInReadingOrder()
    {
        var pdf = TestPdfBuilder.Letter(
            new TestPdfBuilder.TextRun("{{First}}", 72d, 700d),
            new TestPdfBuilder.TextRun("{{Second}}", 72d, 660d));

        var matches = _inspector.FindTags(pdf, TagPattern);

        matches.Count.ShouldBe(2);
        matches[0].Groups[1].ShouldBe("First");
        matches[1].Groups[1].ShouldBe("Second");
        matches[0].Box.Y.ShouldBeLessThan(matches[1].Box.Y);
    }

    [Fact]
    public void FindTags_NoOccurrence_ReturnsEmpty()
    {
        var pdf = TestPdfBuilder.Letter(new TestPdfBuilder.TextRun("nothing to see here", 72d, 700d));

        _inspector.FindTags(pdf, TagPattern).ShouldBeEmpty();
    }

    [Fact]
    public void FindTags_InvalidPattern_ThrowsWithTheOffendingPattern()
    {
        var pdf = TestPdfBuilder.Letter(new TestPdfBuilder.TextRun("hello", 72d, 700d));

        var exception = Should.Throw<PdfDocumentException>(() => _inspector.FindTags(pdf, "((("));

        exception.Message.ShouldContain("(((");
    }

    [Fact]
    public void GetInfo_BytesAreNotAPdf_ThrowsAReadableException()
    {
        var garbage = Encoding.ASCII.GetBytes("this is definitely not a pdf");

        Should.Throw<PdfDocumentException>(() => _inspector.GetInfo(garbage));
    }

    [Fact]
    public void GetInfo_EmptyBytes_Throws()
    {
        Should.Throw<PdfDocumentException>(() => _inspector.GetInfo([]));
    }
}
