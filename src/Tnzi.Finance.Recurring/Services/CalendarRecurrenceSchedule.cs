namespace Tnzi.Finance.Recurring.Services;

/// <summary>
/// 公历排期（默认实现）
/// </summary>
/// <remarks>
/// 只做公历推进，不认假日也不避周末 —— 那是消费方的业务规则，塞进默认实现只会
/// 让每个部署都得先想办法把它关掉。需要的部署注册自己的
/// <see cref="IRecurrenceSchedule"/> 覆盖即可。
/// </remarks>
public class CalendarRecurrenceSchedule : IRecurrenceSchedule
{
    /// <inheritdoc />
    public DateTime First(DateTime startDate, RecurrenceFrequency frequency, int? anchorDay)
    {
        var start = startDate.ToUtcDate();
        if (anchorDay is not > 0)
            return start;

        return frequency switch
        {
            RecurrenceFrequency.Daily => start,
            RecurrenceFrequency.Weekly => AlignToWeekday(start, anchorDay.Value, forward: true),
            _ => AlignToMonthDay(start, anchorDay.Value, forward: true),
        };
    }

    /// <inheritdoc />
    public DateTime Next(DateTime after, RecurrenceFrequency frequency, int interval, int? anchorDay)
    {
        var from = after.ToUtcDate();
        var step = Math.Max(1, interval);

        return frequency switch
        {
            RecurrenceFrequency.Daily => from.AddDays(step),
            RecurrenceFrequency.Weekly => NextWeekly(from, step, anchorDay),
            RecurrenceFrequency.Monthly => NextByMonths(from, step, anchorDay),
            RecurrenceFrequency.Quarterly => NextByMonths(from, step * 3, anchorDay),
            RecurrenceFrequency.Yearly => NextByMonths(from, step * 12, anchorDay),
            _ => from.AddMonths(step),
        };
    }

    private static DateTime NextWeekly(DateTime from, int step, int? anchorDay)
    {
        var next = from.AddDays(7 * step);
        return anchorDay is > 0 and <= 7 ? AlignToWeekday(next, anchorDay.Value, forward: false) : next;
    }

    /// <summary>
    /// 按月推进。
    /// </summary>
    /// <remarks>
    /// ★锚点为 29/30/31 而目标月没有那一天时**收到月末**，不溢出到下月 1 号：
    /// "每月最后一天开票"是真实存在的约定，多走一天等于把整个账期错开。
    /// 且下一次仍从**锚点**推，不会因为二月被夹到 28 就一路 28 下去。
    /// </remarks>
    private static DateTime NextByMonths(DateTime from, int months, int? anchorDay)
    {
        var target = from.AddMonths(months);
        if (anchorDay is not > 0)
            return target;

        var day = Math.Min(anchorDay.Value, DateTime.DaysInMonth(target.Year, target.Month));
        return new DateTime(target.Year, target.Month, day, 0, 0, 0, DateTimeKind.Utc);
    }

    private static DateTime AlignToMonthDay(DateTime date, int anchorDay, bool forward)
    {
        var day = Math.Min(anchorDay, DateTime.DaysInMonth(date.Year, date.Month));
        var aligned = new DateTime(date.Year, date.Month, day, 0, 0, 0, DateTimeKind.Utc);
        if (forward && aligned < date)
        {
            var nextMonth = date.AddMonths(1);
            day = Math.Min(anchorDay, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month));
            aligned = new DateTime(nextMonth.Year, nextMonth.Month, day, 0, 0, 0, DateTimeKind.Utc);
        }
        return aligned;
    }

    /// <summary>星期锚点：1=周一 … 7=周日（ISO 口径，与 .NET 的 Sunday=0 刻意不同）。</summary>
    private static DateTime AlignToWeekday(DateTime date, int anchorDay, bool forward)
    {
        var current = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
        var delta = anchorDay - current;
        if (forward && delta < 0)
            delta += 7;
        else if (!forward && delta > 3)
            delta -= 7;
        else if (!forward && delta < -3)
            delta += 7;
        return date.AddDays(delta);
    }
}
