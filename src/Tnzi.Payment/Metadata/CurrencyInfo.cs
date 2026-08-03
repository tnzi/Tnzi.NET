namespace Tnzi.Payment.Metadata;

/// <summary>
/// 币种最小单位换算。
/// 渠道 API（Stripe/PayPal 等）以“最小货币单位”收发金额，而不同币种的小数位并不都是 2：
/// JPY/KRW 等零小数币种按 1 计，BHD/KWD 等三小数币种按 1000 计。
/// 统一走这里换算，避免 <c>amount * 100</c> 在零小数币种上多收 100 倍。
/// </summary>
public static class CurrencyInfo
{
    /// <summary>
    /// 零小数币种（ISO 4217 exponent = 0）
    /// </summary>
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA",
        "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF"
    };

    /// <summary>
    /// 三小数币种（ISO 4217 exponent = 3）
    /// </summary>
    private static readonly HashSet<string> ThreeDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND"
    };

    /// <summary>
    /// 获取币种小数位（未知币种按 2 位处理）
    /// </summary>
    public static int GetDecimalPlaces(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            return 2;

        var code = currency.Trim();

        if (ZeroDecimalCurrencies.Contains(code))
            return 0;

        if (ThreeDecimalCurrencies.Contains(code))
            return 3;

        return 2;
    }

    /// <summary>
    /// 金额转最小货币单位。
    /// 金额列精度是 19,4，直接强转会把不足一个最小单位的部分截掉（少收/少退），故先四舍五入。
    /// </summary>
    public static long ToMinorUnits(decimal amount, string? currency)
    {
        var factor = GetFactor(currency);
        return (long)Math.Round(amount * factor, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// 最小货币单位还原为金额
    /// </summary>
    public static decimal FromMinorUnits(long minorUnits, string? currency)
    {
        return minorUnits / GetFactor(currency);
    }

    /// <summary>
    /// 按币种小数位对金额取整（用于折扣/按比例计费等中间结果落库前的归一）
    /// </summary>
    public static decimal Round(decimal amount, string? currency)
    {
        return Math.Round(amount, GetDecimalPlaces(currency), MidpointRounding.AwayFromZero);
    }

    private static decimal GetFactor(string? currency)
    {
        return GetDecimalPlaces(currency) switch
        {
            0 => 1m,
            3 => 1000m,
            _ => 100m
        };
    }
}
