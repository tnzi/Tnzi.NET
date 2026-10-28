namespace Tnzi.AI.Tests.Helpers;

public class StringTruncatorTests
{
    [Fact]
    public void Truncate_NullInput_ReturnsEmptyString()
    {
        StringTruncator.Truncate(null, 10).ShouldBe(string.Empty);
    }

    [Fact]
    public void Truncate_EmptyInput_ReturnsEmptyString()
    {
        StringTruncator.Truncate(string.Empty, 10).ShouldBe(string.Empty);
    }

    [Fact]
    public void Truncate_ShorterThanMax_ReturnsInputUnchanged()
    {
        StringTruncator.Truncate("hello", 10).ShouldBe("hello");
    }

    [Fact]
    public void Truncate_ExactlyMaxLength_ReturnsInputUnchanged()
    {
        StringTruncator.Truncate("0123456789", 10).ShouldBe("0123456789");
    }

    [Fact]
    public void Truncate_LongerThanMax_TruncatesAndAppendsEllipsis()
    {
        StringTruncator.Truncate("0123456789abcdef", 10).ShouldBe("0123456789...");
    }

    [Fact]
    public void Truncate_ZeroMax_ReturnsEllipsisOnly()
    {
        StringTruncator.Truncate("anything", 0).ShouldBe("...");
    }

    /// <summary>
    /// 截断点落在代理对中间时整个字符一起舍掉：摘要写进运行表，孤立的高代理项会让严格 UTF-8 的驱动拒绝写入。
    /// </summary>
    [Fact]
    public void Truncate_CutInsideASurrogatePair_DropsTheWholeCharacter()
    {
        // 第 10 个码元是 emoji 的前半个
        var result = StringTruncator.Truncate("012345678\U0001F600tail", 10);

        result.ShouldBe("012345678...");
        result.ShouldNotContain('\uD83D');
    }

    [Fact]
    public void Truncate_PreservesShortUnicodeInput()
    {
        StringTruncator.Truncate("你好世界", 10).ShouldBe("你好世界");
    }
}
