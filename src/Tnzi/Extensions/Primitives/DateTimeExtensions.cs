
namespace Tnzi.Extensions;

public static class DateTimeExtensions
{
    public static DateTime ToUtc(this DateTime dateTime)
    {
        if (dateTime.Kind == DateTimeKind.Utc)
            return dateTime;

        if (dateTime.Kind == DateTimeKind.Unspecified)
            return DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);

        return dateTime.ToUniversalTime();
    }

    public static DateTime? ToUtc(this DateTime? dateTime)
    {
        return dateTime?.ToUtc();
    }

    /// <summary>
    /// 归一化为 "UTC 日期"：截断时间部分并将 Kind 置为 Utc。
    /// </summary>
    /// <remarks>
    /// 用于以 date-only 语义存入 timestamptz 类列的字段（过账日期、汇率生效日期、会计年度区间等）。
    /// PostgreSQL 的 Npgsql 拒绝 Kind=Unspecified 的 DateTime 参数写入 "timestamp with time zone"，
    /// 而 JSON 反序列化得到的日期恰为 Unspecified —— 凡 date-only 字段在持久化/查询参数前
    /// MUST 经本方法归一化，跨数据库行为才一致。
    /// </remarks>
    public static DateTime ToUtcDate(this DateTime dateTime)
        => DateTime.SpecifyKind(dateTime.Date, DateTimeKind.Utc);

    public static long ToUnixTimeSeconds(this DateTime dateTime)
    {
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return (long)(dateTime.ToUtc() - epoch).TotalSeconds;
    }

    public static long ToUnixTimeMilliseconds(this DateTime dateTime)
    {
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return (long)(dateTime.ToUtc() - epoch).TotalMilliseconds;
    }

    public static DateTime FromUnixTimeSeconds(long seconds)
    {
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return epoch.AddSeconds(seconds);
    }

    public static DateTime FromUnixTimeMilliseconds(long milliseconds)
    {
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return epoch.AddMilliseconds(milliseconds);
    }

    public static string ToIso8601(this DateTime dateTime)
    {
        return dateTime.ToUtc().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    }

    public static DateTime StartOfDay(this DateTime dateTime)
    {
        return dateTime.Date;
    }

    public static DateTime EndOfDay(this DateTime dateTime)
    {
        return dateTime.Date.AddDays(1).AddTicks(-1);
    }

    public static DateTime StartOfWeek(this DateTime dateTime, DayOfWeek startOfWeek = DayOfWeek.Monday)
    {
        int diff = (7 + (dateTime.DayOfWeek - startOfWeek)) % 7;
        return dateTime.AddDays(-1 * diff).Date;
    }

    public static DateTime EndOfWeek(this DateTime dateTime, DayOfWeek startOfWeek = DayOfWeek.Monday)
    {
        return dateTime.StartOfWeek(startOfWeek).AddDays(7).AddTicks(-1);
    }

    /// <summary>
    /// 当月第一天 00:00:00，保留原值的 <see cref="DateTimeKind"/>。
    /// </summary>
    /// <remarks>
    /// 必须显式传 Kind：<c>new DateTime(y, m, d)</c> 产出 <see cref="DateTimeKind.Unspecified"/>，
    /// 会把 UTC 输入降级为 Unspecified，随后 Npgsql 拒绝写入 timestamptz 列。
    /// </remarks>
    public static DateTime StartOfMonth(this DateTime dateTime)
    {
        return new DateTime(dateTime.Year, dateTime.Month, 1, 0, 0, 0, dateTime.Kind);
    }

    public static DateTime EndOfMonth(this DateTime dateTime)
    {
        return dateTime.StartOfMonth().AddMonths(1).AddTicks(-1);
    }
}