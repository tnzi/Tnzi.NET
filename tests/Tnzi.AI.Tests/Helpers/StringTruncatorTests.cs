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

    [Fact]
    public void Truncate_PreservesShortUnicodeInput()
    {
        StringTruncator.Truncate("你好世界", 10).ShouldBe("你好世界");
    }
}
