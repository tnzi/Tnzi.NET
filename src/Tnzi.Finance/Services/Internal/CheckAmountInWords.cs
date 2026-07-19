namespace Tnzi.Finance.Services.Internal;

/// <summary>
/// 支票金额英文大写（法定金额栏）
/// </summary>
/// <remarks>
/// 形如 "One Thousand Two Hundred Thirty-Four and 56/100 Dollars"。
/// USD/CAD 收尾 "Dollars"，其它币种以 ISO 货币代码收尾（如 "... and 00/100 EUR"）。
/// 整数金额分位为 "00/100"。首版仅英文（多语言金额大写列为 backlog）。
/// </remarks>
internal static class CheckAmountInWords
{
    private static readonly string[] Ones =
    {
        "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine",
        "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen",
        "Seventeen", "Eighteen", "Nineteen"
    };

    private static readonly string[] Tens =
    {
        "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"
    };

    // 千进制分组标度（首版覆盖到万亿级）
    private static readonly string[] Scales = { "", "Thousand", "Million", "Billion", "Trillion" };

    /// <summary>
    /// 把金额转为英文大写字符串。
    /// </summary>
    /// <param name="amount">金额（负数取绝对值）</param>
    /// <param name="currency">ISO 货币代码（USD/CAD → "Dollars"，其它以代码收尾）</param>
    public static string Convert(decimal amount, string? currency)
    {
        // Scales 覆盖到万亿级（Trillion，千进制 5 组）；≥1e15 会索引越界，显式拒绝而非抛 IndexOutOfRange
        if (Math.Abs(amount) >= 1_000_000_000_000_000m)
            throw new BusinessException("Check amount is too large to spell out (must be below 1,000,000,000,000,000).");

        var value = Math.Abs(Math.Round(amount, 2, MidpointRounding.AwayFromZero));
        var whole = (long)Math.Floor(value);
        var cents = (int)Math.Round((value - whole) * 100m, MidpointRounding.AwayFromZero);
        if (cents == 100)
        {
            whole += 1;
            cents = 0;
        }

        var words = whole == 0 ? "Zero" : ConvertWhole(whole);
        var suffix = CurrencySuffix(currency);
        return $"{words} and {cents:00}/100 {suffix}".Trim();
    }

    private static string CurrencySuffix(string? currency)
    {
        var code = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
        return code is "USD" or "CAD" ? "Dollars" : code;
    }

    private static string ConvertWhole(long value)
    {
        // 按千进制分组，从高位到低位拼接
        var groups = new List<int>();
        while (value > 0)
        {
            groups.Add((int)(value % 1000));
            value /= 1000;
        }

        var parts = new List<string>();
        for (var i = groups.Count - 1; i >= 0; i--)
        {
            if (groups[i] == 0)
                continue;
            var group = ConvertUnderThousand(groups[i]);
            var scale = Scales[i];
            parts.Add(string.IsNullOrEmpty(scale) ? group : $"{group} {scale}");
        }

        return string.Join(" ", parts);
    }

    private static string ConvertUnderThousand(int value)
    {
        var parts = new List<string>();
        var hundreds = value / 100;
        var remainder = value % 100;

        if (hundreds > 0)
            parts.Add($"{Ones[hundreds]} Hundred");

        if (remainder > 0)
            parts.Add(ConvertUnderHundred(remainder));

        return string.Join(" ", parts);
    }

    private static string ConvertUnderHundred(int value)
    {
        if (value < 20)
            return Ones[value];

        var tens = Tens[value / 10];
        var ones = value % 10;
        return ones == 0 ? tens : $"{tens}-{Ones[ones]}";
    }
}
