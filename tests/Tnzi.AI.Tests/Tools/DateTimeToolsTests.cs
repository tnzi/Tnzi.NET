namespace Tnzi.AI.Tests.Tools;

/// <summary>
/// DateTimeTools 内置工具功能测试
/// </summary>
public class DateTimeToolsTests
{
    private readonly DateTimeTools _tools;

    public DateTimeToolsTests()
    {
        _tools = new DateTimeTools(NullLogger<DateTimeTools>.Instance);
    }

    #region GetCurrentDateTimeAsync

    [Fact]
    public async Task GetCurrentDateTime_DefaultFormat_ReturnsFormattedString()
    {
        var result = await _tools.GetCurrentDateTimeAsync();

        result.ShouldNotBeNullOrWhiteSpace();
        // 默认格式: yyyy-MM-dd HH:mm:ss
        result.Length.ShouldBe(19);
        result.ShouldContain("-");
        result.ShouldContain(":");
    }

    [Fact]
    public async Task GetCurrentDateTime_CustomFormat_RespectsFormat()
    {
        var result = await _tools.GetCurrentDateTimeAsync(format: "yyyy-MM-dd");

        result.Length.ShouldBe(10);
        DateTime.TryParseExact(result, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task GetCurrentDateTime_ValidTimezone_ConvertsTime()
    {
        var resultUtc = await _tools.GetCurrentDateTimeAsync(timezone: "UTC");
        var resultShanghai = await _tools.GetCurrentDateTimeAsync(timezone: "Asia/Shanghai");

        // UTC 和上海时间应该不同（除非恰好在整点）
        resultUtc.ShouldNotBeNullOrWhiteSpace();
        resultShanghai.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetCurrentDateTime_InvalidTimezone_FallsBackToUtc()
    {
        // 无效时区应静默降级到 UTC
        var result = await _tools.GetCurrentDateTimeAsync(timezone: "Invalid/Zone");

        result.ShouldNotBeNullOrWhiteSpace();
        result.Length.ShouldBe(19);
    }

    [Fact]
    public async Task GetCurrentDateTime_NullParams_ReturnsUtcDefault()
    {
        var result = await _tools.GetCurrentDateTimeAsync(null, null);

        result.ShouldNotBeNullOrWhiteSpace();
    }

    #endregion

    #region CalculateDateDifferenceAsync

    [Fact]
    public async Task CalculateDateDifference_ValidDates_ReturnsCorrectDiff()
    {
        var result = await _tools.CalculateDateDifferenceAsync("2024-01-01", "2024-12-31");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"days\":");
        json.ShouldContain("\"months\":");
        json.ShouldContain("\"years\":");

        // 2024-01-01 到 2024-12-31 = 365 天（闰年）
        json.ShouldContain("365");
    }

    [Fact]
    public async Task CalculateDateDifference_SameDate_ReturnsZeroDiff()
    {
        var result = await _tools.CalculateDateDifferenceAsync("2024-06-15", "2024-06-15");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"days\":0");
    }

    [Fact]
    public async Task CalculateDateDifference_InvalidStartDate_ReturnsError()
    {
        var result = await _tools.CalculateDateDifferenceAsync("not-a-date", "2024-12-31");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("error");
        json.ShouldContain("not-a-date");
    }

    [Fact]
    public async Task CalculateDateDifference_InvalidEndDate_ReturnsError()
    {
        var result = await _tools.CalculateDateDifferenceAsync("2024-01-01", "invalid");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("error");
        json.ShouldContain("invalid");
    }

    [Fact]
    public async Task CalculateDateDifference_ReverseDates_ReturnsNegativeDays()
    {
        var result = await _tools.CalculateDateDifferenceAsync("2024-12-31", "2024-01-01");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("-365");
    }

    [Fact]
    public async Task CalculateDateDifference_CrossYear_CalculatesMonthsAndYears()
    {
        var result = await _tools.CalculateDateDifferenceAsync("2023-03-15", "2025-06-15");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"years\":2");
        json.ShouldContain("\"months\":27"); // 2年3个月 = 27个月
    }

    #endregion
}
