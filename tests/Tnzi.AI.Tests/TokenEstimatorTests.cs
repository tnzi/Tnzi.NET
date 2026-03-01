namespace Tnzi.AI.Tests;

/// <summary>
/// HeuristicTokenEstimator 单元测试
/// </summary>
public class TokenEstimatorTests
{
    private readonly ITokenEstimator _estimator = new HeuristicTokenEstimator();

    [Fact]
    public void Estimate_PureAscii_DividesBy4PlusOverhead()
    {
        // "hello" = 5 ASCII chars → each counts 1 → 5/4 + 500 = 501
        var result = _estimator.Estimate("hello");
        result.ShouldBe(501);
    }

    [Fact]
    public void Estimate_PureChinese_CountsNonAsciiHigher()
    {
        // "你好世界" = 4 non-ASCII chars → each counts 8 → 32/4 + 500 = 508
        var result = _estimator.Estimate("你好世界");
        result.ShouldBe(508);
    }

    [Fact]
    public void Estimate_MixedContent_HandlesCorrectly()
    {
        // "Hi你好" = 2 ASCII (2×1) + 2 non-ASCII (2×8) = 2+16 = 18 → 18/4 + 500 = 504
        var result = _estimator.Estimate("Hi你好");
        result.ShouldBe(504);
    }

    [Fact]
    public void Estimate_EmptyString_ReturnsBaseOverhead()
    {
        var result = _estimator.Estimate("");
        result.ShouldBe(500);
    }

    [Fact]
    public void Estimate_CustomOverhead_UsesProvidedValue()
    {
        var result = _estimator.Estimate("hello", baseOverhead: 100);
        // 5/4 + 100 = 101
        result.ShouldBe(101);
    }

    [Fact]
    public void Estimate_ChineseHigherThanAscii_ForSameLength()
    {
        var ascii = "abcd"; // 4 chars → 4/4 + 500 = 501
        var chinese = "你好世界"; // 4 chars → 32/4 + 500 = 508

        var asciiResult = _estimator.Estimate(ascii);
        var chineseResult = _estimator.Estimate(chinese);

        chineseResult.ShouldBeGreaterThan(asciiResult);
    }

    [Fact]
    public void Estimate_LongMessage_ScalesLinearly()
    {
        var shortMsg = new string('a', 100);
        var longMsg = new string('a', 1000);

        var shortResult = _estimator.Estimate(shortMsg);
        var longResult = _estimator.Estimate(longMsg);

        // Long message should have ~9x more token estimate (before overhead)
        (longResult - 500).ShouldBeGreaterThan((shortResult - 500) * 8);
    }
}
