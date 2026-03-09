namespace Tnzi.AI.Tools.BuiltIn;

/// <summary>
/// Built-in datetime tools for AI agents
/// </summary>
[AIToolGroup("datetime", "DateTime Tools", "Query and calculate dates and times")]
public class DateTimeTools : IAIToolProvider
{
    private readonly ILogger<DateTimeTools> _logger;

    public DateTimeTools(ILogger<DateTimeTools> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// Get the current date and time
    /// </summary>
    [AIFunction("get_current_datetime", "Get the current date and time, optionally in a specific timezone")]
    public Task<string> GetCurrentDateTimeAsync(
        [AIParameter("timezone", "IANA timezone name, e.g. 'Asia/Shanghai', 'UTC', 'America/New_York'", false)]
        string? timezone = null,
        [AIParameter("format", "Date/time format string, e.g. 'yyyy-MM-dd HH:mm:ss'", false)]
        string? format = null)
    {
        _logger.LogDebug("Getting current datetime with timezone: {Timezone}, format: {Format}", timezone, format);

        var now = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(timezone))
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
                now = TimeZoneInfo.ConvertTimeFromUtc(now, tz);
            }
            catch (TimeZoneNotFoundException)
            {
                // Fall back to UTC
            }
        }

        format ??= "yyyy-MM-dd HH:mm:ss";
        return Task.FromResult(now.ToString(format));
    }

    /// <summary>
    /// Calculate the difference between two dates
    /// </summary>
    [AIFunction("calculate_date_difference", "Calculate the difference between two dates in days, months, and years")]
    public Task<object> CalculateDateDifferenceAsync(
        [AIParameter("start_date", "Start date in yyyy-MM-dd format")]
        string startDate,
        [AIParameter("end_date", "End date in yyyy-MM-dd format")]
        string endDate)
    {
        _logger.LogDebug("Calculating date difference between {StartDate} and {EndDate}", startDate, endDate);

        if (!DateTime.TryParse(startDate, out var start))
        {
            return Task.FromResult<object>(new { error = $"Invalid start date format: {startDate}" });
        }

        if (!DateTime.TryParse(endDate, out var end))
        {
            return Task.FromResult<object>(new { error = $"Invalid end date format: {endDate}" });
        }

        var diff = end - start;
        var totalMonths = ((end.Year - start.Year) * 12) + end.Month - start.Month;

        return Task.FromResult<object>(new
        {
            days = diff.Days,
            total_days = diff.TotalDays,
            months = totalMonths,
            years = end.Year - start.Year
        });
    }
}
