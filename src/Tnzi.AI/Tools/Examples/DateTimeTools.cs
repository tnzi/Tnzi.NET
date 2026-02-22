
namespace Tnzi.AI.Tools.Examples;

/// <summary>
/// 日期时间工具组 - 提供日期时间相关的 AI 工具函数
/// </summary>
[AIToolGroup("datetime", "日期时间工具", "提供日期时间查询和计算功能")]
public class DateTimeTools : IAIToolProvider
{
    private readonly ILogger<DateTimeTools> _logger;

    public DateTimeTools(ILogger<DateTimeTools> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 获取当前日期时间
    /// </summary>
    [AIFunction("get_current_datetime", "获取当前日期和时间")]
    public Task<string> GetCurrentDateTimeAsync(
        [AIParameter("timezone", "时区名称，如 'Asia/Shanghai'、'UTC'", false)]
        string? timezone = null,
        [AIParameter("format", "日期时间格式，如 'yyyy-MM-dd HH:mm:ss'", false)]
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
                // 使用 UTC
            }
        }

        format ??= "yyyy-MM-dd HH:mm:ss";
        return Task.FromResult(now.ToString(format));
    }

    /// <summary>
    /// 计算两个日期之间的差异
    /// </summary>
    [AIFunction("calculate_date_difference", "计算两个日期之间的差异")]
    public Task<object> CalculateDateDifferenceAsync(
        [AIParameter("start_date", "开始日期，格式：yyyy-MM-dd")]
        string startDate,
        [AIParameter("end_date", "结束日期，格式：yyyy-MM-dd")]
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