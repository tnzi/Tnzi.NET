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
    /// 金额单元格：把小数规范到固定标度(默认 2 位),使补零与舍入在整列保持一致
    /// </summary>
    /// <remarks>
    /// <see cref="AppendRow"/> 的 decimal 分支按值的自然标度输出,因此 <c>1.5m</c> 会写成 "1.5" 而
    /// <c>1.50m</c> 写成 "1.50" —— 同一金额列里补零不一致。本方法按 <paramref name="decimals"/>
    /// 舍入并把标度固定下来,让整列对齐。
    /// <para>
    /// 返回值刻意仍为 <see cref="decimal"/> 而非预格式化字符串：数值分支会原样输出,负数保持
    /// "-1234.50",不会被公式注入防护加上前置单引号(字符串单元格则会)。
    /// </para>
    /// </remarks>
    /// <param name="value">金额;<c>null</c> 原样返回,输出为空单元格</param>
    /// <param name="decimals">小数位数,默认 2</param>
    public static decimal? Money(decimal? value, int decimals = 2)
    {
        Check.InRange(decimals, 0, 28);

        if (value is null)
            return null;

        // 先按 decimals 舍入(远离零,与会计口径一致),再加上一个该标度的零值把标度固定住：
        // decimal 加法结果取两操作数标度的较大者,于是 1.5m -> 1.50m,补零得以保留。
        var rounded = Math.Round(value.Value, decimals, MidpointRounding.AwayFromZero);
        var zeroAtScale = new decimal(0, 0, 0, false, (byte)decimals);
        return rounded + zeroAtScale;
    }

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
