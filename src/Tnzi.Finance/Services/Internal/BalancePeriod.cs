namespace Tnzi.Finance.Services.Internal;

/// <summary>
/// 月粒度期间键与月初工具（<see cref="AccountPeriodBalance.Period"/> = yyyyMM 整数）
/// </summary>
/// <remarks>
/// yyyyMM 整数编码保持时序：Y*100+M 使跨年月份（如 202512 → 202601）的整数序等于日历序，
/// 因此汇总桶的 <c>Period &gt;= from &amp;&amp; Period &lt; toExclusive</c> 范围查询按整数比较即选出连续月份
/// （不存在的 202513..202600 天然不产生桶，不影响结果）。
/// </remarks>
internal static class BalancePeriod
{
    /// <summary>日期 → yyyyMM 整数期间键</summary>
    public static int Of(DateTime date) => date.Year * 100 + date.Month;

    /// <summary>该日期是否为所在月的月初（1 号）</summary>
    public static bool IsMonthStart(DateTime date) => date.Day == 1;

    /// <summary>该日期所在月的月初（当月 1 号，UTC date-only）</summary>
    public static DateTime MonthStart(DateTime date) => new DateTime(date.Year, date.Month, 1).ToUtcDate();

    /// <summary>该日期所在月的下月初（UTC date-only）</summary>
    public static DateTime NextMonthStart(DateTime date) => MonthStart(date).AddMonths(1);
}
