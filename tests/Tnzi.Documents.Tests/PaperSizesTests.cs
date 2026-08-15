namespace Tnzi.Documents.Tests;

/// <summary>
/// <see cref="PaperSizes"/> 的取值与查不到时的行为。
/// </summary>
public class PaperSizesTests
{
    [Theory]
    [InlineData("Letter", 612d, 792d)]
    [InlineData("letter", 612d, 792d)]
    [InlineData("  A4  ", 595d, 842d)]
    [InlineData("Legal", 612d, 1008d)]
    [InlineData("A3", 842d, 1191d)]
    public void TryGet_KnownNames_AreCaseInsensitiveAndTrimmed(string name, double width, double height)
    {
        PaperSizes.TryGet(name, out var size).ShouldBeTrue();
        size.WidthPt.ShouldBe(width);
        size.HeightPt.ShouldBe(height);
    }

    [Theory]
    [InlineData("US-Letter")]
    [InlineData("")]
    [InlineData(null)]
    public void TryGet_UnknownName_ReturnsFalse_WithoutFallingBackToADefault(string? name)
    {
        // 回退到默认值会把「配置里写错了纸张名」变成「出图尺寸莫名其妙」——
        // 所以这里返回 false，由验证器在启动期把它变成一条明确的错误。
        PaperSizes.TryGet(name, out _).ShouldBeFalse();
    }

    [Fact]
    public void Names_CoverTheNorthAmericanAndIsoSeries()
    {
        PaperSizes.Names.ShouldContain("Letter");
        PaperSizes.Names.ShouldContain("Legal");
        PaperSizes.Names.ShouldContain("A4");
    }
}
