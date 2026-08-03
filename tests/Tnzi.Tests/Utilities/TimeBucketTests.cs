namespace Tnzi.Tests.Utilities;

/// <summary>
/// TimeBucket 分桶原语测试。
///
/// 重点守两件此前各模块自行实现时踩过的事：
/// ①**跨年周的标签**必须用 ISO 周年而不是日历年（2027-01-01 属于 2026-W53）；
/// ②**Kind 必须原样保留**（月桶用 <c>new DateTime(y, m, 1)</c> 会退化成 Unspecified，
/// 拿去查 PostgreSQL 的 timestamptz 会被 Npgsql 拒绝）。
/// </summary>
public class TimeBucketTests
{
    private static DateTime Utc(int y, int m, int d, int h = 13, int mi = 45)
        => new(y, m, d, h, mi, 0, DateTimeKind.Utc);

    [Fact]
    public void Start_Daily_TruncatesToMidnight()
    {
        Assert.Equal(new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc), TimeBucket.Start(Utc(2026, 7, 31), TrendInterval.Daily));
    }

    [Theory]
    // 2026-07-27 是周一，同周的每一天都应归到它
    [InlineData(2026, 7, 27)]
    [InlineData(2026, 7, 29)]
    [InlineData(2026, 8, 2)] // 周日 —— DayOfWeek.Sunday == 0，最容易算错的一天
    public void Start_Weekly_SnapsBackToMonday(int y, int m, int d)
    {
        var start = TimeBucket.Start(Utc(y, m, d), TrendInterval.Weekly);

        Assert.Equal(new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(DayOfWeek.Monday, start.DayOfWeek);
    }

    [Fact]
    public void Start_Monthly_SnapsToFirstOfMonth()
    {
        Assert.Equal(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), TimeBucket.Start(Utc(2026, 7, 31), TrendInterval.Monthly));
    }

    [Theory]
    [InlineData(TrendInterval.Daily)]
    [InlineData(TrendInterval.Weekly)]
    [InlineData(TrendInterval.Monthly)]
    public void Start_PreservesKind(TrendInterval interval)
    {
        Assert.Equal(DateTimeKind.Utc, TimeBucket.Start(Utc(2026, 7, 31), interval).Kind);
        Assert.Equal(DateTimeKind.Local, TimeBucket.Start(new DateTime(2026, 7, 31, 13, 45, 0, DateTimeKind.Local), interval).Kind);
    }

    [Fact]
    public void Label_Daily_And_Monthly_UseInvariantFormats()
    {
        Assert.Equal("2026-07-31", TimeBucket.Label(Utc(2026, 7, 31), TrendInterval.Daily));
        Assert.Equal("2026-07", TimeBucket.Label(Utc(2026, 7, 31), TrendInterval.Monthly));
    }

    [Fact]
    public void Label_Weekly_UsesIsoWeekYear_NotCalendarYear()
    {
        // 2027-01-01 是周五，属于 2026 年的 ISO 第 53 周。
        // 用日历年拼标签会得到 "2027-W53" —— 2027 年根本没有第 53 周。
        Assert.Equal("2026-W53", TimeBucket.Label(Utc(2027, 1, 1), TrendInterval.Weekly));

        // 反向：2025-12-29 是周一，属于 2026 年的 ISO 第 1 周。
        Assert.Equal("2026-W01", TimeBucket.Label(Utc(2025, 12, 29), TrendInterval.Weekly));
    }

    [Fact]
    public void Label_Weekly_IsStableWithinTheSameWeek()
    {
        var monday = Utc(2026, 7, 27);
        var sunday = Utc(2026, 8, 2);

        Assert.Equal(TimeBucket.Label(monday, TrendInterval.Weekly), TimeBucket.Label(sunday, TrendInterval.Weekly));
    }

    [Fact]
    public void Label_SortsChronologically()
    {
        // 标签会被直接拿去 OrderBy，字典序必须等于时间序（含跨年周）。
        var labels = new[]
        {
            TimeBucket.Label(Utc(2025, 12, 22), TrendInterval.Weekly),
            TimeBucket.Label(Utc(2025, 12, 29), TrendInterval.Weekly),
            TimeBucket.Label(Utc(2026, 1, 5), TrendInterval.Weekly)
        };

        Assert.Equal(labels.OrderBy(l => l, StringComparer.Ordinal).ToArray(), labels);
    }

    [Fact]
    public void Enumerate_Daily_CoversEveryDayInclusive()
    {
        var buckets = TimeBucket.Enumerate(Utc(2026, 7, 29), Utc(2026, 7, 31), TrendInterval.Daily).ToList();

        Assert.Equal(3, buckets.Count);
        Assert.Equal(new DateTime(2026, 7, 29, 0, 0, 0, DateTimeKind.Utc), buckets[0]);
        Assert.Equal(new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc), buckets[^1]);
    }

    [Fact]
    public void Enumerate_Weekly_StartsFromTheMondayOfTheOpeningWeek()
    {
        var buckets = TimeBucket.Enumerate(Utc(2026, 7, 29), Utc(2026, 8, 10), TrendInterval.Weekly).ToList();

        Assert.Equal(new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc), buckets[0]);
        Assert.Equal(3, buckets.Count); // 7/27, 8/3, 8/10
    }

    [Fact]
    public void Enumerate_Monthly_CrossesYearBoundary()
    {
        var labels = TimeBucket.Enumerate(Utc(2026, 11, 15), Utc(2027, 1, 2), TrendInterval.Monthly)
            .Select(b => TimeBucket.Label(b, TrendInterval.Monthly))
            .ToList();

        Assert.Equal(["2026-11", "2026-12", "2027-01"], labels);
    }

    [Fact]
    public void Enumerate_ReturnsEmpty_WhenRangeIsInverted()
    {
        Assert.Empty(TimeBucket.Enumerate(Utc(2026, 7, 31), Utc(2026, 7, 1), TrendInterval.Daily));
    }

    [Fact]
    public void Enumerate_SingleInstant_YieldsOneBucket()
    {
        Assert.Single(TimeBucket.Enumerate(Utc(2026, 7, 31), Utc(2026, 7, 31), TrendInterval.Daily));
    }
}
