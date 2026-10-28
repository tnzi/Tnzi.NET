namespace Tnzi.Payment.Subscriptions.Services;

/// <summary>
/// 超额存储费的算法：一个计费周期里每日样本的平均值，减去计划包含的存储（不低于 0），按 GB-月计价。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>缺的天不进平均</b>：实例停了几天，那几天既不按 0 算（会压低账单）、也不被补成别的值去抬高它，
/// 平均只取这个周期里真的采到的那些天。等价于「缺的天按已采到的那些天的平均计」。
/// </para>
/// <para>
/// 1 GB = 2^30 字节（<see cref="BytesPerGb"/>），与对象存储计费的 GB、框架界面的文件大小显示同一个单位。
/// 一个 GB-月 = 1 GB 存放一个月：按月 / 按年的周期按日历月数计（不足一整期时按天数比例），
/// 按天 / 按周的周期按 30 天一个月计。数量保留 <see cref="QuantityDecimals"/> 位，金额按币种小数位取整，
/// 且由取整后的数量乘单价得来，所以票据上「数量 × 单价」与金额对得上。
/// </para>
/// </remarks>
public static class StorageOverageCalculator
{
    /// <summary>1 GB 的字节数（2^30）。</summary>
    public const long BytesPerGb = 1L << 30;

    /// <summary>GB-月数量保留的小数位。</summary>
    public const int QuantityDecimals = 3;

    /// <summary>按天 / 按周计费时一个月按多少天算。</summary>
    public const int DaysPerMonth = 30;

    /// <summary>
    /// 按样本算一期的超额。没有样本返回 <see langword="null"/>（量不出来，不是 0）。
    /// </summary>
    /// <param name="dailySamples">这一期里采到的每日字节数。</param>
    /// <param name="includedBytes">计划包含的存储。</param>
    /// <param name="unitPrice">每 GB-月单价（0 = 只计量不收费）。</param>
    /// <param name="months">这一期折合多少个月（<see cref="MonthsIn"/>）。</param>
    /// <param name="currency">币种（决定金额小数位）。</param>
    public static StorageOverageQuote? Quote(IReadOnlyCollection<long> dailySamples, long includedBytes, decimal unitPrice, decimal months, string currency)
    {
        Check.NotNull(dailySamples);
        if (dailySamples.Count == 0)
            return null;

        var average = (long)Math.Round(dailySamples.Average(b => (decimal)b), MidpointRounding.AwayFromZero);
        var over = Math.Max(0L, average - Math.Max(0L, includedBytes));
        var quantity = Math.Round((decimal)over / BytesPerGb * Math.Max(0m, months), QuantityDecimals, MidpointRounding.AwayFromZero);
        var price = Math.Max(0m, unitPrice);

        return new StorageOverageQuote(
            dailySamples.Count,
            average,
            includedBytes,
            quantity,
            price,
            CurrencyInfo.Round(quantity * price, currency));
    }

    /// <summary>
    /// <paramref name="start"/> 到 <paramref name="end"/> 这一期折合多少个月。
    /// </summary>
    /// <param name="cycleType">计划的计费周期。</param>
    /// <param name="cycleValue">周期长度。</param>
    /// <param name="start">这一期的开始（可能晚于一整期的起点，例如订阅在期中开始）。</param>
    /// <param name="end">这一期的结束（计费日）。</param>
    public static decimal MonthsIn(BillingCycleType cycleType, int cycleValue, DateTime start, DateTime end)
    {
        if (end <= start)
            return 0m;

        var length = Math.Max(1, cycleValue);
        var calendarMonths = cycleType switch
        {
            BillingCycleType.Month => length,
            BillingCycleType.Year => 12 * length,
            _ => 0
        };

        if (calendarMonths == 0)
            return (decimal)(end - start).TotalDays / DaysPerMonth;

        var fullStart = end.AddMonths(-calendarMonths);
        var fraction = Math.Min(1m, (decimal)(end - start).Ticks / (end - fullStart).Ticks);
        return calendarMonths * fraction;
    }

    /// <summary>
    /// 计费日 <paramref name="end"/> 结束的那一期从哪天开始：往回一个计费周期，且不早于 <paramref name="notBefore"/>（订阅开始或试用结束）。
    /// </summary>
    public static DateTime PeriodStart(BillingCycleType cycleType, int cycleValue, DateTime end, DateTime notBefore)
    {
        var length = Math.Max(1, cycleValue);
        var start = cycleType switch
        {
            BillingCycleType.Day => end.AddDays(-length),
            BillingCycleType.Week => end.AddDays(-7 * length),
            BillingCycleType.Year => end.AddYears(-length),
            _ => end.AddMonths(-length)
        };
        return start < notBefore ? notBefore : start;
    }
}

/// <summary>
/// 一期的超额存储费。
/// </summary>
/// <param name="SampleDays">这一期采到样本的天数。</param>
/// <param name="AverageBytes">样本平均字节数。</param>
/// <param name="IncludedBytes">计划包含的字节数。</param>
/// <param name="Quantity">超出部分折合的 GB-月。</param>
/// <param name="UnitPrice">每 GB-月单价。</param>
/// <param name="Amount">金额（税前）。</param>
public sealed record StorageOverageQuote(int SampleDays, long AverageBytes, long IncludedBytes, decimal Quantity, decimal UnitPrice, decimal Amount);
