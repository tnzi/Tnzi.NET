using Tnzi.Signing.Metadata;
using Tnzi.Signing.Services.Internal;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 收进列宽或文件名长度的文本：截断点落在代理对中间时整个字符一起舍掉。
/// </summary>
/// <remarks>
/// 签署人的 User-Agent 与由标题派生的文件名（渲染稿、成品、完成证书）都会落库。孤立的高代理项会让严格 UTF-8 的驱动
/// （Npgsql）拒绝写入：标题里第 120 个码元处的一个 emoji 就让密封失败。SQLite 不检查，所以这里直接断言截出来的字符串。
/// </remarks>
public class SigningTextTruncationTests
{
    /// <summary>第 <paramref name="width"/> 个码元是 emoji 的前半个。</summary>
    private static string EmojiAtTheCut(int width) => new string('a', width - 1) + "\U0001F600tail";

    [Fact]
    public void Fit_CutInsideASurrogatePair_DropsTheWholeCharacter()
    {
        SigningLimits.Fit(EmojiAtTheCut(10), 10).ShouldBe(new string('a', 9));
        SigningLimits.Fit(null, 10).ShouldBeNull();
        SigningLimits.Fit(string.Empty, 10).ShouldBe(string.Empty);
    }

    [Fact]
    public void SealedFileName_TitleCutInsideASurrogatePair_DropsTheWholeCharacter()
    {
        SigningSealer.SealedFileName(EmojiAtTheCut(120)).ShouldBe(new string('a', 119) + "-signed.pdf");
    }

    [Fact]
    public void FileNameStem_ReplacesInvalidCharactersAndFallsBackForABlankTitle()
    {
        SigningLimits.FileNameStem("a/b", 120).ShouldBe("a_b");
        SigningLimits.FileNameStem("  ", 120).ShouldBe("document");
    }
}
