namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 工具输出落库前的截断（<see cref="CliRunExecutor.Truncate"/>）：正文不超上限，截断点不落在代理对中间。
/// </summary>
/// <remarks>
/// 输出逐条写进运行消息表。孤立的高代理项会让严格 UTF-8 的驱动（Npgsql）拒绝这次写入，
/// 而触发它只需要一段在截断边界处带 emoji 的工具输出；SQLite 不检查，所以这里直接看截出来的字符串。
/// </remarks>
public class CliToolOutputTruncationTests
{
    [Fact]
    public void Truncate_CutInsideASurrogatePair_DropsTheWholeCharacterAndCountsIt()
    {
        // 第 10 个码元是 emoji 的前半个；共 15 个码元
        var output = new string('a', 9) + "\U0001F600tail";

        var truncated = CliRunExecutor.Truncate(output, 10);

        truncated.ShouldBe(new string('a', 9) + "\n… [truncated, 6 more characters]");
    }

    [Fact]
    public void Truncate_LeavesNullAndShortOutputAlone()
    {
        CliRunExecutor.Truncate(null, 10).ShouldBeNull();
        CliRunExecutor.Truncate("short", 10).ShouldBe("short");
    }
}
