
namespace Tnzi.Utilities;

/// <summary>
/// 日期时间范围
/// </summary>
public class DateTimeRange
{
    /// <summary>
    /// 初始化一个<see cref="DateTimeRange"/>类型的新实例
    /// </summary>
    public DateTimeRange()
        : this(DateTime.MinValue, DateTime.MaxValue)
    {
    }

    /// <summary>
    /// 初始化一个<see cref="DateTimeRange"/>类型的新实例
    /// </summary>
    /// <param name="startTime">开始时间</param>
    /// <param name="endTime">结束时间</param>
    public DateTimeRange(DateTime startTime, DateTime endTime)
    {
        StartTime = startTime;
        EndTime = endTime;
    }

    /// <summary>
    /// 获取或设置 开始时间
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 获取或设置 结束时间
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// 获取 时间跨度
    /// </summary>
    public TimeSpan TimeSpan => EndTime - StartTime;

    /// <summary>
    /// 获取 是否包含指定时间
    /// </summary>
    /// <param name="dateTime">要检查的时间</param>
    /// <returns>是否包含</returns>
    public bool Contains(DateTime dateTime)
    {
        return dateTime >= StartTime && dateTime <= EndTime;
    }

    /// <summary>
    /// 获取 是否与指定范围重叠
    /// </summary>
    /// <param name="range">要检查的范围</param>
    /// <returns>是否重叠</returns>
    public bool Overlaps(DateTimeRange range)
    {
        return StartTime <= range.EndTime && EndTime >= range.StartTime;
    }

    #region 静态属性（常用时间范围）

    /// <summary>
    /// 获取 昨天的时间范围
    /// </summary>
    public static DateTimeRange Yesterday
    {
        get
        {
            DateTime now = DateTime.Now;
            return new DateTimeRange(now.Date.AddDays(-1), now.Date.AddMilliseconds(-1));
        }
    }

    /// <summary>
    /// 获取 今天的时间范围
    /// </summary>
    public static DateTimeRange Today
    {
        get
        {
            DateTime now = DateTime.Now;
            return new DateTimeRange(now.Date, now.Date.AddDays(1).AddMilliseconds(-1));
        }
    }

    /// <summary>
    /// 获取 明天的时间范围
    /// </summary>
    public static DateTimeRange Tomorrow
    {
        get
        {
            DateTime now = DateTime.Now;
            return new DateTimeRange(now.Date.AddDays(1), now.Date.AddDays(2).AddMilliseconds(-1));
        }
    }

    /// <summary>
    /// 获取 本周的时间范围
    /// </summary>
    public static DateTimeRange ThisWeek
    {
        get
        {
            DateTime now = DateTime.Now;
            int daysUntilMonday = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;
            DateTime startOfWeek = now.Date.AddDays(-daysUntilMonday);
            return new DateTimeRange(startOfWeek, startOfWeek.AddDays(7).AddMilliseconds(-1));
        }
    }

    /// <summary>
    /// 获取 上周的时间范围
    /// </summary>
    public static DateTimeRange LastWeek
    {
        get
        {
            var thisWeek = ThisWeek;
            return new DateTimeRange(thisWeek.StartTime.AddDays(-7), thisWeek.StartTime.AddMilliseconds(-1));
        }
    }

    /// <summary>
    /// 获取 本月的时间范围
    /// </summary>
    public static DateTimeRange ThisMonth
    {
        get
        {
            DateTime now = DateTime.Now;
            DateTime startTime = new DateTime(now.Year, now.Month, 1);
            DateTime endTime = startTime.AddMonths(1).AddMilliseconds(-1);
            return new DateTimeRange(startTime, endTime);
        }
    }

    /// <summary>
    /// 获取 上个月的时间范围
    /// </summary>
    public static DateTimeRange LastMonth
    {
        get
        {
            DateTime now = DateTime.Now;
            DateTime startTime = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
            DateTime endTime = startTime.AddMonths(1).AddMilliseconds(-1);
            return new DateTimeRange(startTime, endTime);
        }
    }

    /// <summary>
    /// 获取 本年的时间范围
    /// </summary>
    public static DateTimeRange ThisYear
    {
        get
        {
            DateTime now = DateTime.Now;
            DateTime startTime = new DateTime(now.Year, 1, 1);
            DateTime endTime = new DateTime(now.Year + 1, 1, 1).AddMilliseconds(-1);
            return new DateTimeRange(startTime, endTime);
        }
    }

    /// <summary>
    /// 获取 过去7天的时间范围
    /// </summary>
    public static DateTimeRange Last7Days
    {
        get
        {
            DateTime now = DateTime.Now;
            return new DateTimeRange(now.AddDays(-7), now);
        }
    }

    /// <summary>
    /// 获取 过去30天的时间范围
    /// </summary>
    public static DateTimeRange Last30Days
    {
        get
        {
            DateTime now = DateTime.Now;
            return new DateTimeRange(now.AddDays(-30), now);
        }
    }

    #endregion

    /// <summary>
    /// 返回表示当前对象的字符串
    /// </summary>
    public override string ToString()
    {
        return $"[{StartTime:yyyy-MM-dd HH:mm:ss} - {EndTime:yyyy-MM-dd HH:mm:ss}]";
    }
}