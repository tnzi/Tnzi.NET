namespace Tnzi.Utilities;

/// <summary>
/// 时间趋势统计的分桶粒度。
/// </summary>
/// <remarks>
/// 全框架的「趋势 / 时序统计」端点共用这一套粒度语义。各模块对外的查询枚举（如
/// <c>AuditTrendGroupBy</c>、<c>AccessLogTrendInterval</c>）可保留自己的线缆形态，
/// 但**分桶与打标签必须落到 <see cref="TimeBucket"/>**，否则同一个「周」在不同模块里
/// 会是不同的七天（此前 Audit 用 ISO 周、System 用「从起始日起每 7 天」、Feature 用
/// 周日起的自然周，三种口径并存）。
/// </remarks>
public enum TrendInterval
{
    /// <summary>按自然日分桶，标签 <c>yyyy-MM-dd</c></summary>
    Daily = 0,

    /// <summary>按 ISO 8601 周分桶（周一起，首个含四天的周为第 1 周），标签 <c>yyyy-Www</c></summary>
    Weekly = 1,

    /// <summary>按自然月分桶，标签 <c>yyyy-MM</c></summary>
    Monthly = 2
}

/// <summary>
/// 时间分桶原语：把时间点归一到所属桶的起点、生成桶标签、按粒度枚举连续桶（补零用）。
/// </summary>
/// <remarks>
/// ★**周一律按 ISO 8601 口径**（周一起始 + 首个含四天的周为第 1 周），标签用
/// <see cref="ISOWeek.GetYear(System.DateTime)"/> 而非日历年 —— 二者在跨年周并不相等：2027-01-01 属于
/// **2026 年的第 53 周**，用 <c>date.Year</c> 拼标签会得到 "2027-W53"（该年根本没有第 53 周），
/// 同一个周的头尾几天还会被拆进两个不同标签。这是收口前 Audit 与 System 两处各自存在的缺陷。
///
/// ★**所有方法保留输入的 <see cref="DateTimeKind"/>**。框架持久化层是 UTC 的，若在这里
/// 用 <c>new DateTime(y, m, 1)</c> 造出 <see cref="DateTimeKind.Unspecified"/>，
/// 拿去当查询参数会被 Npgsql 拒绝写入 timestamptz。
/// </remarks>
public static class TimeBucket
{
    /// <summary>
    /// 把时间点归一到它所属桶的起点（当日 00:00 / 本 ISO 周周一 00:00 / 当月 1 日 00:00）。
    /// </summary>
    public static DateTime Start(DateTime value, TrendInterval interval) => interval switch
    {
        TrendInterval.Weekly => value.Date.AddDays(-DaysSinceMonday(value)),
        TrendInterval.Monthly => new DateTime(value.Year, value.Month, 1, 0, 0, 0, value.Kind),
        _ => value.Date
    };

    /// <summary>
    /// 下一个桶的起点。传入的应当是 <see cref="Start"/> 的产物。
    /// </summary>
    public static DateTime Next(DateTime bucketStart, TrendInterval interval) => interval switch
    {
        TrendInterval.Weekly => bucketStart.AddDays(7),
        TrendInterval.Monthly => bucketStart.AddMonths(1),
        _ => bucketStart.AddDays(1)
    };

    /// <summary>
    /// 桶标签（可直接作为分组键与 DTO 的 <c>Period</c> 字段）。
    /// 同一个桶内的任意时间点产出同一个标签；按字典序排序即等于按时间排序。
    /// </summary>
    public static string Label(DateTime value, TrendInterval interval) => interval switch
    {
        TrendInterval.Weekly =>
            string.Create(CultureInfo.InvariantCulture, $"{ISOWeek.GetYear(value):D4}-W{ISOWeek.GetWeekOfYear(value):D2}"),
        TrendInterval.Monthly => value.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        _ => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
    };

    /// <summary>
    /// 枚举 <paramref name="from"/> 到 <paramref name="to"/> 覆盖到的桶起点，用于给稀疏的聚合结果
    /// 补零 —— 没有数据的那天/那周在图表上应当是 0，而不是断点。
    /// </summary>
    /// <param name="from">区间起点（会被 <see cref="Start"/> 归一到所属桶）</param>
    /// <param name="to">区间终点</param>
    /// <param name="interval">分桶粒度</param>
    /// <param name="endInclusive">
    /// <c>true</c>（默认）：闭区间，**含 <paramref name="to"/> 所在的那个桶**。
    /// <c>false</c>：半开区间 <c>[from, to)</c>，只产出起点严格早于 <paramref name="to"/> 的桶
    /// —— 调用方的日期选择器把 <paramref name="to"/> 当作「下一个区间的开始」时用这个，
    /// 否则最后会多出一个属于下一期的空桶。
    /// </param>
    /// <remarks><paramref name="to"/> 早于 <paramref name="from"/> 时返回空序列（不抛异常）。</remarks>
    public static IEnumerable<DateTime> Enumerate(DateTime from, DateTime to, TrendInterval interval, bool endInclusive = true)
    {
        if (to < from)
            yield break;

        var cursor = Start(from, interval);

        if (endInclusive)
        {
            var last = Start(to, interval);
            while (cursor <= last)
            {
                yield return cursor;
                cursor = Next(cursor, interval);
            }
        }
        else
        {
            while (cursor < to)
            {
                yield return cursor;
                cursor = Next(cursor, interval);
            }
        }
    }

    /// <summary>
    /// 距本 ISO 周周一已过的天数。方向必须是「当前星期 - 周一」；写反会把周中的日期
    /// 算到上一周的后半段。<c>DayOfWeek.Sunday == 0</c> 故 +6 再取模。
    /// </summary>
    private static int DaysSinceMonday(DateTime value) => ((int)value.DayOfWeek + 6) % 7;
}
