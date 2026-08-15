namespace Tnzi.Documents.Tests;

/// <summary>
/// <see cref="HtmlPdfOptionsValidator"/> —— 配置写错要在启动期就说清楚。
/// </summary>
/// <remarks>
/// 这些字段配错的症状全都是「出图了，只是不对」：纸张名笔误、只配了宽没配高、缩放超出浏览器允许的范围。
/// 它们不会让任何请求失败，只会让文档悄悄变成另一个样子，所以必须在启动期拦下来。
/// </remarks>
public class HtmlPdfOptionsValidatorTests
{
    [Fact]
    public void Defaults_AreValid()
    {
        Validate(new HtmlPdfOptions()).ShouldBeEmpty();
    }

    [Fact]
    public void UnknownPaperSize_IsRejectedAndTheKnownOnesAreListed()
    {
        var errors = Validate(new HtmlPdfOptions { PaperSize = "US-Letter" });

        errors.ShouldHaveSingleItem();
        errors[0].ShouldContain(nameof(HtmlPdfOptions.PaperSize));
        errors[0].ShouldContain("Letter");
    }

    [Theory]
    [InlineData("Letter")]
    [InlineData("letter")]
    [InlineData("A4")]
    [InlineData("Legal")]
    public void KnownPaperSizes_AreAccepted_CaseInsensitively(string paperSize)
    {
        Validate(new HtmlPdfOptions { PaperSize = paperSize }).ShouldBeEmpty();
    }

    [Fact]
    public void HalfAnExplicitPaperSize_IsRejected()
    {
        // 只配宽不配高会静默按纸张名出图，看起来像「配了但没生效」
        var errors = Validate(new HtmlPdfOptions { PaperWidthPt = 612 });

        errors.ShouldHaveSingleItem();
        errors[0].ShouldContain(nameof(HtmlPdfOptions.PaperHeightPt));
    }

    [Fact]
    public void ExplicitPaperSize_MakesTheNamedSizeIrrelevant()
    {
        // 显式宽高覆盖纸张名，此时纸张名是什么都不该再被校验
        Validate(new HtmlPdfOptions { PaperSize = "nonsense", PaperWidthPt = 612, PaperHeightPt = 792 })
            .ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0.05)]
    [InlineData(2.5)]
    public void ScaleOutsideTheBrowsersRange_IsRejected(double scale)
    {
        Validate(new HtmlPdfOptions { Scale = scale }).ShouldNotBeEmpty();
    }

    [Fact]
    public void NegativeMargins_AreRejected()
    {
        Validate(new HtmlPdfOptions { MarginLeftPt = -1 }).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void ConcurrencyOutsideTheAllowedRange_IsRejected(int maxConcurrency)
    {
        Validate(new HtmlPdfOptions { MaxConcurrency = maxConcurrency }).ShouldNotBeEmpty();
    }

    [Fact]
    public void BrowserPathThatDoesNotExist_IsRejectedAtStartup()
    {
        var missing = Path.Combine(Path.GetTempPath(), "tnzi-no-such-browser", "chrome.exe");

        var errors = Validate(new HtmlPdfOptions { BrowserPath = missing });

        errors.ShouldHaveSingleItem();
        errors[0].ShouldContain(missing);
    }

    private static IReadOnlyList<string> Validate(HtmlPdfOptions options)
    {
        var result = new HtmlPdfOptionsValidator().Validate(name: null, options);
        return result.Failed ? result.Failures.ToList() : [];
    }
}
