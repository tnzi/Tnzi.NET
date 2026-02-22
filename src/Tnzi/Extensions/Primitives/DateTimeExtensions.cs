
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

    public static DateTime StartOfMonth(this DateTime dateTime)
    {
        return new DateTime(dateTime.Year, dateTime.Month, 1);
    }

    public static DateTime EndOfMonth(this DateTime dateTime)
    {
        return dateTime.StartOfMonth().AddMonths(1).AddTicks(-1);
    }
}