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
    [AIFunction("get_current_datetime", "Get the current date and time, optionally in a specific timezone",
        IsReadOnly = true, IsConcurrencySafe = true)]
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
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // Fall back to UTC
            }
        }

        format ??= "yyyy-MM-dd HH:mm:ss";

        // format 由 LLM 提供：非法格式串会抛 FormatException 并中断 Agent 执行流，
        // 按工具约定返回错误文本而不是抛出。固定 InvariantCulture，避免当前请求区域
        // 使用非公历日历（如 th-TH）导致年份错乱。
        try
        {
            return Task.FromResult(now.ToString(format, CultureInfo.InvariantCulture));
        }
        catch (FormatException)
        {
            return Task.FromResult($"Invalid format string '{format}'. Use a .NET date/time format such as 'yyyy-MM-dd HH:mm:ss'.");
        }
    }

    /// <summary>
    /// Calculate the difference between two dates
    /// </summary>
    [AIFunction("calculate_date_difference", "Calculate the difference between two dates in days, months, and years",
        IsReadOnly = true, IsConcurrencySafe = true)]
    public Task<object> CalculateDateDifferenceAsync(
        [AIParameter("start_date", "Start date in yyyy-MM-dd format")]
        string startDate,
        [AIParameter("end_date", "End date in yyyy-MM-dd format")]
        string endDate)
    {
        _logger.LogDebug("Calculating date difference between {StartDate} and {EndDate}", startDate, endDate);

        // 文档约定入参是 yyyy-MM-dd，用 InvariantCulture 解析，
        // 避免当前请求区域的日历/分隔符差异把同一字符串解析成不同日期。
        if (!DateTime.TryParse(startDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
        {
            return Task.FromResult<object>(new { error = $"Invalid start date format: {startDate}" });
        }

        if (!DateTime.TryParse(endDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
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
