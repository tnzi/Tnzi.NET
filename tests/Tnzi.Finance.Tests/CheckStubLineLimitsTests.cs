namespace Tnzi.Finance.Tests;

/// <summary>
/// 存根附加行的归一化边界（<see cref="CheckStubLineLimits"/>）
/// </summary>
/// <remarks>
/// 存根是有限的物理空间：条数与长度失控会把票面顶变形，而那种失效在屏幕预览上还看得见，
/// 打到纸上就是一叠废票。这里锁的是「越界一律归一化、绝不拒绝」——附加行是装饰性的，
/// 为它把整批付款打不出来，代价方向是错的。
/// <para>
/// ★ 本组是纯函数断言，单独全绿<b>证明不了机制已接上</b>；走真实入口的在
/// <c>Integration/CheckStubLineTests</c>。
/// </para>
/// </remarks>
public class CheckStubLineLimitsTests
{
    [Fact]
    public void NullOrEmpty_YieldsAnEmptyList_NeverNull()
    {
        CheckStubLineLimits.Normalize(null).ShouldBeEmpty();
        CheckStubLineLimits.Normalize([]).ShouldBeEmpty();
    }

    [Fact]
    public void LinesAreTrimmedAndKeptInOrder()
    {
        var lines = CheckStubLineLimits.Normalize(
        [
            new CheckStubLine("  File No.  ", "  2026-0042  "),
            new CheckStubLine("Client", "Northwind Ltd.")
        ]);

        lines.Count.ShouldBe(2);
        lines[0].ShouldBe(new CheckStubLine("File No.", "2026-0042"));
        lines[1].ShouldBe(new CheckStubLine("Client", "Northwind Ltd."));
    }

    [Fact]
    public void BlankLabel_IsDropped_ButABlankValueIsKept()
    {
        var lines = CheckStubLineLimits.Normalize(
        [
            new CheckStubLine("   ", "orphaned value"),
            new CheckStubLine("", null),
            // 只有标签、没有值 = 一行分节标题，是有意义的
            new CheckStubLine("Disbursements", null),
            new CheckStubLine("Filing fee", "   ")
        ]);

        lines.Count.ShouldBe(2);
        lines[0].Label.ShouldBe("Disbursements");
        lines[0].Value.ShouldBeNull();
        // 全空白的值归一成 null，模板于是排出一个真正的空单元格而不是一串空格
        lines[1].Value.ShouldBeNull();
    }

    [Fact]
    public void TooManyLines_AreCutToTheLimit_NotRejected()
    {
        var raw = Enumerable.Range(1, CheckStubLineLimits.MaxLines + 5)
            .Select(i => new CheckStubLine($"Label {i}", $"Value {i}"))
            .ToList();

        var lines = CheckStubLineLimits.Normalize(raw);

        lines.Count.ShouldBe(CheckStubLineLimits.MaxLines);
        // 保留的是最前面那些：消费应用把最重要的放在前面是唯一说得通的读法
        lines[0].Label.ShouldBe("Label 1");
        lines[^1].Label.ShouldBe($"Label {CheckStubLineLimits.MaxLines}");
    }

    [Fact]
    public void OverlongTextIsTruncated_BecauseTheOpeningIsStillUseful()
    {
        var lines = CheckStubLineLimits.Normalize(
        [
            new CheckStubLine(new string('L', CheckStubLineLimits.MaxLabelLength + 20),
                new string('V', CheckStubLineLimits.MaxValueLength + 200))
        ]);

        lines.Single().Label.Length.ShouldBe(CheckStubLineLimits.MaxLabelLength);
        lines.Single().Value!.Length.ShouldBe(CheckStubLineLimits.MaxValueLength);
    }

    [Fact]
    public void Truncation_NeverLeavesHalfASurrogatePair()
    {
        // 截断点正好落在代理对中间：留下孤立的高代理项就是一个非法 UTF-16 串，
        // 写进 HTML 得到一个替换字符，交给 PdfSharp 的字形查表则直接抛。
        var label = new string('a', CheckStubLineLimits.MaxLabelLength - 1) + "📁";

        var line = CheckStubLineLimits.Normalize([new CheckStubLine(label, null)]).Single();

        line.Label.Length.ShouldBe(CheckStubLineLimits.MaxLabelLength - 1);
        char.IsHighSurrogate(line.Label[^1]).ShouldBeFalse();
    }

    [Fact]
    public void TheInputListIsNotMutated()
    {
        var raw = new List<CheckStubLine> { new("  File No.  ", "  x  ") };

        CheckStubLineLimits.Normalize(raw);

        raw.Single().Label.ShouldBe("  File No.  ");
    }
}
