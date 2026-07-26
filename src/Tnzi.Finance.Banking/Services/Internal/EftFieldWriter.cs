namespace Tnzi.Finance.Banking.Services.Internal;

/// <summary>
/// 定长记录字段写入工具（EFT 文件组装）
/// </summary>
internal static class EftFieldWriter
{
    /// <summary>
    /// 左对齐文本，右补空格（超长截断）。
    /// 先剥除嵌入的控制字符（换行/回车/制表等，替换为空格）：记录以 \n 连接且严格按位解析，
    /// 数据字段（PayeeName=Vendor.Name / 解密账号明文 / OriginatorName）内的 \n/\r 会把一条定宽记录
    /// 截成两行、错位其后所有字段 → 整文件被 ODFI 拒收，而长度不变式（<see cref="Fixed"/>）测不出（\n 单字符）。
    /// </summary>
    public static string Text(string? value, int width)
    {
        var v = Sanitize(value);
        if (v.Length > width)
            v = v[..width];
        return v.PadRight(width);
    }

    /// <summary>把控制字符（含 \r\n\t 与其它不可打印字符）折叠为空格后去除首尾空白。</summary>
    private static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var buffer = new char[value.Length];
        for (var i = 0; i < value.Length; i++)
            buffer[i] = char.IsControl(value[i]) ? ' ' : value[i];
        return new string(buffer).Trim();
    }

    /// <summary>右对齐数值，左补零（超长保留低位）。</summary>
    public static string Num(long value, int width)
    {
        var s = value.ToString(CultureInfo.InvariantCulture);
        if (s.Length > width)
            s = s[^width..];
        return s.PadLeft(width, '0');
    }

    /// <summary>抽取数字并左补零到定宽（超长截断高位保留低位）。</summary>
    public static string Digits(string? value, int width)
    {
        var v = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        if (v.Length > width)
            v = v[^width..];
        return v.PadLeft(width, '0');
    }

    /// <summary>金额转分（无小数、非负）。</summary>
    public static long Cents(decimal amount)
        => (long)Math.Round(Math.Abs(amount) * 100m, MidpointRounding.AwayFromZero);

    /// <summary>空白填充。</summary>
    public static string Spaces(int width) => new(' ', width);

    /// <summary>CPA-005 儒略日期（0YYDDD，6 位）。</summary>
    public static string Julian(DateTime date) => $"0{date:yy}{date.DayOfYear:D3}";

    /// <summary>校验记录长度（组装不变量，越界抛以暴露布局错误）。</summary>
    public static string Fixed(string record, int width)
    {
        if (record.Length != width)
            throw new BusinessException($"EFT record length {record.Length} does not match the required {width}.");
        return record;
    }
}
