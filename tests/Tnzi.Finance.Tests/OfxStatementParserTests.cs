using System.Diagnostics;
using System.Text;

namespace Tnzi.Finance.Tests;

/// <summary>
/// OFX 交易块的切分：语义与「最近的闭合标签」一致，耗时与输入长度成线性。
/// </summary>
/// <remarks>
/// 切分原先是惰性正则 <c>&lt;STMTTRN&gt;(.*?)&lt;/STMTTRN&gt;</c>：每个没有闭合的开标签都要扫到输入末尾才失败，
/// 耗时是「开标签数 × 输入长度」。导入端点收 20 MB 文件并在请求线程上同步解析，一份由开标签重复堆成、
/// 不带闭合的文件就能把一个核压上几十分钟。线性扫描遇到第一个找不到闭合的开标签就停：
/// 它后面的开标签也不可能再有闭合，所以结果与正则逐字相同。
/// </remarks>
public class OfxStatementParserTests
{
    private static string Block(string fitId, string amount, string date = "20260310")
        => $"<STMTTRN><TRNTYPE>DEBIT<DTPOSTED>{date}<TRNAMT>{amount}<FITID>{fitId}<NAME>Payee {fitId}</STMTTRN>";

    private static string Statement(params string[] blocks)
        => "<OFX><CURDEF>USD<BANKTRANLIST>" + string.Concat(blocks) + "</BANKTRANLIST></OFX>";

    [Fact]
    public void Closed_blocks_are_all_parsed_in_order()
    {
        var result = OfxStatementParser.Parse(Statement(Block("A", "-1.00"), Block("B", "-2.00"), Block("C", "3.00")));

        result.Transactions.Select(t => t.ExternalId).ShouldBe(["A", "B", "C"]);
        result.Transactions.Select(t => t.Amount).ShouldBe([-1.00m, -2.00m, 3.00m]);
    }

    [Fact]
    public void Tags_match_case_insensitively()
    {
        var content = "<OFX><CURDEF>USD<stmttrn><DTPOSTED>20260310<TRNAMT>-4.50<FITID>X</StmtTrn></OFX>";

        var result = OfxStatementParser.Parse(content);

        result.Transactions.Single().ExternalId.ShouldBe("X");
    }

    /// <summary>闭合块之后跟一个没闭合的（截断的文件）：前面的照常解析，没闭合的那个不算。</summary>
    [Fact]
    public void A_trailing_unclosed_block_is_ignored()
    {
        var content = Statement(Block("A", "-1.00")) + "<STMTTRN><DTPOSTED>20260311<TRNAMT>-9.00<FITID>TRUNCATED";

        var result = OfxStatementParser.Parse(content);

        result.Transactions.Select(t => t.ExternalId).ShouldBe(["A"]);
    }

    /// <summary>
    /// 两个开标签共用一个闭合：块从第一个开标签取到最近的闭合（与原正则相同）。
    /// </summary>
    [Fact]
    public void A_block_runs_from_its_opener_to_the_nearest_closer()
    {
        var content = "<OFX><CURDEF>USD<STMTTRN><DTPOSTED>20260310<TRNAMT>-1.00<FITID>FIRST<STMTTRN><FITID>SECOND</STMTTRN>"
            + Block("C", "2.00") + "</OFX>";

        var result = OfxStatementParser.Parse(content);

        result.Transactions.Select(t => t.ExternalId).ShouldBe(["FIRST", "C"]);
    }

    /// <summary>
    /// ★ 大量没有闭合的开标签：线性完成，不交易。
    /// </summary>
    [Fact]
    public void Many_unclosed_openers_are_parsed_in_linear_time()
    {
        var sb = new StringBuilder("<OFX><CURDEF>USD");
        for (var i = 0; i < 200_000; i++)
            sb.Append("<STMTTRN>");
        var content = sb.ToString();

        var watch = Stopwatch.StartNew();
        var result = OfxStatementParser.Parse(content);
        watch.Stop();

        result.Transactions.ShouldBeEmpty();
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }
}
