namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 有界的 stderr 尾部缓冲（<see cref="StderrTailBuffer"/>）：只留最后 N 个码元，从头部丢掉的那一刀不落在代理对中间。
/// </summary>
/// <remarks>
/// 尾部会接在失败原因后面落库。以孤立的低代理项开头的话，严格 UTF-8 的驱动（Npgsql）拒绝写入，
/// 这次运行为什么失败就整条丢了；SQLite 不检查，所以这里直接看尾部字符串。
/// </remarks>
public class StderrTailBufferTests
{
    [Fact]
    public void Tail_KeepsOnlyTheLastCharacters()
    {
        var buffer = new StderrTailBuffer(capacity: 4);

        buffer.AppendLine("abcdef");

        buffer.Tail().ShouldBe("def\n");
    }

    [Fact]
    public void Tail_OverflowCutInsideASurrogatePair_DropsTheWholeCharacter()
    {
        var buffer = new StderrTailBuffer(capacity: 4);

        // "a" + emoji + "bc\n" 共 6 个码元，要丢 2 个：第 2 个是 emoji 的前半个
        buffer.AppendLine("a\U0001F600bc");

        buffer.Tail().ShouldBe("bc\n");
    }
}
