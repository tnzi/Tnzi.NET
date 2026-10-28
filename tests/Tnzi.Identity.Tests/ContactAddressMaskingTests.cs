namespace Tnzi.Identity.Tests;

/// <summary>
/// 发码回执上的联系方式脱敏（<see cref="ContactAddressMasking"/>）。
/// </summary>
public class ContactAddressMaskingTests
{
    [Fact]
    public void MaskEmail_KeepsTheFirstCharacterAndTheDomain()
    {
        ContactAddressMasking.MaskEmail("alice@example.com").ShouldBe("a***@example.com");
        ContactAddressMasking.MaskEmail("a@example.com").ShouldBe("a***@example.com");
        ContactAddressMasking.MaskEmail("@example.com").ShouldBe("***");
    }

    /// <summary>
    /// 本地部分以 emoji 开头：首字符按码元取会露出半个代理项（回执上是一个替换字符），所以一个都不露。
    /// </summary>
    [Fact]
    public void MaskEmail_LocalPartStartingWithAnEmoji_ShowsNoLoneSurrogate()
    {
        ContactAddressMasking.MaskEmail("\U0001F600alice@example.com").ShouldBe("***@example.com");
    }
}
