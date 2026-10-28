namespace Tnzi.Finance.Banking.Services.Internal;

/// <summary>
/// OFX 对账单解析器（宽松处理 OFX 1.x SGML 与 2.x XML）
/// </summary>
/// <remarks>
/// 纯函数、无状态。两种版本的差异（1.x 叶子标签无闭合、2.x 完整 XML）经统一的
/// "读到下一个 &lt; 为止" 的宽松取值消化：<c>&lt;STMTTRN&gt;...&lt;/STMTTRN&gt;</c>
/// 分块（两版本聚合标签皆有闭合），块内叶子标签取值到下一个尖括号。
/// 提取 STMTTRN（DTPOSTED/TRNAMT/FITID/NAME/MEMO/CHECKNUM/REFNUM）与 LEDGERBAL/BANKTRANLIST。
/// </remarks>
internal static class OfxStatementParser
{
    private const string StmtTrnOpen = "<STMTTRN>";
    private const string StmtTrnClose = "</STMTTRN>";

    /// <summary>
    /// 解析 OFX 文本；无法识别为 OFX 时抛 <see cref="BusinessException"/>（400）。
    /// </summary>
    public static BankStatementParseResult Parse(string content)
    {
        Check.NotNullOrWhiteSpace(content);
        if (content.IndexOf("<OFX>", StringComparison.OrdinalIgnoreCase) < 0 &&
            content.IndexOf("OFXHEADER", StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new BusinessException("The file is not a recognizable OFX statement.");
        }

        // ACCTID 取自 BANKACCTFROM（银行账户）或 CCACCTFROM（信用卡账户）块，供导入时与目标账户档案交叉校验
        var acctFrom = ReadFirstBlock(content, "BANKACCTFROM") ?? ReadFirstBlock(content, "CCACCTFROM");
        var result = new BankStatementParseResult
        {
            Currency = ReadTag(content, "CURDEF"),
            StatementAccountId = ReadTag(acctFrom, "ACCTID"),
            LedgerBalance = ParseDecimal(ReadTag(ReadFirstBlock(content, "LEDGERBAL"), "BALAMT")),
            PeriodFrom = ParseOfxDate(ReadTag(content, "DTSTART")),
            PeriodTo = ParseOfxDate(ReadTag(content, "DTEND"))
        };

        foreach (var block in StmtTrnBlocks(content))
        {
            var date = ParseOfxDate(ReadTag(block, "DTPOSTED"));
            var amount = ParseDecimal(ReadTag(block, "TRNAMT"));
            if (date == null || amount == null)
                continue;

            var name = ReadTag(block, "NAME");
            var memo = ReadTag(block, "MEMO");
            var reference = ReadTag(block, "CHECKNUM") ?? ReadTag(block, "REFNUM");

            result.Transactions.Add(new ParsedBankTransaction(
                PostedDate: date.Value,
                Amount: amount.Value,
                Currency: result.Currency,
                ExternalId: ReadTag(block, "FITID"),
                Description: memo ?? name,
                Payee: name,
                Reference: reference));
        }

        return result;
    }

    /// <summary>
    /// 依次取出每个 <c>&lt;STMTTRN&gt;</c> 块的内容（开标签到其后最近的闭合标签，不分大小写）。
    /// </summary>
    /// <remarks>
    /// ★ 线性扫描而不是惰性正则 <c>&lt;STMTTRN&gt;(.*?)&lt;/STMTTRN&gt;</c>：正则对每个没有闭合的开标签都要
    /// 扫到输入末尾才失败，耗时是「开标签数 × 输入长度」，一份由开标签堆成的 20 MB 文件能把请求线程压上几十分钟。
    /// 这里遇到第一个找不到闭合的开标签就停：它后面的开标签也不可能再有闭合，所以结果与正则逐字相同。
    /// </remarks>
    private static IEnumerable<string> StmtTrnBlocks(string content)
    {
        var cursor = 0;
        while (true)
        {
            var open = content.IndexOf(StmtTrnOpen, cursor, StringComparison.OrdinalIgnoreCase);
            if (open < 0)
                yield break;
            var start = open + StmtTrnOpen.Length;
            var close = content.IndexOf(StmtTrnClose, start, StringComparison.OrdinalIgnoreCase);
            if (close < 0)
                yield break;
            yield return content[start..close];
            cursor = close + StmtTrnClose.Length;
        }
    }

    /// <summary>读取叶子标签的值（值 = 标签后到下一个 '&lt;' 为止，实体解码 + trim）</summary>
    private static string? ReadTag(string? source, string tag)
    {
        if (string.IsNullOrEmpty(source))
            return null;

        var open = $"<{tag}>";
        var idx = source.IndexOf(open, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;

        var start = idx + open.Length;
        var end = source.IndexOf('<', start);
        if (end < 0)
            end = source.Length;

        var value = source[start..end].Trim();
        return string.IsNullOrEmpty(value) ? null : Decode(value);
    }

    /// <summary>截取第一个 &lt;tag&gt;...&lt;/tag&gt; 聚合块（宽松：无闭合时取到文本末尾）</summary>
    private static string? ReadFirstBlock(string source, string tag)
    {
        var open = $"<{tag}>";
        var idx = source.IndexOf(open, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;
        var start = idx + open.Length;
        var close = source.IndexOf($"</{tag}>", start, StringComparison.OrdinalIgnoreCase);
        return close < 0 ? source[start..] : source[start..close];
    }

    private static decimal? ParseDecimal(string? value)
        => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;

    /// <summary>OFX 日期：YYYYMMDD[HHMMSS][.XXX][gmt]，取前 8 位</summary>
    private static DateTime? ParseOfxDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 8)
            return null;
        var datePart = value[..8];
        return DateTime.TryParseExact(datePart, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToUtcDate()
            : null;
    }

    private static string Decode(string value)
        => value.Replace("&amp;", "&", StringComparison.Ordinal)
                .Replace("&lt;", "<", StringComparison.Ordinal)
                .Replace("&gt;", ">", StringComparison.Ordinal)
                .Replace("&apos;", "'", StringComparison.Ordinal)
                .Replace("&quot;", "\"", StringComparison.Ordinal);
}
