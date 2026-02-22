
namespace Tnzi.Extensions;

/// <summary>
/// DateTimeRange 扩展方法
/// </summary>
public static class DateTimeRangeExtensions
{
    /// <summary>
    /// 将日期时间范围转换为 UTC 时间范围
    /// 用于查询时确保与数据库中的 UTC 时间正确比较
    /// </summary>
    /// <param name="range">日期时间范围（通常是本地时间）</param>
    /// <returns>UTC 时间范围</returns>
    /// <example>
    /// <code>
    /// var range = DateTimeRange.ThisMonth;
    /// var utcRange = range.ToUtc(); // 转换为 UTC 时间范围
    /// var queryable = dbContext.Users
    ///     .Where(u => u.CreationTime >= utcRange.StartTime && u.CreationTime <= utcRange.EndTime);
    /// </code>
    /// </example>
    public static DateTimeRange ToUtc(this DateTimeRange range)
    {
        Check.NotNull(range);

        return new DateTimeRange(
            range.StartTime.ToUtc(),
            range.EndTime.ToUtc()
        );
    }

    /// <summary>
    /// 创建 UTC 日期范围（用于查询某一天的数据）
    /// 将本地日期转换为 UTC 日期范围（00:00:00 到 23:59:59.999）
    /// </summary>
    /// <param name="localDate">本地日期（不含时间部分）</param>
    /// <returns>UTC 时间范围</returns>
    /// <example>
    /// <code>
    /// var localDate = new DateTime(2025, 12, 14); // 本地日期
    /// var utcRange = DateTimeRangeExtensions.CreateUtcDateRange(localDate);
    /// // 如果本地时区是 UTC+8，则：
    /// // StartTime: 2025-12-13 16:00:00 UTC (对应本地 2025-12-14 00:00:00)
    /// // EndTime: 2025-12-14 15:59:59.999 UTC (对应本地 2025-12-14 23:59:59.999)
    /// var queryable = dbContext.Users
    ///     .Where(u => u.CreationTime >= utcRange.StartTime && u.CreationTime <= utcRange.EndTime);
    /// </code>
    /// </example>
    public static DateTimeRange CreateUtcDateRange(DateTime localDate)
    {
        // 本地日期的开始时间（00:00:00）
        var startTime = localDate.Date.ToUtc();

        // 本地日期的结束时间（23:59:59.999）
        var endTime = localDate.Date.AddDays(1).AddTicks(-1).ToUtc();

        return new DateTimeRange(startTime, endTime);
    }

    /// <summary>
    /// 创建 UTC 日期范围（用于查询某一天的数据，可空版本）
    /// </summary>
    /// <param name="localDate">本地日期（不含时间部分）</param>
    /// <returns>UTC 时间范围，如果输入为 null 则返回 null</returns>
    public static DateTimeRange? CreateUtcDateRange(DateTime? localDate)
    {
        return localDate.HasValue ? CreateUtcDateRange(localDate.Value) : null;
    }
}