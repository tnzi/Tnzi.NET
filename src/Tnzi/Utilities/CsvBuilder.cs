namespace Tnzi.Utilities;

/// <summary>
/// CSV 构建器：统一的单元格转义(RFC 4180 引号转义 + 公式注入防护)与 invariant culture 类型化格式输出
/// </summary>
/// <remarks>
/// 公式注入防护：以 = + - @ 或制表符开头的字符串单元格前置单引号,防止 Excel/Google Sheets
/// 把导出的用户可控内容当公式执行。数值/日期由类型化分支以 invariant culture 输出,不经公式转义。
/// 所有框架模块的 CSV 导出 MUST 经此类(或 <see cref="EscapeCell"/>)输出,禁止手写转义。
/// </remarks>
[StableApi(Since = "0.1.0")]
public class CsvBuilder
{
    private readonly StringBuilder _sb = new();
    private readonly string _dateTimeFormat;

    /// <summary>
    /// 初始化 CSV 构建器
    /// </summary>
    /// <param name="dateTimeFormat">DateTime/DateTimeOffset 单元格的输出格式,默认 ISO 8601 往返格式 "o"</param>
    public CsvBuilder(string dateTimeFormat = "o")
    {
        _dateTimeFormat = Check.NotNullOrWhiteSpace(dateTimeFormat);
    }

    /// <summary>
    /// 追加一行(逐单元格自动转义与格式化)
    /// </summary>
    /// <param name="cells">单元格值;null 输出为空单元格,string 经转义,decimal/DateTime/DateTimeOffset 类型化输出,其余 Convert.ToString</param>
    public CsvBuilder AppendRow(params object?[] cells)
    {
        Check.NotNull(cells);

        for (var i = 0; i < cells.Length; i++)
        {
            if (i > 0)
                _sb.Append(',');
            _sb.Append(FormatCell(cells[i]));
        }

        _sb.AppendLine();
        return this;
    }

    /// <summary>
    /// 输出完整 CSV 内容
    /// </summary>
    public override string ToString() => _sb.ToString();

    /// <summary>
    /// 转义单个字符串单元格(公式注入防护 + RFC 4180 引号转义),供自定义拼装场景复用
    /// </summary>
    public static string EscapeCell(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        // 公式注入转义：前置单引号使电子表格按文本解析（含回车 \r，OWASP 触发集）
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";

        return value;
    }

    private string FormatCell(object? cell) => cell switch
    {
        null => string.Empty,
        string s => EscapeCell(s),
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString(_dateTimeFormat, CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString(_dateTimeFormat, CultureInfo.InvariantCulture),
        // 数值类型走 invariant 输出，不经公式转义：否则负数（如 int -42）以 '-' 开头会被误加前置单引号变成文本
        byte or sbyte or short or ushort or int or uint or long or ulong or double or float
            => Convert.ToString(cell, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => EscapeCell(Convert.ToString(cell, CultureInfo.InvariantCulture))
    };
}
