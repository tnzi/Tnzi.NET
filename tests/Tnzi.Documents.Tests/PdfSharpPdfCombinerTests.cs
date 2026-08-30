using System.Text;

namespace Tnzi.Documents.Tests;

/// <summary>
/// <see cref="PdfSharpPdfCombiner"/> 的顺序、页几何保留与非法输入。
/// </summary>
/// <remarks>
/// 合并出错的方式几乎都是「产物看着正常」：少了一份、顺序反了、横页被摆正。
/// 所以这里全部断言到**页数与每一页的尺寸**，而不是"没抛异常"。
/// </remarks>
public class PdfSharpPdfCombinerTests
{
    private readonly IPdfCombiner _combiner = new PdfSharpPdfCombiner();
    private readonly IPdfInspector _inspector = new PdfPigPdfInspector();

    /// <summary>A4 纵向（point）。</summary>
    private const double A4Width = 595d;
    private const double A4Height = 842d;

    [Fact]
    public void Combine_ConcatenatesEveryPage_InTheGivenOrder()
    {
        var first = TestPdfBuilder.Letter(new TestPdfBuilder.TextRun("first", 72d, 700d));
        var second = TestPdfBuilder.Build(A4Width, A4Height, new TestPdfBuilder.TextRun("second", 72d, 700d));
        var third = TestPdfBuilder.Letter(new TestPdfBuilder.TextRun("third", 72d, 700d));

        var combined = _combiner.Combine([first, second, third]);

        var info = _inspector.GetInfo(combined);
        info.PageCount.ShouldBe(3);

        // 顺序：文本按投递顺序出现，且第 2 页确实是中间那份
        _inspector.FindTags(combined, "second").Single().PageNumber.ShouldBe(2);
        _inspector.FindTags(combined, "third").Single().PageNumber.ShouldBe(3);
    }

    [Fact]
    public void Combine_PreservesEachSourcePageSizeAndOrientation()
    {
        var letter = TestPdfBuilder.Letter(new TestPdfBuilder.TextRun("portrait letter", 72d, 700d));
        var a4 = TestPdfBuilder.Build(A4Width, A4Height, new TestPdfBuilder.TextRun("portrait a4", 72d, 700d));
        // 横向 = 宽高对调，不是一个标志位；被"摆正"的症状是内容旋转 90 度而没有任何一步失败
        var landscape = TestPdfBuilder.Build(A4Height, A4Width, new TestPdfBuilder.TextRun("landscape", 72d, 400d));

        var combined = _combiner.Combine([letter, a4, landscape]);

        var info = _inspector.GetInfo(combined);
        info.Pages[0].Width.ShouldBe(TestPdfBuilder.LetterWidth, 0.01d);
        info.Pages[0].Height.ShouldBe(TestPdfBuilder.LetterHeight, 0.01d);
        info.Pages[1].Width.ShouldBe(A4Width, 0.01d);
        info.Pages[1].Height.ShouldBe(A4Height, 0.01d);
        info.Pages[2].Width.ShouldBe(A4Height, 0.01d);
        info.Pages[2].Height.ShouldBe(A4Width, 0.01d);
    }

    [Fact]
    public void Combine_KeepsMultiPageSourcesWhole()
    {
        var singlePage = TestPdfBuilder.Letter(new TestPdfBuilder.TextRun("cover", 72d, 700d));
        var twoPages = _combiner.Combine([
            TestPdfBuilder.Letter(new TestPdfBuilder.TextRun("body one", 72d, 700d)),
            TestPdfBuilder.Letter(new TestPdfBuilder.TextRun("body two", 72d, 700d))
        ]);

        var combined = _combiner.Combine([singlePage, twoPages]);

        _inspector.GetInfo(combined).PageCount.ShouldBe(3);
    }

    /// <summary>
    /// 单份输入原样返回：那份文档本来就是答案，再经 PDFsharp 转一手只会平白改写文件结构。
    /// </summary>
    [Fact]
    public void Combine_WithASingleDocument_ReturnsTheInputBytesUnchanged()
    {
        var only = TestPdfBuilder.Letter(new TestPdfBuilder.TextRun("only", 72d, 700d));

        var combined = _combiner.Combine([only]);

        combined.ShouldBe(only);
    }

    /// <summary>
    /// 单份也要验合法性 —— 否则"只有一份"就成了绕过检查的口子，
    /// 而调用方拿到的是一份到了下游才炸的字节。
    /// </summary>
    [Fact]
    public void Combine_WithASingleInvalidDocument_StillFails()
    {
        Should.Throw<PdfDocumentException>(() => _combiner.Combine([Encoding.ASCII.GetBytes("not a pdf")]));
    }

    /// <summary>
    /// 短路那条走的是与循环体**同一批**检查（同一个私有 helper），这里把每一项各钉一条：
    /// 空字节数组走 RequireBytes，畸形字节走 Open。少任何一项，"只有一份"就成了绕过检查的口子。
    /// </summary>
    [Fact]
    public void Combine_WithASingleEmptyByteArray_StillFails()
    {
        var exception = Should.Throw<PdfDocumentException>(() => _combiner.Combine([[]]));

        exception.Message.ShouldContain("index 0");
    }

    [Fact]
    public void Combine_WithNoDocuments_IsACallerError()
    {
        // 零页文档不是合法 PDF：这是调用方传错了，不是 PDF 层面的失败
        Should.Throw<ArgumentException>(() => _combiner.Combine([]));
    }

    [Fact]
    public void Combine_WithNullList_Throws()
    {
        Should.Throw<ArgumentNullException>(() => _combiner.Combine(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Combine_WithAnInvalidDocument_SaysWhichOne(int badIndex)
    {
        var documents = new List<byte[]>
        {
            TestPdfBuilder.Letter(new TestPdfBuilder.TextRun("a", 72d, 700d)),
            TestPdfBuilder.Letter(new TestPdfBuilder.TextRun("b", 72d, 700d)),
            TestPdfBuilder.Letter(new TestPdfBuilder.TextRun("c", 72d, 700d))
        };
        documents[badIndex] = Encoding.ASCII.GetBytes("%PDF-1.4 but truncated");

        // 「合并失败」四个字没法排查：错误必须点名是第几份
        var exception = Should.Throw<PdfDocumentException>(() => _combiner.Combine(documents));
        exception.Message.ShouldContain($"index {badIndex}");
    }

    [Fact]
    public void Combine_WithAnEmptyByteArray_FailsAsAPdfError()
    {
        var good = TestPdfBuilder.Letter(new TestPdfBuilder.TextRun("a", 72d, 700d));

        var exception = Should.Throw<PdfDocumentException>(() => _combiner.Combine([good, []]));
        exception.Message.ShouldContain("index 1");
    }

    /// <summary>
    /// 底层库的异常一律包成 <see cref="PdfDocumentException"/> 并保留 InnerException ——
    /// 调用方按框架异常兜底，不必认识 PDFsharp。
    /// </summary>
    [Fact]
    public void Combine_WrapsTheUnderlyingLibraryException()
    {
        var good = TestPdfBuilder.Letter(new TestPdfBuilder.TextRun("a", 72d, 700d));

        var exception = Should.Throw<PdfDocumentException>(
            () => _combiner.Combine([good, Encoding.ASCII.GetBytes("definitely not a pdf")]));

        exception.InnerException.ShouldNotBeNull();
        exception.Code.ShouldBe(new PdfDocumentException("probe").Code);
    }
}
